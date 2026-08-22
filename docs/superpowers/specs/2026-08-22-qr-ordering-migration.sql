-- QR Menu & Ordering: run this entire file in the Supabase SQL Editor
-- after all app code (Tasks 1-9) has been built successfully.

-- Task 1 prerequisite: persist product category
alter table products add column if not exists category text not null default 'Other';

-- Task 2/5: menu slug on account settings
alter table account_settings add column if not exists menu_slug text unique;

-- Task 2: orders + order_items
create table if not exists orders (
  guid uuid primary key default gen_random_uuid(),
  owner_guid uuid not null references auth.users(id),
  order_number int not null,
  customer_name text not null,
  table_note text not null default '',
  status text not null default 'pending',
  total_amount numeric not null default 0,
  created_at timestamptz not null default now(),
  unique (owner_guid, order_number)
);

create table if not exists order_items (
  guid uuid primary key default gen_random_uuid(),
  order_id uuid not null references orders(guid) on delete cascade,
  product_id uuid not null,
  product_name text not null,
  unit_price numeric not null,
  quantity int not null
);

-- Realtime requires full row data on updates/deletes for the staff
-- subscription in OrderService.SubscribeRealtime to work correctly.
alter table orders replica identity full;

-- Enable RLS (tables are created with it off by default)
alter table orders enable row level security;
alter table order_items enable row level security;

-- Public (anon) read access for the menu page -- ONLY for accounts
-- with a published slug, and only non-archived rows.
create policy "public menu read - products" on products
  for select using (
    not is_archived
    and owner_guid in (select owner_guid from account_settings where menu_slug is not null)
  );

create policy "public menu read - account_settings" on account_settings
  for select using (menu_slug is not null);

-- Public (anon) write-only access to place orders -- no anon select,
-- update, or delete, so a customer can create an order but can never
-- browse, edit, or cancel anyone else's.
create policy "public order insert" on orders
  for insert with check (true);

create policy "public order_items insert" on order_items
  for insert with check (true);

-- Owner (authenticated) full access to their own orders -- same
-- owner_guid = auth.uid() shape used by every other table in this app.
create policy "owner manage orders" on orders
  for all using (owner_guid = auth.uid());

create policy "owner manage order_items" on order_items
  for all using (order_id in (select guid from orders where owner_guid = auth.uid()));

-- Enable realtime publication for the orders table (Supabase dashboard
-- equivalent: Database > Replication > toggle "orders" on). This SQL
-- form works if the supabase_realtime publication already exists,
-- which it does by default on every Supabase project.
alter publication supabase_realtime add table orders;
