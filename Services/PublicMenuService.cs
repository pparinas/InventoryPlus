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
    /// session's own data, not the shop being viewed.
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
            // order_number) constraint plus a single retry.
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
            catch (Supabase.Postgrest.Exceptions.PostgrestException)
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
