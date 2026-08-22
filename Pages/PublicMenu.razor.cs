using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using InventoryPlus.Services;

namespace InventoryPlus.Pages
{
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

        protected override async Task OnInitializedAsync()
        {
            service = new PublicMenuService(SupabaseClient);
            try
            {
                menu = await service.ResolveMenuAsync(Slug);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ResolveMenuAsync error: {ex.Message}");
                menu = null;
            }
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

        protected async Task SubmitOrder()
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
