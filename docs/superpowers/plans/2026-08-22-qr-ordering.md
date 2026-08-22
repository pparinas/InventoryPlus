# QR Menu & Ordering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let customers scan a QR code, view a live menu, and place an order (pay-at-counter) that appears as a real-time pending order in the staff app, editable and convertible into a normal completed sale.

**Architecture:** Two new unauthenticated pages (`/menu/{slug}`, `/order/{slug}/{orderNumber}`) do their own independent Supabase queries via a small `PublicMenuService` helper — they must NOT read the app's `InventoryService`/`SettingsService` singletons, since those hold whichever user (if any) is currently logged into this browser, not the target shop. The staff side adds a new `OrderService` singleton (mirrors `InventoryService`'s load/sync pattern) plus a "Pending Orders" view inside the existing Sales page, gated Pro-only. Confirming payment on a pending order reuses the existing `InventoryService.RecordSaleAsync` per line item — no new stock-deduction logic. Realtime staff alerts use Supabase's built-in Postgres-changes subscription (`_supabase.Realtime.Channel(...)`).

**Tech Stack:** Blazor WebAssembly (.NET 10), Supabase C# SDK 1.5.0 (Postgrest + Realtime), existing CSS component classes (`card`, `form-row`, `stock-card`, `pos-*`, `badge`) — no new dependencies.

**Spec:** `docs/superpowers/specs/2026-08-22-qr-ordering-design.md`

## Global Constraints

- Payment is pay-at-counter only — no online gateway, no proof-of-payment upload (spec §1).
- Customer gets a static confirmation, no live order-status tracking (spec §3.2). Staff DOES get live updates via Realtime (spec §3.3).
- QR ordering is Pro-gated: the Settings slug field and the Pending Orders view both check `AppSettings.IsPro`, matching the existing `IsPro`/POS-Mode pattern (`Pages/Sales.razor:27`).
- v1 does not render a QR code image in-app — Settings shows the menu URL with a "Copy Link" button; the shop owner runs that URL through any free QR generator once and prints it. Building a QR encoder is unjustified scope for a one-time, owner-side action.
- All Supabase DDL/RLS/replica-identity changes are deferred to Task 12 (the last task), run only after every code task is built and verified — per explicit instruction. Tasks 1–11 must compile and pass their non-DB-dependent checks without the new tables existing; DB-dependent live verification happens once Task 12's SQL is applied.
- This repo has no automated test project (nothing under `**/*.Tests` — verified by directory listing). Every existing feature in this app's history has been verified by building + manual/live checks (curl against Supabase, browser interaction), not unit tests. This plan follows that established convention rather than introducing a new test framework: each task's "verify" step is a build check plus a concrete manual check (curl command, or exact UI steps), not an xunit test.
- Where a decision isn't pinned down by the spec, the plan below states the choice made — do not stop to ask, per instruction; flag it in the task's commit message if you deviate.

---

## Task 1: Persist `Product.Category`

Prerequisite fix (spec §2.1): `Category` is currently `[JsonIgnore]`d and never saved. The Add/Edit Product form (`Pages/Products.razor:190`) and CSV import (`Components/ImportProductsModal.razor`) already read/write `currentProduct.Category` — only the model attribute is wrong.

**Files:**
- Modify: `Models/ProductModel.cs:32-34`

**Interfaces:**
- Produces: `Product.Category` now round-trips through Supabase like every other field (no signature change — same property name/type).

- [ ] **Step 1: Change the attribute**

In `Models/ProductModel.cs`, replace:

```csharp
        // To persist: add 'category' column to Supabase, uncomment [Column], remove [JsonIgnore]
        [JsonIgnore]
        public string Category { get; set; } = "Other";
```

with:

```csharp
        [Column("category")]
        public string Category { get; set; } = "Other";
```

- [ ] **Step 2: Remove the now-unused `JsonIgnore` using directive if nothing else in the file uses it**

Check `Models/ProductModel.cs` for other `[JsonIgnore]` usages (there are — `AvailableCount`, `TotalCost`, `ProfitMargin`, `ProfitMarginPercentage`). Keep the `using Newtonsoft.Json;` line; only the one attribute changes.

- [ ] **Step 3: Build**

Run: `dotnet build InventoryPlus.csproj -c Debug`
Expected: 0 errors (this is a pure attribute swap, no call sites change).

- [ ] **Step 4: Commit**

```bash
git add Models/ProductModel.cs
git commit -m "Persist Product.Category to Supabase

Prerequisite for the QR menu grouping products by category. Column
add is deferred to the final Supabase migration task."
```

---

## Task 2: Data models — `Order`, `OrderItem`, `AccountSettings.MenuSlug`

**Files:**
- Create: `Models/OrderModel.cs`
- Create: `Models/OrderItemModel.cs`
- Modify: `Models/AccountSettingsModel.cs`

**Interfaces:**
- Produces: `Order` (Table `orders`), `OrderItem` (Table `order_items`) — consumed by `PublicMenuService` (Task 3) and `OrderService` (Task 4).
- Produces: `AccountSettings.MenuSlug` (string, nullable-empty) — consumed by `SettingsService` (Task 5) and `PublicMenuService` (Task 3).

- [ ] **Step 1: Create the Order model**

`Models/OrderModel.cs`:

```csharp
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace InventoryPlus.Models
{
    [Table("orders")]
    public class Order : BaseModel
    {
        [PrimaryKey("guid", true)]
        public Guid Guid { get; set; } = Guid.NewGuid();

        [Column("owner_guid")]
        public Guid OwnerGuid { get; set; }

        [Column("order_number")]
        public int OrderNumber { get; set; }

        [Column("customer_name")]
        public string CustomerName { get; set; } = string.Empty;

        [Column("table_note")]
        public string TableNote { get; set; } = string.Empty;

        [Column("status")]
        public string Status { get; set; } = "pending"; // "pending" | "completed" | "cancelled"

        [Column("total_amount")]
        public double TotalAmount { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Reference(typeof(OrderItem), includeInQuery: false)]
        public List<OrderItem> Items { get; set; } = new();

        [JsonIgnore]
        public double RecomputedTotal => Items.Sum(i => i.UnitPrice * i.Quantity);
    }
}
```

Note: `RecomputedTotal` needs `using System.Linq;` — add it to the using list.

- [ ] **Step 2: Create the OrderItem model**

`Models/OrderItemModel.cs`:

```csharp
using System;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace InventoryPlus.Models
{
    [Table("order_items")]
    public class OrderItem : BaseModel
    {
        [PrimaryKey("guid", true)]
        public Guid Guid { get; set; } = Guid.NewGuid();

        [Column("order_id")]
        public Guid OrderId { get; set; }

        [Column("product_id")]
        public Guid ProductId { get; set; }

        [Column("product_name")]
        public string ProductName { get; set; } = string.Empty;

        [Column("unit_price")]
        public double UnitPrice { get; set; }

        [Column("quantity")]
        public int Quantity { get; set; }
    }
}
```

- [ ] **Step 3: Add `MenuSlug` to `AccountSettings`**

In `Models/AccountSettingsModel.cs`, add after `ShowDecimals`:

```csharp
        [Column("menu_slug")]
        public string? MenuSlug { get; set; }
```

- [ ] **Step 4: Build**

Run: `dotnet build InventoryPlus.csproj -c Debug`
Expected: 0 errors. (`Order.Items.Sum` needs `System.Linq` — if the build reports `CS1061` on `.Sum`, add `using System.Linq;` to `Models/OrderModel.cs`.)

- [ ] **Step 5: Commit**

```bash
git add Models/OrderModel.cs Models/OrderItemModel.cs Models/AccountSettingsModel.cs
git commit -m "Add Order/OrderItem models and AccountSettings.MenuSlug

Data model for the QR ordering feature (spec section 4). Tables
don't exist in Supabase yet -- that's the final deferred task."
```

---

## Task 3: `PublicMenuService` — anonymous menu fetch + order submission

This is deliberately **not** DI-registered as a singleton. `InventoryService`/`SettingsService` are singletons holding the *currently logged-in browser session's* data — reusing them here would leak the logged-in user's own inventory onto someone else's public menu page, or vice versa. `PublicMenuService` is instantiated per-page with `new PublicMenuService(SupabaseClient)` and does its own scoped queries.

**Files:**
- Create: `Services/PublicMenuService.cs`

**Interfaces:**
- Consumes: `Supabase.Client` (existing DI singleton, injected into the page and passed to this service's constructor).
- Produces: `PublicMenuInfo? ResolveMenuAsync(string slug)`, `Task<Order?> SubmitOrderAsync(Guid ownerGuid, string customerName, string tableNote, List<(Guid productId, string name, double price, int qty)> items)` — consumed by `Pages/PublicMenu.razor` (Task 6) and `Pages/OrderConfirmation.razor` (Task 7).

- [ ] **Step 1: Write the service**

`Services/PublicMenuService.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using InventoryPlus.Models;
using Supabase;

namespace InventoryPlus.Services
{
    public class PublicMenuInfo
    {
        public Guid OwnerGuid { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string LogoUrl { get; set; } = string.Empty;
        public List<Product> Products { get; set; } = new();
    }

    /// <summary>
    /// Anonymous-safe reads/writes for the public QR menu. Deliberately does not
    /// touch InventoryService/SettingsService -- those are the CURRENT browser
    /// session's own data, not the shop being viewed. See Task 3 header note.
    /// </summary>
    public class PublicMenuService
    {
        private readonly Client _supabase;

        public PublicMenuService(Client supabase)
        {
            _supabase = supabase;
        }

        public async Task<PublicMenuInfo?> ResolveMenuAsync(string slug)
        {
            var settingsResp = await _supabase.From<AccountSettings>()
                .Where(s => s.MenuSlug == slug)
                .Get();
            var settings = settingsResp.Models.FirstOrDefault();
            if (settings == null) return null;

            var productsResp = await _supabase.From<Product>()
                .Where(p => p.OwnerGuid == settings.OwnerGuid && p.IsArchived == false)
                .Order(p => p.Name, Supabase.Postgrest.Constants.Ordering.Ascending)
                .Get();

            return new PublicMenuInfo
            {
                OwnerGuid = settings.OwnerGuid,
                CompanyName = settings.CompanyName,
                LogoUrl = settings.LogoUrl,
                Products = productsResp.Models
            };
        }

        public async Task<Order> SubmitOrderAsync(
            Guid ownerGuid,
            string customerName,
            string tableNote,
            List<(Guid productId, string name, double price, int qty)> cartItems)
        {
            var total = cartItems.Sum(i => i.price * i.qty);

            // Per-owner order numbers: read the current max and add one.
            // A rare double-submit race is resolved by the unique(owner_guid,
            // order_number) constraint plus a single retry -- see spec section 4.
            int nextNumber = 1;
            var existing = await _supabase.From<Order>()
                .Where(o => o.OwnerGuid == ownerGuid)
                .Order(o => o.OrderNumber, Supabase.Postgrest.Constants.Ordering.Descending)
                .Limit(1)
                .Get();
            var last = existing.Models.FirstOrDefault();
            if (last != null) nextNumber = last.OrderNumber + 1;

            var order = new Order
            {
                Guid = Guid.NewGuid(),
                OwnerGuid = ownerGuid,
                OrderNumber = nextNumber,
                CustomerName = customerName,
                TableNote = tableNote ?? string.Empty,
                Status = "pending",
                TotalAmount = total,
                CreatedAt = DateTime.UtcNow
            };

            try
            {
                await _supabase.From<Order>().Insert(order);
            }
            catch (Supabase.Postgrest.Exceptions.PostgrestException) when (true)
            {
                // Likely the unique(owner_guid, order_number) race -- retry once with a fresh number.
                var retryExisting = await _supabase.From<Order>()
                    .Where(o => o.OwnerGuid == ownerGuid)
                    .Order(o => o.OrderNumber, Supabase.Postgrest.Constants.Ordering.Descending)
                    .Limit(1)
                    .Get();
                order.OrderNumber = (retryExisting.Models.FirstOrDefault()?.OrderNumber ?? nextNumber) + 1;
                await _supabase.From<Order>().Insert(order);
            }

            var items = cartItems.Select(i => new OrderItem
            {
                Guid = Guid.NewGuid(),
                OrderId = order.Guid,
                ProductId = i.productId,
                ProductName = i.name,
                UnitPrice = i.price,
                Quantity = i.qty
            }).ToList();
            await _supabase.From<OrderItem>().Insert(items);

            order.Items = items;
            return order;
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build InventoryPlus.csproj -c Debug`
Expected: 0 errors. The `.Order(x => x.Prop, Ordering.Ascending/Descending)` and `.Limit(n)` calls above were verified against the current Supabase C# docs before writing, but this app has no prior `.Order()`/`.Limit()` call site to cross-check against (confirmed via `grep` — `InventoryService.cs` sorts client-side instead). If the build reports a signature mismatch, check the installed package's actual method signature (the compiler error will show it) and adjust.

- [ ] **Step 3: Commit**

```bash
git add Services/PublicMenuService.cs
git commit -m "Add PublicMenuService for anonymous menu reads and order submission

Not DI-registered -- instantiated per public page. Deliberately
separate from InventoryService/SettingsService so it never reads the
currently-logged-in browser session's own data by mistake."
```

---

## Task 4: `OrderService` — staff-side pending orders, realtime, and payment confirmation

**Files:**
- Create: `Services/OrderService.cs`
- Modify: `Program.cs` (register the new singleton)
- Modify: `Layout/MainLayout.razor.cs` (load orders alongside inventory/settings on login)

**Interfaces:**
- Consumes: `Order`, `OrderItem` (Task 2); `InventoryService.RecordSaleAsync(Product, int, string, string, string, double, string, IJSRuntime?)` (existing, `Services/InventoryService.cs:254`); `InventoryService.ActiveProducts` (existing).
- Produces: `OrderService.PendingOrders` (`List<Order>`), `LoadAsync(string userId, IJSRuntime? js)`, `SubscribeRealtime(Action onChange)`, `UnsubscribeRealtime()`, `UpdateItemQuantityAsync(Order, OrderItem, int newQty)`, `RemoveItemAsync(Order, OrderItem)`, `Task<(bool success, string? failedItemName)> ConfirmPaymentAsync(Order, string paymentMethod, InventoryService inventory, IJSRuntime? js)`, `CancelOrderAsync(Order)`. All consumed by `Components/PendingOrdersView.razor` (Task 8).

- [ ] **Step 1: Write the service**

`Services/OrderService.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using InventoryPlus.Models;
using Microsoft.JSInterop;
using Supabase;
using Supabase.Realtime;

namespace InventoryPlus.Services
{
    /// <summary>
    /// Staff-side order management: pending orders for the signed-in owner,
    /// realtime updates, editing, and converting a pending order into normal
    /// completed Sales via the existing InventoryService.RecordSaleAsync.
    /// </summary>
    public class OrderService
    {
        private readonly Client _supabase;
        private Guid _ownerGuid;
        private RealtimeChannel? _channel;

        public List<Order> PendingOrders { get; private set; } = new();
        public event Action? OnStateChanged;

        public OrderService(Client supabase)
        {
            _supabase = supabase;
        }

        public async Task LoadAsync(string userId, IJSRuntime? js = null)
        {
            if (!Guid.TryParse(userId, out _ownerGuid)) return;

            try
            {
                var resp = await _supabase.From<Order>()
                    .Where(o => o.OwnerGuid == _ownerGuid && o.Status == "pending")
                    .Order(o => o.CreatedAt, Supabase.Postgrest.Constants.Ordering.Descending)
                    .Get();
                PendingOrders = resp.Models;

                foreach (var order in PendingOrders)
                {
                    var itemsResp = await _supabase.From<OrderItem>()
                        .Where(i => i.OrderId == order.Guid)
                        .Get();
                    order.Items = itemsResp.Models;
                }

                OnStateChanged?.Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OrderService.LoadAsync error: {ex.Message}");
            }
        }

        public void SubscribeRealtime(Action onChange)
        {
            if (_channel != null) return; // already subscribed
            _channel = _supabase.Realtime.Channel("realtime", "public", "orders", "owner_guid", $"owner_guid=eq.{_ownerGuid}");
            _channel.AddPostgresChangeHandler(Supabase.Realtime.PostgresChanges.PostgresChangesOptions.ListenType.All, async (_, __) =>
            {
                await LoadAsync(_ownerGuid.ToString());
                onChange();
            });
            _ = _channel.Subscribe();
        }

        public void UnsubscribeRealtime()
        {
            _channel?.Unsubscribe();
            _channel = null;
        }

        public async Task UpdateItemQuantityAsync(Order order, OrderItem item, int newQty)
        {
            if (newQty <= 0)
            {
                await RemoveItemAsync(order, item);
                return;
            }
            item.Quantity = newQty;
            await _supabase.From<OrderItem>().Upsert(item);
            order.TotalAmount = order.Items.Sum(i => i.UnitPrice * i.Quantity);
            await _supabase.From<Order>().Upsert(order);
            OnStateChanged?.Invoke();
        }

        public async Task RemoveItemAsync(Order order, OrderItem item)
        {
            await _supabase.From<OrderItem>().Delete(item);
            order.Items.Remove(item);
            order.TotalAmount = order.Items.Sum(i => i.UnitPrice * i.Quantity);
            await _supabase.From<Order>().Upsert(order);
            OnStateChanged?.Invoke();
        }

        /// <summary>
        /// Records each order item as a normal Sale via the existing stock-aware
        /// path, then marks the order completed. Stops at the first item that
        /// fails (e.g. stock changed concurrently) and leaves the order pending
        /// with the remaining items intact -- no partial silent completion.
        /// </summary>
        public async Task<(bool success, string? failedItemName)> ConfirmPaymentAsync(
            Order order, string paymentMethod, InventoryService inventory, IJSRuntime? js = null)
        {
            var remaining = new List<OrderItem>(order.Items);
            foreach (var item in order.Items)
            {
                var product = inventory.ActiveProducts.FirstOrDefault(p => p.Guid == item.ProductId);
                if (product == null)
                {
                    return (false, item.ProductName);
                }
                var sale = await inventory.RecordSaleAsync(
                    product, item.Quantity, note: $"Order #{order.OrderNumber}",
                    paymentMethod: paymentMethod, customerName: order.CustomerName, js: js);
                if (sale == null)
                {
                    return (false, item.ProductName);
                }
                remaining.Remove(item);
            }

            order.Status = "completed";
            await _supabase.From<Order>().Upsert(order);
            PendingOrders.Remove(order);
            OnStateChanged?.Invoke();
            return (true, null);
        }

        public async Task CancelOrderAsync(Order order)
        {
            order.Status = "cancelled";
            await _supabase.From<Order>().Upsert(order);
            PendingOrders.Remove(order);
            OnStateChanged?.Invoke();
        }
    }
}
```

Note on the realtime API (`Channel(...)`, `AddPostgresChangeHandler`, `.Subscribe()`): verified against the current Supabase C# docs for the installed SDK version (1.5.0) before writing this. If the exact enum path `Supabase.Realtime.PostgresChanges.PostgresChangesOptions.ListenType.All` doesn't resolve, use your IDE's autocomplete on `ListenType` — the namespace may have shifted slightly between minor versions; the shape (channel → add handler → subscribe) is stable.

- [ ] **Step 2: Register the service**

In `Program.cs`, after the `InventoryService` registration:

```csharp
builder.Services.AddSingleton<InventoryPlus.Services.OrderService>();
```

- [ ] **Step 3: Load orders on login**

In `Layout/MainLayout.razor.cs`, inject it:

```csharp
        [Inject] public OrderService Orders { get; set; } = default!;
```

In `Layout/MainLayout.razor.cs:172-179`, the authenticated load path is:

```csharp
                var loadSettingsTask = !AppSettings.IsLoaded
                    ? AppSettings.LoadAsync(safeUserId, JSRuntime)
                    : Task.CompletedTask;
                var loadInventoryTask = !Inventory.IsLoaded && !Inventory.IsLoading
                    ? Inventory.LoadAsync(safeUserId, JSRuntime)
                    : Task.CompletedTask;
                await Task.WhenAll(loadSettingsTask, loadInventoryTask);
```

Change the last two lines to:

```csharp
                var loadOrdersTask = Orders.LoadAsync(safeUserId, JSRuntime);
                await Task.WhenAll(loadSettingsTask, loadInventoryTask, loadOrdersTask);
```

(No `IsLoaded`/`IsLoading` guard needed for `Orders` — `OrderService.LoadAsync` is cheap and idempotent, unlike `InventoryService`'s full product/ingredient/sale load.)

- [ ] **Step 4: Build**

Run: `dotnet build InventoryPlus.csproj -c Debug`
Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add Services/OrderService.cs Program.cs Layout/MainLayout.razor.cs
git commit -m "Add OrderService: pending orders, realtime, payment confirmation

Confirming payment loops the existing InventoryService.RecordSaleAsync
per item -- no new stock-deduction logic. Stops at the first failed
item and leaves the order pending rather than partially completing."
```

---

## Task 5: Settings — Online Menu slug field (Pro-gated)

**Files:**
- Modify: `Services/SettingsService.cs`
- Modify: `Pages/Settings.razor`
- Modify: `Pages/Settings.razor.cs` (if the save/load logic lives there — check first; some of this app's settings persistence lives directly in `SettingsService`)

**Interfaces:**
- Produces: `SettingsService.MenuSlug` (string) — consumed by `Pages/Settings.razor` and, indirectly, by shop owners copying their URL. Not consumed by `PublicMenuService` (that reads straight from Supabase, per Task 3's design note).

- [ ] **Step 1: Add the property to `SettingsService`**

Mirror the existing `_colorScheme` pattern (`Services/SettingsService.cs:210-222`):

```csharp
        // Online menu
        private string? _menuSlug;
        public string? MenuSlug
        {
            get => _menuSlug;
            set
            {
                if (_menuSlug != value)
                {
                    _menuSlug = value;
                    NotifyStateChanged();
                }
            }
        }
```

- [ ] **Step 2: Wire it into `LoadAsync`**

In `Services/SettingsService.cs`, inside `LoadAsync`, alongside `_colorScheme = result.ColorScheme ?? "lime";`:

```csharp
                    _menuSlug = result.MenuSlug;
```

- [ ] **Step 3: Wire it into `SaveAsync`**

Find `SaveAsync` (`Services/SettingsService.cs:388`) and its `new AccountSettings { ... }` construction (visible around line 400); add:

```csharp
                    MenuSlug = _menuSlug,
```

- [ ] **Step 4: Add the UI in Settings**

In `Pages/Settings.razor`, inside the "General" tab section (same section as `Company Name`/`Show Inventory Tab`), add a new block gated on Pro:

```razor
                <div style="border-top:1px solid var(--border);margin:14px 0;padding-top:14px">
                    <label class="form-label">Online Menu <span class="badge badge-amber" style="margin-left:4px">PRO</span></label>
                    <p style="color:var(--fg3);font-size:11px;margin-bottom:8px">Let customers scan a QR code to view your menu and order.</p>
                    @if (!AppSettings.IsPro)
                    {
                        <p style="color:var(--fg3);font-size:11px">Upgrade to Pro to enable QR ordering.</p>
                    }
                    else
                    {
                        <div class="form-row">
                            <div class="form-group">
                                <label class="form-label">Menu URL slug</label>
                                <input class="form-control" @bind="menuSlugInput" placeholder="e.g. sephys" />
                            </div>
                            <div class="form-group">
                                <label class="form-label">Your menu link</label>
                                <div style="display:flex;gap:5px">
                                    <input class="form-control" readonly value="@($"{Nav.BaseUri}menu/{(string.IsNullOrEmpty(AppSettings.MenuSlug) ? "…" : AppSettings.MenuSlug)}")" style="font-family:'Space Mono',monospace;font-size:10px" />
                                    <button class="btn btn-ghost btn-sm btn-icon" @onclick="CopyMenuLink" title="Copy link"><i class="fa-solid fa-copy"></i></button>
                                </div>
                            </div>
                        </div>
                    }
                </div>
```

- [ ] **Step 5: Wire up the code-behind**

`Pages/Settings.razor.cs` already injects `NavigationManager Nav`, `IJSRuntime JSRuntime`, and `ToastService Toast` (verified — no new injects needed). The General tab's save handler is `SaveSettings()` at line 183.

Add a field, initialize it in the existing `OnInitializedAsync` (line 86 — add this line inside that method's body, near where `newCompanyName` is set from `AppSettings.CompanyName`):

```csharp
        protected string menuSlugInput = "";
```

```csharp
            menuSlugInput = AppSettings.MenuSlug ?? "";
```

In `SaveSettings()` (line 183), add this line alongside the existing `AppSettings.CompanyName = newCompanyName;` etc.:

```csharp
            AppSettings.MenuSlug = string.IsNullOrWhiteSpace(menuSlugInput) ? null : menuSlugInput.Trim().ToLowerInvariant();
```

Add a copy-to-clipboard handler:

```csharp
        protected async Task CopyMenuLink()
        {
            var url = $"{Nav.BaseUri}menu/{AppSettings.MenuSlug}";
            await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", url);
            Toast.Show("Menu link copied!");
        }
```

Update the Step 4 markup above to reference `Nav.BaseUri` (not `NavManager.BaseUri`) and call `CopyMenuLink` — the button's `@onclick="CopyMenuLink"` already matches; just fix the input's `value="@($"{Nav.BaseUri}menu/{...}")"`.

- [ ] **Step 6: Build**

Run: `dotnet build InventoryPlus.csproj -c Debug`
Expected: 0 errors.

- [ ] **Step 7: Manual verify**

Run the app locally, log in as the test Pro account (or temporarily set a future `SubscriptionExpiry` on the test account via the Admin page, same as any other Pro-gated feature test in this app), open Settings → General, confirm the Online Menu block appears, type a slug, save, reload, confirm it persisted (the actual Supabase write will fail until Task 12's `menu_slug` column exists — expect a toast error at this point; that's correct and expected, not a bug to fix now. Confirm instead that the input reflects your typed value pre-save and the Copy Link button doesn't crash.)

- [ ] **Step 8: Commit**

```bash
git add Services/SettingsService.cs Pages/Settings.razor Pages/Settings.razor.cs
git commit -m "Add Pro-gated Online Menu slug field to Settings

Shows the /menu/{slug} URL with a copy-link button. No in-app QR
image generation -- owner runs the URL through any QR generator
once. DB column added in the final deferred migration task."
```

---

## Task 6: `PublicMenuLayout` + `/menu/{slug}` page

**Files:**
- Create: `Layout/PublicMenuLayout.razor`
- Create: `Pages/PublicMenu.razor`
- Create: `Pages/PublicMenu.razor.cs`

**Interfaces:**
- Consumes: `PublicMenuService` (Task 3), `Product.AvailableCount`/`TotalCost` (existing, `Models/ProductModel.cs`).
- Produces: navigates to `/order/{slug}/{orderNumber}` on submit (Task 7 reads that route).

- [ ] **Step 1: Minimal layout**

`Layout/PublicMenuLayout.razor` (no sidebar/topbar — this renders for anonymous phone visitors):

```razor
@inherits LayoutComponentBase

<div style="min-height:100vh;background:var(--bg)">
    @Body
</div>
```

- [ ] **Step 2: Page markup**

`Pages/PublicMenu.razor`:

```razor
@page "/menu/{Slug}"
@layout InventoryPlus.Layout.PublicMenuLayout
@attribute [AllowAnonymous]

<PageTitle>@(menu?.CompanyName ?? "Menu")</PageTitle>

@if (isLoading)
{
    <div style="padding:3rem;text-align:center;color:var(--fg3)"><i class="fa-solid fa-spinner fa-spin fa-2x"></i></div>
}
else if (menu == null)
{
    <div style="padding:3rem;text-align:center">
        <h2>Menu not available</h2>
        <p style="color:var(--fg3)">This menu link isn't active right now.</p>
    </div>
}
else
{
    <div style="max-width:520px;margin:0 auto;padding:16px;padding-bottom:120px">
        <div style="text-align:center;margin-bottom:16px">
            @if (!string.IsNullOrEmpty(menu.LogoUrl))
            {
                <img src="@menu.LogoUrl" style="width:56px;height:56px;border-radius:var(--radius-sm);object-fit:cover" />
            }
            <h1 style="margin-top:8px">@menu.CompanyName</h1>
        </div>

        @foreach (var group in menu.Products.GroupBy(p => string.IsNullOrEmpty(p.Category) ? "Other" : p.Category))
        {
            <div style="font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:.08em;color:var(--fg3);margin:16px 0 8px">@group.Key</div>
            @foreach (var product in group)
            {
                var available = product.AvailableCount > 0 || !product.HasIngredients;
                <div class="card" style="padding:10px 12px;margin-bottom:8px;display:flex;justify-content:space-between;align-items:center;opacity:@(available ? "1" : "0.5")">
                    <div>
                        <div style="font-weight:600">@product.Name</div>
                        <div class="mono text-lime" style="font-size:12px">₱@product.SellingPrice.ToString("0.00")</div>
                        @if (!available)
                        {
                            <div style="font-size:10px;color:var(--fg3)">Currently unavailable</div>
                        }
                    </div>
                    @if (available)
                    {
                        var qty = cart.TryGetValue(product.Guid, out var c) ? c : 0;
                        if (qty == 0)
                        {
                            <button class="btn btn-primary btn-sm" @onclick="() => AddToCart(product)">Add</button>
                        }
                        else
                        {
                            <div style="display:flex;align-items:center;gap:6px">
                                <button class="btn btn-ghost btn-sm btn-icon" @onclick="() => ChangeQty(product, -1)">-</button>
                                <span class="mono">@qty</span>
                                <button class="btn btn-ghost btn-sm btn-icon" @onclick="() => ChangeQty(product, 1)">+</button>
                            </div>
                        }
                    }
                </div>
            }
        }
    </div>

    @if (cart.Count > 0)
    {
        <div style="position:fixed;bottom:0;left:0;right:0;background:var(--bg2);border-top:1px solid var(--border);padding:12px 16px">
            <button class="btn btn-primary" style="width:100%;justify-content:center" @onclick="() => showCheckout = true">
                View Cart (@cart.Values.Sum()) — ₱@CartTotal.ToString("0.00")
            </button>
        </div>
    }

    <AppModal IsOpen="showCheckout" IsOpenChanged="@((v) => showCheckout = v)" Size="Large">
        <Header><h5>Your Order</h5></Header>
        <Content>
            @foreach (var (productId, qty) in cart)
            {
                var p = menu.Products.First(x => x.Guid == productId);
                <div style="display:flex;justify-content:space-between;padding:6px 0">
                    <span>@p.Name × @qty</span>
                    <span class="mono">₱@((p.SellingPrice * qty).ToString("0.00"))</span>
                </div>
            }
            <div style="display:flex;justify-content:space-between;font-weight:700;padding-top:8px;border-top:1px solid var(--border);margin-top:8px">
                <span>Total</span>
                <span class="mono text-lime">₱@CartTotal.ToString("0.00")</span>
            </div>
            <div class="form-group" style="margin-top:14px">
                <label class="form-label">Your Name <span class="req">*</span></label>
                <input class="form-control" @bind="customerName" placeholder="e.g. Juan" />
            </div>
            <div class="form-group">
                <label class="form-label">Table / Note (optional)</label>
                <input class="form-control" @bind="tableNote" placeholder="e.g. Table 4" />
            </div>
            @if (!string.IsNullOrEmpty(submitError))
            {
                <p class="text-red" style="font-size:12px">@submitError</p>
            }
        </Content>
        <Footer>
            <button class="btn btn-ghost" @onclick="() => showCheckout = false">Back</button>
            <button class="btn btn-primary" disabled="@(string.IsNullOrWhiteSpace(customerName) || isSubmitting)" @onclick="SubmitOrder">
                @if (isSubmitting) { <i class="fa-solid fa-spinner fa-spin"></i> }
                Place Order
            </button>
        </Footer>
    </AppModal>
}
```

- [ ] **Step 3: Code-behind**

`Pages/PublicMenu.razor.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using InventoryPlus.Services;

namespace InventoryPlus.Pages
{
    [AllowAnonymous]
    public partial class PublicMenu : ComponentBase
    {
        [Parameter] public string Slug { get; set; } = "";
        [Inject] public Supabase.Client SupabaseClient { get; set; } = default!;
        [Inject] public NavigationManager NavManager { get; set; } = default!;

        private PublicMenuService service = default!;
        protected PublicMenuInfo? menu;
        protected bool isLoading = true;
        protected bool showCheckout = false;
        protected bool isSubmitting = false;
        protected string customerName = "";
        protected string tableNote = "";
        protected string? submitError;
        protected Dictionary<Guid, int> cart = new();

        protected double CartTotal => cart.Sum(kv => menu!.Products.First(p => p.Guid == kv.Key).SellingPrice * kv.Value);

        protected override async System.Threading.Tasks.Task OnInitializedAsync()
        {
            service = new PublicMenuService(SupabaseClient);
            menu = await service.ResolveMenuAsync(Slug);
            isLoading = false;
        }

        protected void AddToCart(Models.Product product) => ChangeQty(product, 1);

        protected void ChangeQty(Models.Product product, int delta)
        {
            var qty = cart.TryGetValue(product.Guid, out var c) ? c : 0;
            qty += delta;
            if (qty <= 0) cart.Remove(product.Guid);
            else cart[product.Guid] = qty;
        }

        protected async System.Threading.Tasks.Task SubmitOrder()
        {
            submitError = null;
            if (string.IsNullOrWhiteSpace(customerName)) return;
            isSubmitting = true;
            try
            {
                var items = cart.Select(kv =>
                {
                    var p = menu!.Products.First(x => x.Guid == kv.Key);
                    return (p.Guid, p.Name, p.SellingPrice, kv.Value);
                }).ToList();

                var order = await service.SubmitOrderAsync(menu!.OwnerGuid, customerName.Trim(), tableNote.Trim(), items);
                NavManager.NavigateTo($"/order/{Slug}/{order.OrderNumber}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Order submit error: {ex.Message}");
                submitError = "Couldn't place your order — please try again.";
            }
            finally { isSubmitting = false; }
        }
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build InventoryPlus.csproj -c Debug`
Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add Layout/PublicMenuLayout.razor Pages/PublicMenu.razor Pages/PublicMenu.razor.cs
git commit -m "Add public /menu/{slug} page: browse, cart, checkout

Anonymous, uses PublicMenuService directly -- never touches the
logged-in session's InventoryService/SettingsService. Live testing
happens after Task 12's tables exist."
```

---

## Task 7: `/order/{slug}/{orderNumber}` confirmation page

**Files:**
- Create: `Pages/OrderConfirmation.razor`
- Create: `Pages/OrderConfirmation.razor.cs`

**Interfaces:**
- Consumes: `PublicMenuService.ResolveMenuAsync` (Task 3, for the owner lookup) plus a direct `Order`/`OrderItem` fetch by `(owner_guid, order_number)`.

- [ ] **Step 1: Page markup**

`Pages/OrderConfirmation.razor`:

```razor
@page "/order/{Slug}/{OrderNumber:int}"
@layout InventoryPlus.Layout.PublicMenuLayout
@attribute [AllowAnonymous]

<PageTitle>Order #@OrderNumber</PageTitle>

@if (isLoading)
{
    <div style="padding:3rem;text-align:center;color:var(--fg3)"><i class="fa-solid fa-spinner fa-spin fa-2x"></i></div>
}
else if (order == null)
{
    <div style="padding:3rem;text-align:center">
        <h2>Order not found</h2>
    </div>
}
else
{
    <div style="max-width:420px;margin:0 auto;padding:24px 16px;text-align:center">
        <div style="font-size:3rem;color:var(--lime)"><i class="fa-solid fa-circle-check"></i></div>
        <h1>Order #@order.OrderNumber</h1>
        <p style="color:var(--fg3)">Show this screen to staff to pay.</p>

        <div class="card" style="text-align:left;padding:14px;margin-top:16px">
            @foreach (var item in order.Items)
            {
                <div style="display:flex;justify-content:space-between;padding:5px 0">
                    <span>@item.ProductName × @item.Quantity</span>
                    <span class="mono">₱@((item.UnitPrice * item.Quantity).ToString("0.00"))</span>
                </div>
            }
            <div style="display:flex;justify-content:space-between;font-weight:700;padding-top:8px;border-top:1px solid var(--border);margin-top:8px">
                <span>Total</span>
                <span class="mono text-lime">₱@order.TotalAmount.ToString("0.00")</span>
            </div>
        </div>
    </div>
}
```

- [ ] **Step 2: Code-behind**

`Pages/OrderConfirmation.razor.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using InventoryPlus.Models;
using InventoryPlus.Services;

namespace InventoryPlus.Pages
{
    [AllowAnonymous]
    public partial class OrderConfirmation : ComponentBase
    {
        [Parameter] public string Slug { get; set; } = "";
        [Parameter] public int OrderNumber { get; set; }
        [Inject] public Supabase.Client SupabaseClient { get; set; } = default!;

        protected bool isLoading = true;
        protected Order? order;

        protected override async System.Threading.Tasks.Task OnInitializedAsync()
        {
            var service = new PublicMenuService(SupabaseClient);
            var menu = await service.ResolveMenuAsync(Slug);
            if (menu != null)
            {
                var resp = await SupabaseClient.From<Order>()
                    .Where(o => o.OwnerGuid == menu.OwnerGuid && o.OrderNumber == OrderNumber)
                    .Get();
                order = resp.Models.FirstOrDefault();
                if (order != null)
                {
                    var itemsResp = await SupabaseClient.From<OrderItem>()
                        .Where(i => i.OrderId == order.Guid)
                        .Get();
                    order.Items = itemsResp.Models;
                }
            }
            isLoading = false;
        }
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build InventoryPlus.csproj -c Debug`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add Pages/OrderConfirmation.razor Pages/OrderConfirmation.razor.cs
git commit -m "Add order confirmation page at /order/{slug}/{orderNumber}

Static confirmation, no live tracking, per spec section 3.2."
```

---

## Task 8: Staff Pending Orders view (Pro-gated, inside Sales page)

**Files:**
- Create: `Components/PendingOrdersView.razor`
- Modify: `Pages/Sales.razor`
- Modify: `Pages/Sales.razor.cs`
- Modify: `Services/SettingsService.cs` (one more small flag)

**Interfaces:**
- Consumes: `OrderService` (Task 4), `Order`/`OrderItem` (Task 2).

- [ ] **Step 1: Add a view-toggle flag**

In `Services/SettingsService.cs`, alongside `PosActiveView`, add a simple non-persisted UI flag (this is page-navigation state, not a saved preference — matches how `showMobileNav` etc. work elsewhere, i.e. plain component state would also work, but this app's convention keeps cross-cutting page-mode flags on `SettingsService` since `Sales.razor`'s header needs to react to it too):

```csharp
        // Pending Orders view toggle (Pro feature)
        private bool _showPendingOrders = false;
        public bool ShowPendingOrders
        {
            get => _showPendingOrders;
            set { if (_showPendingOrders != value) { _showPendingOrders = value; NotifyStateChanged(); } }
        }
```

- [ ] **Step 2: Add the nav button in Sales.razor**

In `Pages/Sales.razor`, in the non-POS-mode header (around line 26-34, next to the existing "POS Mode" button):

```razor
            <button class="btn btn-ghost btn-sm" disabled="@(!AppSettings.IsPro)" @onclick="() => AppSettings.ShowPendingOrders = !AppSettings.ShowPendingOrders">
                <i class="fa-solid fa-receipt"></i> Pending Orders
                @if (Orders.PendingOrders.Count > 0)
                {
                    <span class="badge badge-amber" style="margin-left:4px">@Orders.PendingOrders.Count</span>
                }
                @if (!AppSettings.IsPro)
                {
                    <span style="font-size:8px;font-weight:700;color:var(--amber);margin-left:4px;background:rgba(255,193,7,0.15);padding:1px 5px;border-radius:4px">PRO</span>
                }
            </button>
```

Then wrap the existing POS grid section (the `@if (!AppSettings.IsPosMode || AppSettings.PosActiveView == "pos")` block) so it's skipped while the pending view is open, and render the new component instead:

```razor
@if (AppSettings.ShowPendingOrders)
{
    <InventoryPlus.Components.PendingOrdersView />
}
else if (!AppSettings.IsPosMode || AppSettings.PosActiveView == "pos")
{
<!-- existing POS grid content unchanged -->
```

(Close the added `else if` at the same point the existing block already closes — this is a one-line condition change on the opening `@if`, not a restructure of the block's contents.)

- [ ] **Step 3: Inject `OrderService` into Sales**

In `Pages/Sales.razor.cs`, add:

```csharp
        [Inject] public OrderService Orders { get; set; } = default!;
```

Subscribe to realtime when the page initializes (find the existing `OnInitialized`/`OnInitializedAsync` and add):

```csharp
            Orders.OnStateChanged += StateHasChanged;
            Orders.SubscribeRealtime(() => InvokeAsync(StateHasChanged));
```

And unsubscribe in `Dispose` (check whether `Sales` already implements `IDisposable` — if not, add `IDisposable` to the class declaration and a `Dispose()` method):

```csharp
        public void Dispose()
        {
            Orders.OnStateChanged -= StateHasChanged;
        }
```

(Deliberately not calling `Orders.UnsubscribeRealtime()` here — the realtime channel should stay live across page navigation within the app since a new order can arrive while staff are on a different page. It's `OrderService`-lifetime, not page-lifetime; Task 4 already registers it as a singleton.)

- [ ] **Step 4: The Pending Orders component**

`Components/PendingOrdersView.razor`:

```razor
@inject InventoryPlus.Services.OrderService Orders
@inject InventoryPlus.Services.InventoryService Inventory
@inject InventoryPlus.Services.ToastService Toast
@inject Microsoft.JSInterop.IJSRuntime JS

<div class="section-gap fade-in">
    @if (!Orders.PendingOrders.Any())
    {
        <InventoryPlus.Components.EmptyState Icon="fa-solid fa-receipt" Title="No pending orders" Message="Orders placed through your QR menu will show up here." />
    }
    else
    {
        @foreach (var order in Orders.PendingOrders)
        {
            <div class="card card-p" style="margin-bottom:10px">
                <div style="display:flex;justify-content:space-between;align-items:flex-start">
                    <div>
                        <div style="font-weight:700">Order #@order.OrderNumber — @order.CustomerName</div>
                        @if (!string.IsNullOrEmpty(order.TableNote))
                        {
                            <div style="font-size:11px;color:var(--fg3)">@order.TableNote</div>
                        }
                    </div>
                    <span class="mono text-lime fw-700">₱@order.TotalAmount.ToString("0.00")</span>
                </div>

                <div style="margin:10px 0">
                    @foreach (var item in order.Items.ToList())
                    {
                        <div style="display:flex;justify-content:space-between;align-items:center;padding:4px 0">
                            <span>@item.ProductName</span>
                            <div style="display:flex;align-items:center;gap:6px">
                                <button class="btn btn-ghost btn-sm btn-icon" @onclick="() => Orders.UpdateItemQuantityAsync(order, item, item.Quantity - 1)">-</button>
                                <span class="mono">@item.Quantity</span>
                                <button class="btn btn-ghost btn-sm btn-icon" @onclick="() => Orders.UpdateItemQuantityAsync(order, item, item.Quantity + 1)">+</button>
                                <button class="btn btn-danger btn-sm btn-icon" @onclick="() => Orders.RemoveItemAsync(order, item)"><i class="fa-solid fa-trash"></i></button>
                            </div>
                        </div>
                    }
                </div>

                <div style="display:flex;gap:6px">
                    <button class="btn btn-ghost btn-sm" @onclick="() => Orders.CancelOrderAsync(order)">Cancel</button>
                    <button class="btn btn-primary btn-sm" style="flex:1;justify-content:center" @onclick="() => ConfirmPayment(order, "Cash")">Cash</button>
                    <button class="btn btn-primary btn-sm" style="flex:1;justify-content:center" @onclick="() => ConfirmPayment(order, "GCash")">GCash</button>
                </div>
            </div>
        }
    }
</div>

@code {
    private async Task ConfirmPayment(InventoryPlus.Models.Order order, string method)
    {
        var (success, failedItem) = await Orders.ConfirmPaymentAsync(order, method, Inventory, JS);
        if (success)
            Toast.Show($"Order #{order.OrderNumber} completed!");
        else
            Toast.Show($"Couldn't complete \"{failedItem}\" — check stock and try again.", "error");
    }
}
```

- [ ] **Step 5: Build**

Run: `dotnet build InventoryPlus.csproj -c Debug`
Expected: 0 errors.

- [ ] **Step 6: Manual verify (partial — DB tables don't exist yet)**

Run the app, log into a Pro test account, open Sales, confirm the "Pending Orders" button renders and toggles the (empty-state) view without errors. Full end-to-end order flow can only be verified after Task 12.

- [ ] **Step 7: Commit**

```bash
git add Components/PendingOrdersView.razor Pages/Sales.razor Pages/Sales.razor.cs Services/SettingsService.cs
git commit -m "Add staff Pending Orders view with realtime updates

Pro-gated third view on the Sales page. Confirm Payment reuses
OrderService.ConfirmPaymentAsync (Task 4), which itself reuses the
existing RecordSaleAsync stock-deduction path."
```

---

## Task 9: Verify build end-to-end and fix any cross-task drift

Given Tasks 1-8 span many files with hand-written cross-references (property names, method signatures), do one full clean build before moving to live DB testing.

**Files:** none new — verification only.

- [ ] **Step 1: Clean build**

Run: `dotnet clean InventoryPlus.csproj && dotnet build InventoryPlus.csproj -c Debug`
Expected: 0 errors, 0 new warnings beyond the pre-existing ones already in this codebase (nullable-reference warnings on `Login.razor.cs`, `Settings.razor.cs`, etc. — those predate this feature).

- [ ] **Step 2: Fix any signature mismatches**

If the build fails, the most likely causes given this plan's design: (a) the exact Supabase Realtime API names in `OrderService.SubscribeRealtime` not matching the installed 1.5.0 client — check `obj/Debug/net10.0/*.AssemblyInfo.cs` isn't helpful here; instead inspect the actual installed package via `dotnet build` error output, which will name the correct type/method; (b) `.Order(...)`/`.Limit(...)` on Postgrest queries needing to match `Services/InventoryService.cs`'s existing usage exactly — grep that file for the precise calls and copy the pattern.

- [ ] **Step 3: Commit any fixes**

```bash
git add -A
git commit -m "Fix build errors from QR ordering cross-task API mismatches"
```

(Skip this commit if Step 1 already passed clean.)

---

## Task 10: Local dev config for testing (no Supabase changes yet)

Nothing to change here that touches Supabase — this task exists to make sure the new anonymous routes are reachable locally before Task 12's SQL lands, so once it does, verification is fast.

**Files:** none — verification only.

- [ ] **Step 1: Run locally and hit the new routes**

Start the app (`dotnet run` or the existing `.claude/launch.json` dev-server config), then in a browser:
- `/menu/nonexistent-slug` → should show the "Menu not available" state (works even without the new tables, since `ResolveMenuAsync` just gets an empty result from `account_settings` — `MenuSlug` column doesn't exist yet though, so expect a Postgrest error in the console at this point; that's expected and resolves once Task 12 runs).
- `/order/x/1` → should show "Order not found" once the query itself doesn't error (same caveat).

- [ ] **Step 2: No commit** — this task is a checkpoint, not a code change.

---

## Task 11: Supabase migration SQL — write it now, apply it in Task 12

Per your instruction, all Supabase changes happen after every code task is done. This task writes the SQL as a tracked file; Task 12 is the actual "go run this" step.

**Files:**
- Create: `docs/superpowers/specs/2026-08-22-qr-ordering-migration.sql`

- [ ] **Step 1: Write the migration file**

```sql
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
```

- [ ] **Step 2: Commit the migration file (not yet applied)**

```bash
git add docs/superpowers/specs/2026-08-22-qr-ordering-migration.sql
git commit -m "Add QR ordering Supabase migration SQL (not yet applied)

Run this in the Supabase SQL Editor once all code tasks (1-9) are
verified. See Task 12."
```

---

## Task 12: Apply the Supabase migration and do full live verification

**Only start this task once Tasks 1-11 are committed and the app builds clean.**

- [ ] **Step 1: Apply the migration**

In the Supabase Dashboard → SQL Editor, run the full contents of `docs/superpowers/specs/2026-08-22-qr-ordering-migration.sql`.

- [ ] **Step 2: Verify RLS with two accounts**

Using the app's existing test accounts (or create a second test account via `/register`), set both to Pro (via Admin), give both different `menu_slug` values in Settings. Confirm:
- Account A's `/menu/{slugA}` never shows Account B's products.
- An order placed on `/menu/{slugA}` never appears in Account B's Pending Orders.

- [ ] **Step 3: Full order flow, live**

- Open `/menu/{slugA}` in one browser tab (simulating the customer), staff Pending Orders view in another (simulating the shop, logged in as Account A).
- Add items to cart, submit with a name.
- Confirm the pending order appears in the staff view **without a manual refresh** (this is the realtime check — if it doesn't appear live, check the browser console for the realtime subscription error and confirm `alter publication supabase_realtime add table orders;` actually ran).
- Edit a quantity in the staff view, confirm the total recalculates.
- Confirm Payment (Cash), confirm: the order disappears from Pending Orders, a matching Sale appears in the normal Sales list, and the relevant product's stock/AvailableCount decreased correctly (same verification method used throughout this app's history — check via the UI, then confirm against Supabase directly with a scoped `curl` using the account's access token, matching the pattern already used to verify the ingredient-linking and recipe fixes earlier in this project).

- [ ] **Step 4: Stock race check**

With two browser tabs on the same `/menu/{slugA}`, order the last unit of a low-stock tracked product from both, then Confirm Payment on both pending orders in staff view. Confirm the second Confirm Payment fails gracefully (toast naming the item) rather than overselling.

- [ ] **Step 5: Clean up test data**

Delete/archive any test orders, sales, and products created during verification, same cleanup discipline used for every other feature tested live in this project this session.

- [ ] **Step 6: Final commit**

```bash
git add -A
git commit -m "Verify QR ordering end-to-end against live Supabase

RLS cross-tenant isolation, realtime staff alerts, edit, payment
confirmation with stock deduction, and concurrent-order stock race
all confirmed live. Feature complete."
```
