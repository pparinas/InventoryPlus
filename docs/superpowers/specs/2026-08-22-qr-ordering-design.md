# QR Menu & Ordering — Design

Status: approved by user, pending spec review
Author: Claude (session with Paul), 2026-08-22

## 1. Purpose

Let a business's customers scan a QR code, view a live menu (current
products, prices, availability), place an order with their name and
optional table/note, and get an order number. The order appears as a
**pending order** in the staff app in real time; staff can edit it
(fix quantities, drop an item that just sold out) and then confirm
payment (cash/GCash, collected at the counter — no online payment
gateway), which converts it into a normal completed Sale using the
existing stock-deduction path.

This is a Pro-only feature, matching the app's existing free/Pro
gating pattern (POS Mode, PIN protection).

## 2. Out of scope for v1

- Online/in-app payment (gateway integration, refunds) — explicitly
  deferred; payment is always collected at the counter.
- Live order-status tracking for the customer (Pending → Preparing →
  Ready) — the customer gets one confirmation screen, no polling.
- Order editing by the customer after submission.
- Multi-language menu, delivery/pickup distinction, scheduled orders.
- Printing a kitchen ticket automatically (staff can already print via
  the existing receipt flow if needed).

## 2.1 Prerequisite: persist `Product.Category`

Caught during spec review: `Product.Category` is currently
`[JsonIgnore]`d and never saved to the database (a `// To persist:
add 'category' column...` comment already flags this in
`ProductModel.cs`) — every product silently reports "Other" once
reloaded. Grouping the public menu by category would be pointless
without fixing this first, so this spec includes it as a small
prerequisite: add the `category` column, uncomment the mapping, wire
the existing (already-built) Category input in the product
form to actually persist. Small, low-risk, and worth doing before the
menu groups by it.

## 3. Pages & flow

### 3.1 `/menu/{slug}` — public menu (new, unauthenticated)

New layout (`PublicMenuLayout`, sibling to `LandingLayout`) since it
must render for anonymous visitors and carries no sidebar/topbar.

- Resolves `{slug}` → `owner_guid` via `account_settings.menu_slug`.
  404-style "Menu not found" state if no match, or if the owning
  account isn't Pro (a lapsed subscription should take the menu down,
  not error obscurely).
- Shows business name/logo (from `account_settings`), products grouped
  by `category`, each with price and, if present, `recipe` shown as a
  customer-facing description.
- Availability mirrors the existing `Product.AvailableCount` /
  "not tracked vs out" logic already fixed in Products: an
  out-of-stock or zero-available tracked product is shown greyed out
  and not addable to cart. A "not tracked" product (always available)
  is always orderable.
- Client-side cart (same mental model as the existing POS cart:
  add/remove, qty stepper, running total). No login.
- Checkout step: name (required), table/note (optional), review
  totals, "Place Order".
- On submit: inserts one `orders` row + N `order_items` rows, gets
  back the generated `order_number`, navigates to
  `/order/{owner_slug}/{order_number}`.

### 3.2 `/order/{slug}/{order_number}` — confirmation (new, unauthenticated)

Static confirmation: order number (large, easy to read to staff),
items, total, "Show this screen to staff" messaging. No realtime
subscription here — v1 has no live tracking.

### 3.3 Staff: Pending Orders (new tab within Sales/POS)

- Added as a third mode alongside the existing Sales list / POS Mode,
  gated by `AppSettings.IsPro` the same way POS Mode already is.
- List of orders with `status = 'pending'`, newest first, each
  showing customer name, table/note, items, total, and how long ago
  it came in.
- Supabase Realtime subscription on `orders` filtered to the signed-in
  `owner_guid` — new pending orders and edits appear without a
  refresh. Falls back gracefully (page still works, just needs a
  manual refresh) if the realtime channel fails to connect — matches
  the app's existing `IsOffline` pattern for degraded connectivity.
- **Edit**: adjust quantities or remove line items in place (no adding
  new items in v1 — if a customer wants something not ordered, staff
  adds it as a normal walk-in sale instead).
- **Confirm Payment**: choose Cash/GCash (reusing the existing
  `PaymentMethod` values from POS), then:
  1. For each `order_item`, call the existing `record_sale_with_stock`
     RPC (same one POS checkout already uses) — this handles stock
     deduction and Sale-row creation unchanged.
  2. Mark the `orders` row `status = 'completed'`.
  - If any single item's RPC call fails (e.g. stock changed
    concurrently), stop, surface which item failed via a toast, leave
    the order `pending` with the remaining items intact so staff can
    retry — no partial silent state.
- **Cancel**: marks `status = 'cancelled'`, no stock/sale impact. For
  a customer who never shows up.

## 4. Data model

```sql
-- One new column on the existing table
alter table account_settings add column menu_slug text unique;

create table orders (
  guid uuid primary key default gen_random_uuid(),
  owner_guid uuid not null references auth.users(id),
  order_number int not null,
  customer_name text not null,
  table_note text not null default '',
  status text not null default 'pending', -- 'pending' | 'completed' | 'cancelled'
  total_amount numeric not null default 0,
  created_at timestamptz not null default now(),
  unique (owner_guid, order_number)
);

create table order_items (
  guid uuid primary key default gen_random_uuid(),
  order_id uuid not null references orders(guid) on delete cascade,
  product_id uuid not null,
  product_name text not null,   -- snapshotted at order time
  unit_price numeric not null,  -- snapshotted at order time
  quantity int not null
);
```

`order_number` is per-owner sequential (not a global auto-increment,
so numbers stay small and readable — "Order #7", not "Order
#48213"). Simplest implementation: a Postgres function that takes
`max(order_number) + 1` for that `owner_guid` inside the insert
transaction; a rare double-submit race is acceptable to resolve with a
retry-on-conflict given the `unique (owner_guid, order_number)`
constraint, rather than adding a separate sequence-per-owner table for
v1.

### RLS policies (new)

```sql
-- Public read: only active products/ingredients for accounts with a
-- published slug, so an anon visitor can render the menu.
create policy "public menu read - products" on products
  for select using (
    not is_archived
    and owner_guid in (select owner_guid from account_settings where menu_slug is not null)
  );

create policy "public menu read - account_settings" on account_settings
  for select using (menu_slug is not null);

-- Public write: anon can create orders/order_items, but cannot read,
-- update, or delete them -- a customer can place an order but can't
-- browse or tamper with anyone else's.
create policy "public order insert" on orders
  for insert with check (true);

create policy "public order_items insert" on order_items
  for insert with check (true);

-- Owner read/update: only the authenticated business owner can see
-- and manage their own orders (existing owner_guid = auth.uid() shape
-- used everywhere else in this app).
create policy "owner manage orders" on orders
  for all using (owner_guid = auth.uid());

create policy "owner manage order_items" on order_items
  for all using (order_id in (select guid from orders where owner_guid = auth.uid()));
```

This is the biggest risk area in the design: RLS mistakes here could
either leak one business's orders to another, or block legitimate
access. Needs careful review before applying, and testing with a
second test account to confirm cross-tenant isolation before this
ships.

## 5. Error handling

- Slug not found / account not Pro → friendly "This menu isn't
  available right now" page, not a raw 404.
- Cart submit fails (network, RLS misconfiguration) → inline error on
  the checkout step, cart state preserved so the customer doesn't lose
  their selections.
- Realtime channel drops → Pending Orders list keeps working via the
  page's normal on-load fetch; a subtle "reconnecting" indicator
  rather than a hard failure state.
- Confirm Payment partial failure → as described in 3.3, order stays
  pending with a clear per-item error rather than silently completing
  a partial sale.

## 6. Testing

- Cross-tenant isolation: two Pro test accounts, confirm account A's
  staff view never shows account B's orders and vice versa.
- Non-Pro account: `/menu/{slug}` shows the unavailable page even if a
  slug was set before a subscription lapsed.
- Concurrent order-number generation (two orders submitted at nearly
  the same time) doesn't produce a duplicate `order_number`.
- Stock race: two customers order the last unit of the same item
  before staff confirms either — second Confirm Payment fails
  gracefully per item, doesn't oversell.
- Mobile: the public menu is the one page in this whole app where
  *most* traffic will be a customer's phone, not staff's — needs to be
  verified on a real narrow viewport, not just adapted from the
  staff-side card patterns.
