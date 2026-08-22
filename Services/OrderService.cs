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
        private Guid _ownerGuid;
        private RealtimeChannel? _channel;

        public List<Order> PendingOrders { get; private set; } = new();
        public event Action? OnStateChanged;

        public OrderService(Supabase.Client supabase)
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
                _channel = _supabase.Realtime.Channel("realtime", "public", "orders", "owner_guid", $"owner_guid=eq.{_ownerGuid}");
                _channel.AddPostgresChangeHandler(Supabase.Realtime.PostgresChanges.PostgresChangesOptions.ListenType.All, async (_, __) =>
                {
                    await LoadAsync(_ownerGuid.ToString());
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
