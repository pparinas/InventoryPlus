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
        private readonly Supabase.Client _supabase;
        private readonly ToastService _toast;
        private Guid _ownerGuid;
        private RealtimeChannel? _channel;

        public List<Order> PendingOrders { get; private set; } = new();
        public event Action? OnStateChanged;

        public OrderService(Supabase.Client supabase, ToastService toast)
        {
            _supabase = supabase;
            _toast = toast;
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
            // Fire-and-forget: realtime is a nice-to-have live update, not a page
            // dependency. A connection failure (or the "orders" table/realtime
            // publication not existing yet) must never crash the page it's called
            // from -- Pending Orders still works via the normal load-on-open path.
            _ = SubscribeRealtimeAsync(onChange);
        }

        private async Task SubscribeRealtimeAsync(Action onChange)
        {
            try
            {
                // Sales.OnInitialized() can run before MainLayout's LoadDataAsync
                // finishes awaiting LoadAsync (child OnInitialized fires before the
                // parent layout's OnAfterRenderAsync completes), so _ownerGuid may
                // still be default here. Wait briefly for it to be set rather than
                // subscribing to a filter on an empty guid.
                var attempts = 0;
                while (_ownerGuid == Guid.Empty && attempts < 25)
                {
                    await Task.Delay(200);
                    attempts++;
                }
                if (_ownerGuid == Guid.Empty)
                {
                    Console.WriteLine("OrderService realtime subscribe skipped: owner not resolved yet.");
                    return;
                }

                _channel = _supabase.Realtime.Channel("realtime", "public", "orders", "owner_guid", _ownerGuid.ToString());
                _channel.AddPostgresChangeHandler(Supabase.Realtime.PostgresChanges.PostgresChangesOptions.ListenType.All, async (_, __) =>
                {
                    var previousIds = PendingOrders.Select(o => o.Guid).ToHashSet();
                    await LoadAsync(_ownerGuid.ToString());
                    foreach (var order in PendingOrders.Where(o => !previousIds.Contains(o.Guid)))
                        _toast.Show($"New order #{order.OrderNumber} from {order.CustomerName} — ₱{order.TotalAmount:0.00}", "info");
                    onChange();
                });
                await _channel.Subscribe();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OrderService realtime subscribe failed (falling back to load-on-open): {ex.Message}");
                _channel = null;
            }
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

        /// <summary>
        /// Adds a product the customer forgot (or now wants) to a pending order.
        /// Bumps quantity instead of duplicating the line if it's already there.
        /// </summary>
        public async Task AddItemAsync(Order order, Product product)
        {
            var existing = order.Items.FirstOrDefault(i => i.ProductId == product.Guid);
            if (existing != null)
            {
                await UpdateItemQuantityAsync(order, existing, existing.Quantity + 1);
                return;
            }

            var item = new OrderItem
            {
                Guid = Guid.NewGuid(),
                OrderId = order.Guid,
                ProductId = product.Guid,
                ProductName = product.Name,
                UnitPrice = product.SellingPrice,
                Quantity = 1
            };
            await _supabase.From<OrderItem>().Insert(item);
            order.Items.Add(item);
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
