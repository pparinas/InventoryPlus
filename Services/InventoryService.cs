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

        public event Action? OnStateChanged;

        public InventoryService()
        {
            // Seed Data
            var tomato = new Ingredient { Id = Guid.NewGuid(), Name = "Tomato", Unit = "pcs", Stock = 100, CostPerUnit = 0.5 };
            var pasta = new Ingredient { Id = Guid.NewGuid(), Name = "Pasta", Unit = "kg", Stock = 10.5, CostPerUnit = 2.0 };
            var cheese = new Ingredient { Id = Guid.NewGuid(), Name = "Cheese", Unit = "kg", Stock = 5, CostPerUnit = 10.0 };
            
            Ingredients.AddRange(new[] { tomato, pasta, cheese });

            var product1 = new Product
            {
                Id = Guid.NewGuid(),
                Name = "Tomato Pasta",
                SellingPrice = 15.0,
                TaxRate = 0.05,
                RequiredIngredients = new List<ProductIngredient>
                {
                    new ProductIngredient { IngredientId = pasta.Id, Ingredient = pasta, QuantityRequired = 0.2 },
                    new ProductIngredient { IngredientId = tomato.Id, Ingredient = tomato, QuantityRequired = 4 },
                    new ProductIngredient { IngredientId = cheese.Id, Ingredient = cheese, QuantityRequired = 0.1 }
                }
            };

            Products.Add(product1);
        }

        public void AddIngredient(Ingredient ingredient) { Ingredients.Add(ingredient); NotifyStateChanged(); }
        public void UpdateIngredient(Ingredient ingredient) { NotifyStateChanged(); }
        public void DeleteIngredient(Ingredient ingredient) { Ingredients.Remove(ingredient); NotifyStateChanged(); }

        public void AddProduct(Product product) { Products.Add(product); NotifyStateChanged(); }
        public void UpdateProduct(Product product) { NotifyStateChanged(); }
        public void DeleteProduct(Product product) { Products.Remove(product); NotifyStateChanged(); }

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
                ProductId = product.Id,
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
