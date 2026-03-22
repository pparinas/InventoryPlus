using System;
using System.Collections.Generic;
using System.Linq;
using InventoryPlus.Models;

namespace InventoryPlus.Services
{
    public class InventoryService
    {
        public List<Ingredient> Ingredients { get; set; } = new();
        public List<Product> Products { get; set; } = new();
        public List<Sale> Sales { get; set; } = new();

        public IEnumerable<Ingredient> ActiveIngredients => Ingredients.Where(i => !i.IsArchived);
        public IEnumerable<Product> ActiveProducts => Products.Where(p => !p.IsArchived);

        public event Action? OnStateChanged;

        public InventoryService()
        {
        }

        public void AddIngredient(Ingredient ingredient) { Ingredients.Add(ingredient); NotifyStateChanged(); }
        public void UpdateIngredient(Ingredient ingredient) { NotifyStateChanged(); }
        public void DeleteIngredient(Ingredient ingredient) { ingredient.IsArchived = true; NotifyStateChanged(); }

        public void AddProduct(Product product) { Products.Add(product); NotifyStateChanged(); }
        public void UpdateProduct(Product product) { NotifyStateChanged(); }
        public void DeleteProduct(Product product) { product.IsArchived = true; NotifyStateChanged(); }

        public bool RecordSale(Product product, int quantity, string note = "", string paymentMethod = "Cash")
        {
            if (product.AvailableCount < quantity) return false;

            // Deduct ingredients stock
            foreach (var req in product.RequiredIngredients)
            {
                if (req.Ingredient != null)
                {
                    req.Ingredient.Stock -= req.QuantityRequired * quantity;
                }
            }

            var tax = product.SellingPrice * product.TaxRate * quantity;
            var subtotal = product.SellingPrice * quantity;
            var total = subtotal + tax;
            var cost = product.TotalCost * quantity;
            var profit = subtotal - cost;

            var sale = new Sale
            {
                ProductId = product.Guid,
                ProductName = product.Name,
                QuantitySold = quantity,
                TotalAmount = total,
                TaxAmount = tax,
                ProfitAmount = profit,
                Date = DateTime.Now,
                Note = note,
                PaymentMethod = paymentMethod
            };

            Sales.Add(sale);
            NotifyStateChanged();
            return true;
        }

        public void NotifyStateChanged() => OnStateChanged?.Invoke();
    }
}
