using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Authorization;
using InventoryPlus.Services;
using InventoryPlus.Models;

namespace InventoryPlus.Pages
{
    [Authorize]
    public partial class Sales : ComponentBase, IDisposable
    {
        [Inject] public InventoryService Inventory { get; set; } = default!;
        [Inject] public SettingsService AppSettings { get; set; } = default!;
        protected bool showSuccessModal;
        protected string saleNote = "";
        protected string paymentMethod = "Cash";

        protected class CartItem
        {
            public Product Product { get; set; } = new();
            public int Quantity { get; set; }
        }

        protected List<CartItem> Cart = new();

        protected override void OnInitialized()
        {
            Inventory.OnStateChanged += HandleStateChanged;
        }

        private void HandleStateChanged() => StateHasChanged();

        public void Dispose()
        {
            Inventory.OnStateChanged -= HandleStateChanged;
        }

        protected double Subtotal => Cart.Sum(c => c.Product.SellingPrice * c.Quantity);
        protected double TaxTotal => Cart.Sum(c => c.Product.SellingPrice * c.Product.TaxRate * c.Quantity);
        protected double Total => Subtotal + TaxTotal;

        protected void AddToCart(Product p)
        {
            var existing = Cart.FirstOrDefault(c => c.Product.Id == p.Id);
            if (existing != null)
            {
                if (existing.Quantity < p.AvailableCount)
                {
                    existing.Quantity++;
                }
            }
            else
            {
                Cart.Add(new CartItem { Product = p, Quantity = 1 });
            }
        }

        protected void RemoveFromCart(CartItem item)
        {
            if (item.Quantity > 1) item.Quantity--;
            else Cart.Remove(item);
        }

        protected void CompleteSale()
        {
            if (!Cart.Any()) return;

            foreach (var item in Cart)
            {
                Inventory.RecordSale(item.Product, item.Quantity, saleNote, paymentMethod);
            }

            Cart.Clear();
            saleNote = "";
            paymentMethod = "Cash";
            showSuccessModal = true;
        }
    }
}
