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
        public List<Opex> OpexItems { get; set; } = new();

        public IEnumerable<Opex> ActiveOpex => OpexItems.Where(o => !o.IsArchived);

        public bool IsLoaded { get; set; }
        public bool IsLoading { get; private set; }
        public bool IsOffline { get; private set; }
        public bool IsGuestMode { get; set; }

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

                // Refresh product image signed URLs
                await RefreshProductImageUrlsAsync();

                // Load sales
                var salesResp = await _supabase.From<Sale>()
                    .Where(s => s.OwnerId == _ownerGuid)
                    .Get();
                Sales = salesResp.Models.OrderByDescending(s => s.Date).ToList();

                // Load OPEX
                var opexResp = await _supabase.From<Opex>()
                    .Where(o => o.OwnerGuid == _ownerGuid)
                    .Get();
                OpexItems = opexResp.Models.OrderByDescending(o => o.Date).ToList();

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
                NotifyStateChanged();
            }
        }

        // ── Ingredients ────────────────────────────────────────────────────────

        public async Task AddIngredientAsync(Ingredient ingredient)
        {
            ingredient.OwnerGuid = _ownerGuid;
            if (IsGuestMode)
            {
                if (ingredient.Guid == Guid.Empty) ingredient.Guid = Guid.NewGuid();
                Ingredients.Add(ingredient);
                NotifyStateChanged();
                return;
            }
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
            if (!IsGuestMode) await _supabase.From<Ingredient>().Upsert(ingredient);
            NotifyStateChanged();
        }

        public async Task DeleteIngredientAsync(Ingredient ingredient)
        {
            ingredient.IsArchived = true;
            if (!IsGuestMode) await _supabase.From<Ingredient>().Upsert(ingredient);
            NotifyStateChanged();
        }

        // ── Products ───────────────────────────────────────────────────────────

        public async Task AddProductAsync(Product product)
        {
            product.OwnerGuid = _ownerGuid;
            if (IsGuestMode)
            {
                if (product.Guid == Guid.Empty) product.Guid = Guid.NewGuid();
                foreach (var pi in product.RequiredIngredients)
                {
                    pi.ProductId = product.Guid;
                    if (pi.Guid == Guid.Empty) pi.Guid = Guid.NewGuid();
                }
                Products.Add(product);
                NotifyStateChanged();
                return;
            }
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
            if (IsGuestMode) { NotifyStateChanged(); return; }
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
            if (IsGuestMode) { NotifyStateChanged(); return; }
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

            // Deduct stock based on product type
            if (product.HasIngredients)
            {
                // Deduct ingredient stock and persist
                foreach (var req in product.RequiredIngredients)
                {
                    if (req.Ingredient != null)
                    {
                        req.Ingredient.Stock -= req.QuantityRequired * quantity;
                        if (!IsGuestMode) await _supabase.From<Ingredient>().Upsert(req.Ingredient);
                    }
                }
            }
            else
            {
                // Deduct direct stock count
                product.StockCount -= quantity;
                if (product.StockCount < 0) product.StockCount = 0;
                if (!IsGuestMode)
                {
                    var reqs = product.RequiredIngredients.ToList();
                    product.RequiredIngredients = new();
                    await _supabase.From<Product>().Upsert(product);
                    product.RequiredIngredients = reqs;
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

            if (IsGuestMode)
            {
                sale.Guid = Guid.NewGuid();
                Sales.Insert(0, sale);
                NotifyStateChanged();
                return sale;
            }

            var resp = await _supabase.From<Sale>().Insert(sale);
            var saved = resp.Models.FirstOrDefault() ?? sale;

            Sales.Insert(0, saved);

            NotifyStateChanged();
            return saved;
        }

        public void NotifyStateChanged() => OnStateChanged?.Invoke();

        // ── OPEX ───────────────────────────────────────────────────────────────

        public async Task AddOpexAsync(Opex opex)
        {
            opex.OwnerGuid = _ownerGuid;
            if (IsGuestMode)
            {
                if (opex.Guid == Guid.Empty) opex.Guid = Guid.NewGuid();
                OpexItems.Insert(0, opex);
                NotifyStateChanged();
                return;
            }
            var resp = await _supabase.From<Opex>().Insert(opex);
            var saved = resp.Models.FirstOrDefault();
            if (saved != null) opex.Guid = saved.Guid;
            OpexItems.Insert(0, opex);
            NotifyStateChanged();
        }

        public async Task UpdateOpexAsync(Opex opex)
        {
            opex.OwnerGuid = _ownerGuid;
            if (!IsGuestMode) await _supabase.From<Opex>().Upsert(opex);
            NotifyStateChanged();
        }

        public async Task DeleteOpexAsync(Opex opex)
        {
            opex.IsArchived = true;
            if (!IsGuestMode) await _supabase.From<Opex>().Upsert(opex);
            NotifyStateChanged();
        }

        // ── Void Sale ──────────────────────────────────────────────────────────

        public async Task VoidSaleAsync(Sale sale)
        {
            if (sale.IsVoided) return;

            // Restore stock
            var product = Products.FirstOrDefault(p => p.Guid == sale.ProductId);
            if (product != null)
            {
                if (product.HasIngredients)
                {
                    foreach (var req in product.RequiredIngredients)
                    {
                        if (req.Ingredient != null)
                        {
                            req.Ingredient.Stock += req.QuantityRequired * sale.QuantitySold;
                            if (!IsGuestMode) await _supabase.From<Ingredient>().Upsert(req.Ingredient);
                        }
                    }
                }
                else
                {
                    product.StockCount += sale.QuantitySold;
                    if (!IsGuestMode)
                    {
                        var reqs = product.RequiredIngredients.ToList();
                        product.RequiredIngredients = new();
                        await _supabase.From<Product>().Upsert(product);
                        product.RequiredIngredients = reqs;
                    }
                }
            }

            sale.IsVoided = true;
            if (!IsGuestMode) await _supabase.From<Sale>().Upsert(sale);
            NotifyStateChanged();
        }

        public async Task UpdateSaleAsync(Sale sale)
        {
            if (!IsGuestMode) await _supabase.From<Sale>().Upsert(sale);
            NotifyStateChanged();
        }

        // ── Guest Mode ─────────────────────────────────────────────────────────

        private const string GuestCacheKey = "inv_cache_guest";

        public async Task LoadGuestAsync(IJSRuntime js)
        {
            if (IsLoading) return;
            IsLoading = true;
            IsGuestMode = true;
            try
            {
                var json = await js.InvokeAsync<string?>("localStorage.getItem", GuestCacheKey);
                if (!string.IsNullOrEmpty(json))
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("ingredients", out var ingEl))
                        Ingredients = ingEl.EnumerateArray().Select(e => new Ingredient
                        {
                            Guid = e.GetProperty("guid").GetGuid(),
                            OwnerGuid = Guid.Empty,
                            Name = e.GetProperty("name").GetString() ?? "",
                            Unit = e.GetProperty("unit").GetString() ?? "",
                            Stock = e.GetProperty("stock").GetDouble(),
                            CostPerUnit = e.GetProperty("costPerUnit").GetDouble(),
                            Type = e.TryGetProperty("type", out var t) ? t.GetString() ?? "Ingredient" : "Ingredient",
                            IsArchived = e.TryGetProperty("isArchived", out var a) && a.GetBoolean()
                        }).ToList();

                    if (root.TryGetProperty("products", out var prodEl))
                        Products = prodEl.EnumerateArray().Select(e => new Product
                        {
                            Guid = e.GetProperty("guid").GetGuid(),
                            OwnerGuid = Guid.Empty,
                            Name = e.GetProperty("name").GetString() ?? "",
                            SellingPrice = e.GetProperty("sellingPrice").GetDouble(),
                            TaxRate = e.TryGetProperty("taxRate", out var tr) ? tr.GetDouble() : 0,
                            IsArchived = e.TryGetProperty("isArchived", out var a2) && a2.GetBoolean()
                        }).ToList();

                    if (root.TryGetProperty("sales", out var saleEl))
                        Sales = saleEl.EnumerateArray().Select(e => new Sale
                        {
                            Guid = e.GetProperty("guid").GetGuid(),
                            OwnerId = Guid.Empty,
                            ProductName = e.GetProperty("productName").GetString() ?? "",
                            QuantitySold = e.GetProperty("quantitySold").GetInt32(),
                            TotalAmount = e.GetProperty("totalAmount").GetDouble(),
                            ProfitAmount = e.TryGetProperty("profitAmount", out var pa) ? pa.GetDouble() : 0,
                            Date = e.GetProperty("date").GetDateTime(),
                            PaymentMethod = e.TryGetProperty("paymentMethod", out var pm) ? pm.GetString() ?? "Cash" : "Cash"
                        }).ToList();

                    if (root.TryGetProperty("opex", out var opexGuestEl))
                        OpexItems = opexGuestEl.EnumerateArray().Select(e => new Opex
                        {
                            Guid = e.GetProperty("guid").GetGuid(),
                            OwnerGuid = Guid.Empty,
                            Name = e.GetProperty("name").GetString() ?? "",
                            Category = e.TryGetProperty("category", out var c) ? c.GetString() ?? "Other" : "Other",
                            Amount = e.GetProperty("amount").GetDouble(),
                            Date = e.GetProperty("date").GetDateTime(),
                            Note = e.TryGetProperty("note", out var n) ? n.GetString() ?? "" : "",
                            IsRecurring = e.TryGetProperty("isRecurring", out var ir) && ir.GetBoolean(),
                            Recurrence = e.TryGetProperty("recurrence", out var r) ? r.GetString() ?? "None" : "None",
                            IsArchived = e.TryGetProperty("isArchived", out var ar) && ar.GetBoolean()
                        }).OrderByDescending(o => o.Date).ToList();
                }

                IsLoaded = true;
                NotifyStateChanged();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LoadGuestAsync error: {ex.Message}");
                IsLoaded = true;
                NotifyStateChanged();
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task SaveGuestCacheAsync(IJSRuntime js)
        {
            try
            {
                var snapshot = new
                {
                    ingredients = Ingredients.Select(i => new { guid = i.Guid, name = i.Name, unit = i.Unit, stock = i.Stock, costPerUnit = i.CostPerUnit, type = i.Type, isArchived = i.IsArchived }),
                    products = Products.Select(p => new { guid = p.Guid, name = p.Name, sellingPrice = p.SellingPrice, taxRate = p.TaxRate, isArchived = p.IsArchived }),
                    sales = Sales.Select(s => new { guid = s.Guid, productName = s.ProductName, quantitySold = s.QuantitySold, totalAmount = s.TotalAmount, profitAmount = s.ProfitAmount, date = s.Date, paymentMethod = s.PaymentMethod }),
                    opex = OpexItems.Select(o => new { guid = o.Guid, name = o.Name, category = o.Category, amount = o.Amount, date = o.Date, note = o.Note, isRecurring = o.IsRecurring, recurrence = o.Recurrence, isArchived = o.IsArchived })
                };
                var json = JsonSerializer.Serialize(snapshot);
                await js.InvokeVoidAsync("localStorage.setItem", GuestCacheKey, json);
            }
            catch (Exception ex) { Console.WriteLine($"SaveGuestCacheAsync error: {ex.Message}"); }
        }

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
                        imageUrl = ExtractProductImagePath(p.ImageUrl) ?? p.ImageUrl,
                        isArchived = p.IsArchived
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
                    }),
                    opex = OpexItems.Select(o => new
                    {
                        guid = o.Guid, ownerGuid = o.OwnerGuid, name = o.Name,
                        category = o.Category, amount = o.Amount, date = o.Date,
                        note = o.Note, isRecurring = o.IsRecurring,
                        recurrence = o.Recurrence, isArchived = o.IsArchived
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

                // Refresh product image signed URLs from cache paths
                await RefreshProductImageUrlsAsync();

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

                if (root.TryGetProperty("opex", out var opexEl))
                    OpexItems = opexEl.EnumerateArray().Select(e => new Opex
                    {
                        Guid = e.GetProperty("guid").GetGuid(),
                        OwnerGuid = e.GetProperty("ownerGuid").GetGuid(),
                        Name = e.GetProperty("name").GetString() ?? "",
                        Category = e.GetProperty("category").GetString() ?? "Other",
                        Amount = e.GetProperty("amount").GetDouble(),
                        Date = e.GetProperty("date").GetDateTime(),
                        Note = e.GetProperty("note").GetString() ?? "",
                        IsRecurring = e.GetProperty("isRecurring").GetBoolean(),
                        Recurrence = e.GetProperty("recurrence").GetString() ?? "None",
                        IsArchived = e.GetProperty("isArchived").GetBoolean()
                    }).OrderByDescending(o => o.Date).ToList();

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LoadFromCacheAsync error: {ex.Message}");
                return false;
            }
        }

        // ── Image URL helpers ──────────────────────────────────────────────────

        private static string? ExtractProductImagePath(string? urlOrPath)
        {
            if (string.IsNullOrEmpty(urlOrPath)) return null;
            if (!urlOrPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return urlOrPath;
            foreach (var marker in new[] { "/object/public/product-images/", "/object/sign/product-images/" })
            {
                var idx = urlOrPath.IndexOf(marker, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    var p = urlOrPath.Substring(idx + marker.Length);
                    var q = p.IndexOf('?');
                    return q >= 0 ? p.Substring(0, q) : p;
                }
            }
            return null;
        }

        private async Task RefreshProductImageUrlsAsync()
        {
            foreach (var product in Products)
            {
                if (string.IsNullOrEmpty(product.ImageUrl)) continue;
                var path = ExtractProductImagePath(product.ImageUrl);
                if (string.IsNullOrEmpty(path)) continue;
                try
                {
                    product.ImageUrl = await _supabase.Storage
                        .From("product-images")
                        .CreateSignedUrl(path, 60 * 60 * 24 * 7);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to refresh image URL for {product.Name}: {ex.Message}");
                }
            }
        }
    }
}

