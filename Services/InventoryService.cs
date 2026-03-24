using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using InventoryPlus.Models;
using Microsoft.JSInterop;
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
        public bool IsOffline { get; private set; }

        private const string CacheKeyPrefix = "inv_cache_";

        public IEnumerable<Ingredient> ActiveIngredients => Ingredients.Where(i => !i.IsArchived);
        public IEnumerable<Product> ActiveProducts => Products.Where(p => !p.IsArchived);

        public event Action? OnStateChanged;

        public InventoryService(Client supabase)
        {
            _supabase = supabase;
        }

        public async Task LoadAsync(string userId, IJSRuntime? js = null)
        {
            if (!Guid.TryParse(userId, out _ownerGuid)) return;
            if (IsLoading) return;

            IsLoading = true;
            bool hasCache = false;
            try
            {
                // 1. Load from cache immediately so the UI can render with last-known data
                if (js != null)
                {
                    hasCache = await LoadFromCacheAsync(js, userId);
                    if (hasCache)
                    {
                        IsLoaded = true;
                        NotifyStateChanged();
                    }
                }

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

                IsOffline = false;
                IsLoaded = true;

                // 3. Persist fresh data for next offline/fast load
                if (js != null) await SaveCacheAsync(js);

                NotifyStateChanged();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"InventoryService.LoadAsync error: {ex.Message}");
                if (hasCache)
                {
                    // Show cached data with offline indicator
                    IsOffline = true;
                    IsLoaded = true;
                    NotifyStateChanged();
                }
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

        public async Task<Sale?> RecordSaleAsync(Product product, int quantity, string note = "", string paymentMethod = "Cash", string customerName = "", double discountAmount = 0, string discountType = "None")
        {
            if (product.AvailableCount < quantity) return null;

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

            // Apply discount
            if (discountType == "Percentage" && discountAmount > 0)
                total -= total * (discountAmount / 100.0);
            else if (discountType == "Fixed" && discountAmount > 0)
                total -= discountAmount;
            if (total < 0) total = 0;

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
                PaymentMethod = paymentMethod,
                CustomerName = customerName,
                DiscountAmount = discountAmount,
                DiscountType = discountType
            };

            var resp = await _supabase.From<Sale>().Insert(sale);
            var saved = resp.Models.FirstOrDefault() ?? sale;

            // Re-apply in-memory-only fields (marked [JsonIgnore], not persisted to DB)
            saved.CustomerName = customerName;
            saved.DiscountAmount = discountAmount;
            saved.DiscountType = discountType;

            Sales.Insert(0, saved);

            NotifyStateChanged();
            return saved;
        }

        public void NotifyStateChanged() => OnStateChanged?.Invoke();

        // ── Cache ──────────────────────────────────────────────────────────────

        public async Task ClearCacheAsync(IJSRuntime js, string userId)
        {
            try { await js.InvokeVoidAsync("localStorage.removeItem", $"{CacheKeyPrefix}{userId}"); }
            catch { }
        }

        private async Task SaveCacheAsync(IJSRuntime js)
        {
            try
            {
                var snapshot = new
                {
                    ingredients = Ingredients.Select(i => new
                    {
                        guid = i.Guid, ownerGuid = i.OwnerGuid, name = i.Name,
                        unit = i.Unit, stock = i.Stock, costPerUnit = i.CostPerUnit,
                        type = i.Type, isArchived = i.IsArchived, createdAt = i.CreatedAt
                    }),
                    products = Products.Select(p => new
                    {
                        guid = p.Guid, ownerGuid = p.OwnerGuid, name = p.Name,
                        sellingPrice = p.SellingPrice, taxRate = p.TaxRate,
                        imageUrl = p.ImageUrl, isArchived = p.IsArchived
                    }),
                    productIngredients = Products.SelectMany(p => p.RequiredIngredients).Select(pi => new
                    {
                        guid = pi.Guid, ownerId = pi.OwnerId, productId = pi.ProductId,
                        ingredientId = pi.IngredientId, quantityRequired = pi.QuantityRequired
                    }),
                    sales = Sales.Select(s => new
                    {
                        guid = s.Guid, ownerId = s.OwnerId, productId = s.ProductId,
                        productName = s.ProductName, quantitySold = s.QuantitySold,
                        totalAmount = s.TotalAmount, taxAmount = s.TaxAmount,
                        profitAmount = s.ProfitAmount, date = s.Date,
                        note = s.Note, paymentMethod = s.PaymentMethod
                    })
                };
                var json = JsonSerializer.Serialize(snapshot);
                await js.InvokeVoidAsync("localStorage.setItem", $"{CacheKeyPrefix}{_ownerGuid}", json);
            }
            catch (Exception ex) { Console.WriteLine($"SaveCacheAsync error: {ex.Message}"); }
        }

        private async Task<bool> LoadFromCacheAsync(IJSRuntime js, string userId)
        {
            try
            {
                var json = await js.InvokeAsync<string?>("localStorage.getItem", $"{CacheKeyPrefix}{userId}");
                if (string.IsNullOrEmpty(json)) return false;

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                Ingredients = root.GetProperty("ingredients").EnumerateArray().Select(e => new Ingredient
                {
                    Guid = e.GetProperty("guid").GetGuid(),
                    OwnerGuid = e.GetProperty("ownerGuid").GetGuid(),
                    Name = e.GetProperty("name").GetString() ?? "",
                    Unit = e.GetProperty("unit").GetString() ?? "",
                    Stock = e.GetProperty("stock").GetDouble(),
                    CostPerUnit = e.GetProperty("costPerUnit").GetDouble(),
                    Type = e.GetProperty("type").GetString() ?? "Ingredient",
                    IsArchived = e.GetProperty("isArchived").GetBoolean(),
                    CreatedAt = e.TryGetProperty("createdAt", out var ca) ? ca.GetDateTime() : DateTime.MinValue
                }).ToList();

                var piList = root.GetProperty("productIngredients").EnumerateArray().Select(e => new ProductIngredient
                {
                    Guid = e.GetProperty("guid").GetGuid(),
                    OwnerId = e.GetProperty("ownerId").GetGuid(),
                    ProductId = e.GetProperty("productId").GetGuid(),
                    IngredientId = e.GetProperty("ingredientId").GetGuid(),
                    QuantityRequired = e.GetProperty("quantityRequired").GetDouble()
                }).ToList();

                Products = root.GetProperty("products").EnumerateArray().Select(e =>
                {
                    var g = e.GetProperty("guid").GetGuid();
                    var product = new Product
                    {
                        Guid = g,
                        OwnerGuid = e.GetProperty("ownerGuid").GetGuid(),
                        Name = e.GetProperty("name").GetString() ?? "",
                        SellingPrice = e.GetProperty("sellingPrice").GetDouble(),
                        TaxRate = e.GetProperty("taxRate").GetDouble(),
                        ImageUrl = e.GetProperty("imageUrl").GetString() ?? "",
                        IsArchived = e.GetProperty("isArchived").GetBoolean(),
                        RequiredIngredients = piList.Where(pi => pi.ProductId == g).ToList()
                    };
                    foreach (var pi in product.RequiredIngredients)
                        pi.Ingredient = Ingredients.FirstOrDefault(i => i.Guid == pi.IngredientId);
                    return product;
                }).ToList();

                Sales = root.GetProperty("sales").EnumerateArray().Select(e => new Sale
                {
                    Guid = e.GetProperty("guid").GetGuid(),
                    OwnerId = e.GetProperty("ownerId").GetGuid(),
                    ProductId = e.GetProperty("productId").GetGuid(),
                    ProductName = e.GetProperty("productName").GetString() ?? "",
                    QuantitySold = e.GetProperty("quantitySold").GetInt32(),
                    TotalAmount = e.GetProperty("totalAmount").GetDouble(),
                    TaxAmount = e.GetProperty("taxAmount").GetDouble(),
                    ProfitAmount = e.GetProperty("profitAmount").GetDouble(),
                    Date = e.GetProperty("date").GetDateTime(),
                    Note = e.GetProperty("note").GetString() ?? "",
                    PaymentMethod = e.GetProperty("paymentMethod").GetString() ?? "Cash"
                }).OrderByDescending(s => s.Date).ToList();

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LoadFromCacheAsync error: {ex.Message}");
                return false;
            }
        }
    }
}

