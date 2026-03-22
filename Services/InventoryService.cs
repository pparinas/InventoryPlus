using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using InventoryPlus.Models;
using Supabase;

namespace InventoryPlus.Services
{
    public class InventoryService
    {
        private readonly Client _supabase;
        private Guid _ownerGuid;

        public List<Ingredient> Ingredients { get; set; } = new();
        public List<Product> Products { get; set; } = new();
        public List<Sale> Sales { get; set; } = new();

        public bool IsLoaded { get; set; }
        public bool IsLoading { get; private set; }

        public IEnumerable<Ingredient> ActiveIngredients => Ingredients.Where(i => !i.IsArchived);
        public IEnumerable<Product> ActiveProducts => Products.Where(p => !p.IsArchived);

        public event Action? OnStateChanged;

        public InventoryService(Client supabase)
        {
            _supabase = supabase;
        }

        public async Task LoadAsync(string userId)
        {
            if (!Guid.TryParse(userId, out _ownerGuid)) return;
            if (IsLoading) return;

            IsLoading = true;
            try
            {
                // Load ingredients
                var ingredientsResp = await _supabase.From<Ingredient>()
                    .Where(i => i.OwnerGuid == _ownerGuid)
                    .Get();
                Ingredients = ingredientsResp.Models;

                // Load products
                var productsResp = await _supabase.From<Product>()
                    .Where(p => p.OwnerGuid == _ownerGuid)
                    .Get();
                Products = productsResp.Models;

                // Load product ingredients for all products and wire them up
                var piResp = await _supabase.From<ProductIngredient>()
                    .Where(pi => pi.OwnerId == _ownerGuid)
                    .Get();

                foreach (var product in Products)
                {
                    product.RequiredIngredients = piResp.Models
                        .Where(pi => pi.ProductId == product.Guid)
                        .ToList();

                    foreach (var pi in product.RequiredIngredients)
                        pi.Ingredient = Ingredients.FirstOrDefault(i => i.Guid == pi.IngredientId);
                }

                // Load sales
                var salesResp = await _supabase.From<Sale>()
                    .Where(s => s.OwnerId == _ownerGuid)
                    .Get();
                Sales = salesResp.Models.OrderByDescending(s => s.Date).ToList();

                IsLoaded = true;
                NotifyStateChanged();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"InventoryService.LoadAsync error: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        // ── Ingredients ────────────────────────────────────────────────────────

        public async Task AddIngredientAsync(Ingredient ingredient)
        {
            ingredient.OwnerGuid = _ownerGuid;
            var resp = await _supabase.From<Ingredient>().Insert(ingredient);
            var saved = resp.Models.FirstOrDefault();
            if (saved != null)
            {
                ingredient.Guid = saved.Guid;
                Ingredients.Add(ingredient);
                NotifyStateChanged();
            }
        }

        public async Task UpdateIngredientAsync(Ingredient ingredient)
        {
            ingredient.OwnerGuid = _ownerGuid;
            await _supabase.From<Ingredient>().Upsert(ingredient);
            NotifyStateChanged();
        }

        public async Task DeleteIngredientAsync(Ingredient ingredient)
        {
            ingredient.IsArchived = true;
            await _supabase.From<Ingredient>().Upsert(ingredient);
            NotifyStateChanged();
        }

        // ── Products ───────────────────────────────────────────────────────────

        public async Task AddProductAsync(Product product)
        {
            product.OwnerGuid = _ownerGuid;
            var requirements = product.RequiredIngredients.ToList();

            // Clear reference list before insert to avoid serialisation issues
            product.RequiredIngredients = new();
            await _supabase.From<Product>().Upsert(product);
            product.RequiredIngredients = requirements;

            // Save each ProductIngredient row
            foreach (var pi in requirements)
            {
                pi.ProductId = product.Guid;
                pi.OwnerId = _ownerGuid;
                var savedIngredient = pi.Ingredient;
                pi.Ingredient = null;
                await _supabase.From<ProductIngredient>().Upsert(pi);
                pi.Ingredient = savedIngredient;
            }

            Products.Add(product);
            NotifyStateChanged();
        }

        public async Task UpdateProductAsync(Product product)
        {
            product.OwnerGuid = _ownerGuid;
            var requirements = product.RequiredIngredients.ToList();

            product.RequiredIngredients = new();
            await _supabase.From<Product>().Upsert(product);
            product.RequiredIngredients = requirements;

            // Delete removed product ingredients from DB
            var existingPIs = await _supabase.From<ProductIngredient>()
                .Where(pi => pi.ProductId == product.Guid)
                .Get();
            foreach (var old in existingPIs.Models)
            {
                if (!requirements.Any(r => r.Guid == old.Guid))
                    await _supabase.From<ProductIngredient>().Delete(old);
            }

            // Upsert current requirements
            foreach (var pi in requirements)
            {
                pi.ProductId = product.Guid;
                pi.OwnerId = _ownerGuid;
                var savedIngredient = pi.Ingredient;
                pi.Ingredient = null;
                await _supabase.From<ProductIngredient>().Upsert(pi);
                pi.Ingredient = savedIngredient ?? Ingredients.FirstOrDefault(i => i.Guid == pi.IngredientId);
            }

            NotifyStateChanged();
        }

        public async Task DeleteProductAsync(Product product)
        {
            product.IsArchived = true;
            var requirements = product.RequiredIngredients.ToList();
            product.RequiredIngredients = new();
            await _supabase.From<Product>().Upsert(product);
            product.RequiredIngredients = requirements;
            NotifyStateChanged();
        }

        // ── Sales ──────────────────────────────────────────────────────────────

        public async Task<bool> RecordSaleAsync(Product product, int quantity, string note = "", string paymentMethod = "Cash")
        {
            if (product.AvailableCount < quantity) return false;

            // Deduct ingredient stock and persist
            foreach (var req in product.RequiredIngredients)
            {
                if (req.Ingredient != null)
                {
                    req.Ingredient.Stock -= req.QuantityRequired * quantity;
                    await _supabase.From<Ingredient>().Upsert(req.Ingredient);
                }
            }

            var subtotal = product.SellingPrice * quantity;
            var tax = product.SellingPrice * product.TaxRate * quantity;
            var total = subtotal + tax;
            var profit = subtotal - (product.TotalCost * quantity);

            var sale = new Sale
            {
                OwnerId = _ownerGuid,
                ProductId = product.Guid,
                ProductName = product.Name,
                QuantitySold = quantity,
                TotalAmount = total,
                TaxAmount = tax,
                ProfitAmount = profit,
                Date = DateTime.UtcNow,
                Note = note,
                PaymentMethod = paymentMethod
            };

            var resp = await _supabase.From<Sale>().Insert(sale);
            var saved = resp.Models.FirstOrDefault() ?? sale;
            Sales.Insert(0, saved);

            NotifyStateChanged();
            return true;
        }

        public void NotifyStateChanged() => OnStateChanged?.Invoke();
    }
}

