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
        private const string PendingKeyPrefix = "inv_pending_";

        public IEnumerable<Ingredient> ActiveIngredients => Ingredients.Where(i => !i.IsArchived);
        public IEnumerable<Product> ActiveProducts => Products.Where(p => !p.IsArchived);

        public event Action? OnStateChanged;

        // Pending writes queue (in-memory; persisted to localStorage)
        private readonly Queue<PendingWrite> _pendingWrites = new();

        private record PendingWrite(string Op, string Payload, DateTime QueuedAt);

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
                    // Also restore any unflushed pending writes
                    await LoadPendingQueueAsync(js, userId);
                }

                // Load all tables in parallel (5 queries → 1 round-trip window)
                var ingredientsTask = _supabase.From<Ingredient>()
                    .Where(i => i.OwnerGuid == _ownerGuid).Get();
                var productsTask = _supabase.From<Product>()
                    .Where(p => p.OwnerGuid == _ownerGuid).Get();
                var piTask = _supabase.From<ProductIngredient>()
                    .Where(pi => pi.OwnerId == _ownerGuid).Get();
                var salesTask = _supabase.From<Sale>()
                    .Where(s => s.OwnerId == _ownerGuid).Get();
                var opexTask = _supabase.From<Opex>()
                    .Where(o => o.OwnerGuid == _ownerGuid).Get();

                await Task.WhenAll(ingredientsTask, productsTask, piTask, salesTask, opexTask);

                Ingredients = ingredientsTask.Result.Models;
                Products = productsTask.Result.Models;
                var piModels = piTask.Result.Models;

                foreach (var product in Products)
                {
                    product.RequiredIngredients = piModels
                        .Where(pi => pi.ProductId == product.Guid)
                        .ToList();

                    foreach (var pi in product.RequiredIngredients)
                        pi.Ingredient = Ingredients.FirstOrDefault(i => i.Guid == pi.IngredientId);
                }

                // Refresh product image signed URLs in parallel
                await RefreshProductImageUrlsAsync();

                Sales = salesTask.Result.Models.OrderByDescending(s => s.Date).ToList();
                OpexItems = opexTask.Result.Models.OrderByDescending(o => o.Date).ToList();

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

        public async Task AddIngredientAsync(Ingredient ingredient, IJSRuntime? js = null)
        {
            ingredient.OwnerGuid = _ownerGuid;
            if (ingredient.Guid == Guid.Empty) ingredient.Guid = Guid.NewGuid();

            // Optimistic: update local immediately
            Ingredients.Add(ingredient);
            if (js != null) await SaveCacheAsync(js);
            NotifyStateChanged();

            if (IsGuestMode) return;

            // Background sync
            _ = SyncSafeAsync(js, async () =>
            {
                var resp = await _supabase.From<Ingredient>().Insert(ingredient);
                var saved = resp.Models.FirstOrDefault();
                if (saved != null) ingredient.Guid = saved.Guid;
            });
        }

        public async Task UpdateIngredientAsync(Ingredient ingredient, IJSRuntime? js = null)
        {
            ingredient.OwnerGuid = _ownerGuid;
            if (js != null) await SaveCacheAsync(js);
            NotifyStateChanged();

            if (!IsGuestMode)
                _ = SyncSafeAsync(js, () => _supabase.From<Ingredient>().Upsert(ingredient));
        }

        public async Task DeleteIngredientAsync(Ingredient ingredient, IJSRuntime? js = null)
        {
            ingredient.IsArchived = true;
            if (js != null) await SaveCacheAsync(js);
            NotifyStateChanged();

            if (!IsGuestMode)
                _ = SyncSafeAsync(js, () => _supabase.From<Ingredient>().Upsert(ingredient));
        }

        // ── Products ───────────────────────────────────────────────────────────

        public async Task AddProductAsync(Product product, IJSRuntime? js = null)
        {
            product.OwnerGuid = _ownerGuid;
            if (product.Guid == Guid.Empty) product.Guid = Guid.NewGuid();

            foreach (var pi in product.RequiredIngredients)
            {
                pi.ProductId = product.Guid;
                if (pi.Guid == Guid.Empty) pi.Guid = Guid.NewGuid();
            }

            // Optimistic: update local immediately
            Products.Add(product);
            if (js != null) await SaveCacheAsync(js);
            NotifyStateChanged();

            if (IsGuestMode) return;

            _ = SyncSafeAsync(js, async () =>
            {
                var requirements = product.RequiredIngredients.ToList();
                product.RequiredIngredients = new();
                await _supabase.From<Product>().Upsert(product);
                product.RequiredIngredients = requirements;

                var piTasks = requirements.Select(pi =>
                {
                    pi.ProductId = product.Guid;
                    pi.OwnerId = _ownerGuid;
                    var savedIngredient = pi.Ingredient;
                    pi.Ingredient = null;
                    var task = _supabase.From<ProductIngredient>().Upsert(pi);
                    return task.ContinueWith(_ => pi.Ingredient = savedIngredient);
                });
                await Task.WhenAll(piTasks);
            });
        }

        public async Task UpdateProductAsync(Product product, IJSRuntime? js = null)
        {
            product.OwnerGuid = _ownerGuid;
            if (js != null) await SaveCacheAsync(js);
            NotifyStateChanged();

            if (IsGuestMode) return;

            _ = SyncSafeAsync(js, async () =>
            {
                var requirements = product.RequiredIngredients.ToList();
                product.RequiredIngredients = new();
                await _supabase.From<Product>().Upsert(product);
                product.RequiredIngredients = requirements;

                var existingPIs = await _supabase.From<ProductIngredient>()
                    .Where(pi => pi.ProductId == product.Guid).Get();

                var toDelete = existingPIs.Models.Where(old => !requirements.Any(r => r.Guid == old.Guid));
                var deleteTasks = toDelete.Select(old => (Task)_supabase.From<ProductIngredient>().Delete(old));

                var upsertTasks = requirements.Select(pi =>
                {
                    pi.ProductId = product.Guid;
                    pi.OwnerId = _ownerGuid;
                    var savedIngredient = pi.Ingredient;
                    pi.Ingredient = null;
                    var task = _supabase.From<ProductIngredient>().Upsert(pi);
                    return (Task)task.ContinueWith(_ => pi.Ingredient = savedIngredient ?? Ingredients.FirstOrDefault(i => i.Guid == pi.IngredientId));
                });

                await Task.WhenAll(deleteTasks.Concat(upsertTasks));
            });
        }

        public async Task DeleteProductAsync(Product product, IJSRuntime? js = null)
        {
            product.IsArchived = true;
            if (js != null) await SaveCacheAsync(js);
            NotifyStateChanged();

            if (IsGuestMode) return;

            _ = SyncSafeAsync(js, async () =>
            {
                var requirements = product.RequiredIngredients.ToList();
                product.RequiredIngredients = new();
                await _supabase.From<Product>().Upsert(product);
                product.RequiredIngredients = requirements;
            });
        }

        // ── Sales ──────────────────────────────────────────────────────────────

        public async Task<Sale?> RecordSaleAsync(Product product, int quantity, string note = "", string paymentMethod = "Cash", string customerName = "", double discountAmount = 0, string discountType = "None", IJSRuntime? js = null)
        {
            if (product.AvailableCount < quantity) return null;

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

            // Update local state immediately
            if (product.HasIngredients)
            {
                foreach (var req in product.RequiredIngredients)
                {
                    if (req.Ingredient != null)
                    {
                        var deduct = UnitConverter.ConvertSafe(req.QuantityRequired, req.EffectiveUsageUnit, req.Ingredient.Unit) * quantity;
                        req.Ingredient.Stock -= deduct;
                    }
                }
            }
            else
            {
                product.StockCount -= quantity;
                if (product.StockCount < 0) product.StockCount = 0;
            }

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

            Sales.Insert(0, sale);
            if (js != null) await SaveCacheAsync(js);
            NotifyStateChanged();

            if (IsGuestMode)
            {
                sale.Guid = Guid.NewGuid();
                return sale;
            }

            // Background sync
            _ = SyncSafeAsync(js, async () =>
            {
                try
                {
                    var ingredientUpdates = product.HasIngredients
                        ? product.RequiredIngredients
                            .Where(r => r.Ingredient != null)
                            .Select(r => new { id = r.IngredientId, deduct = UnitConverter.ConvertSafe(r.QuantityRequired, r.EffectiveUsageUnit, r.Ingredient!.Unit) * quantity })
                            .ToArray()
                        : Array.Empty<object>();

                    var rpcParams = new Dictionary<string, object>
                    {
                        ["p_owner_id"] = _ownerGuid.ToString(),
                        ["p_product_id"] = product.Guid.ToString(),
                        ["p_product_name"] = product.Name,
                        ["p_quantity_sold"] = quantity,
                        ["p_total_amount"] = total,
                        ["p_tax_amount"] = tax,
                        ["p_profit_amount"] = profit,
                        ["p_note"] = note,
                        ["p_payment_method"] = paymentMethod,
                        ["p_customer_name"] = customerName,
                        ["p_discount_amount"] = discountAmount,
                        ["p_discount_type"] = discountType,
                        ["p_has_ingredients"] = product.HasIngredients,
                        ["p_ingredient_updates"] = JsonSerializer.Serialize(ingredientUpdates)
                    };

                    var rpcResult = await _supabase.Rpc("record_sale_with_stock", rpcParams);
                    if (rpcResult != null)
                    {
                        var content = rpcResult.Content;
                        if (content != null && Guid.TryParse(content.Trim('"'), out var saleGuid))
                            sale.Guid = saleGuid;
                    }
                }
                catch
                {
                    // RPC not deployed yet — fall back to parallel approach
                    if (product.HasIngredients)
                    {
                        var stockTasks = product.RequiredIngredients
                            .Where(req => req.Ingredient != null)
                            .Select(req => _supabase.From<Ingredient>().Upsert(req.Ingredient!));
                        await Task.WhenAll(stockTasks);
                    }
                    else
                    {
                        var reqs = product.RequiredIngredients.ToList();
                        product.RequiredIngredients = new();
                        await _supabase.From<Product>().Upsert(product);
                        product.RequiredIngredients = reqs;
                    }

                    var resp = await _supabase.From<Sale>().Insert(sale);
                    var saved = resp.Models.FirstOrDefault();
                    if (saved != null) sale.Guid = saved.Guid;
                }
            });

            return sale;
        }

        public void NotifyStateChanged() => OnStateChanged?.Invoke();

        // ── OPEX ───────────────────────────────────────────────────────────────

        public async Task AddOpexAsync(Opex opex, IJSRuntime? js = null)
        {
            opex.OwnerGuid = _ownerGuid;
            if (opex.Guid == Guid.Empty) opex.Guid = Guid.NewGuid();

            OpexItems.Insert(0, opex);
            if (js != null) await SaveCacheAsync(js);
            NotifyStateChanged();

            if (IsGuestMode) return;

            _ = SyncSafeAsync(js, async () =>
            {
                var resp = await _supabase.From<Opex>().Insert(opex);
                var saved = resp.Models.FirstOrDefault();
                if (saved != null) opex.Guid = saved.Guid;
            });
        }

        public async Task UpdateOpexAsync(Opex opex, IJSRuntime? js = null)
        {
            opex.OwnerGuid = _ownerGuid;
            if (js != null) await SaveCacheAsync(js);
            NotifyStateChanged();

            if (!IsGuestMode)
                _ = SyncSafeAsync(js, () => _supabase.From<Opex>().Upsert(opex));
        }

        public async Task DeleteOpexAsync(Opex opex, IJSRuntime? js = null)
        {
            opex.IsArchived = true;
            if (js != null) await SaveCacheAsync(js);
            NotifyStateChanged();

            if (!IsGuestMode)
                _ = SyncSafeAsync(js, () => _supabase.From<Opex>().Upsert(opex));
        }

        // ── Void Sale ──────────────────────────────────────────────────────────

        public async Task VoidSaleAsync(Sale sale, IJSRuntime? js = null)
        {
            if (sale.IsVoided) return;

            // Restore stock locally
            var product = Products.FirstOrDefault(p => p.Guid == sale.ProductId);
            if (product != null)
            {
                if (product.HasIngredients)
                {
                    foreach (var req in product.RequiredIngredients)
                    {
                        if (req.Ingredient != null)
                        {
                            var restore = UnitConverter.ConvertSafe(req.QuantityRequired, req.EffectiveUsageUnit, req.Ingredient.Unit) * sale.QuantitySold;
                            req.Ingredient.Stock += restore;
                        }
                    }
                }
                else
                {
                    product.StockCount += sale.QuantitySold;
                }
            }

            sale.IsVoided = true;
            if (js != null) await SaveCacheAsync(js);
            NotifyStateChanged();

            if (IsGuestMode) return;

            _ = SyncSafeAsync(js, async () =>
            {
                try
                {
                    var ingredientUpdates = (product?.HasIngredients == true)
                        ? product.RequiredIngredients
                            .Where(r => r.Ingredient != null)
                            .Select(r => new { id = r.IngredientId, restore = UnitConverter.ConvertSafe(r.QuantityRequired, r.EffectiveUsageUnit, r.Ingredient!.Unit) * sale.QuantitySold })
                            .ToArray()
                        : Array.Empty<object>();

                    var rpcParams = new Dictionary<string, object>
                    {
                        ["p_owner_id"] = _ownerGuid.ToString(),
                        ["p_sale_id"] = sale.Guid.ToString(),
                        ["p_product_id"] = sale.ProductId.ToString(),
                        ["p_quantity_sold"] = sale.QuantitySold,
                        ["p_has_ingredients"] = product?.HasIngredients ?? false,
                        ["p_ingredient_updates"] = JsonSerializer.Serialize(ingredientUpdates)
                    };

                    await _supabase.Rpc("void_sale_with_stock", rpcParams);
                }
                catch
                {
                    if (product != null)
                    {
                        if (product.HasIngredients)
                        {
                            var stockTasks = product.RequiredIngredients
                                .Where(req => req.Ingredient != null)
                                .Select(req => _supabase.From<Ingredient>().Upsert(req.Ingredient!));
                            await Task.WhenAll(stockTasks);
                        }
                        else
                        {
                            var reqs = product.RequiredIngredients.ToList();
                            product.RequiredIngredients = new();
                            await _supabase.From<Product>().Upsert(product);
                            product.RequiredIngredients = reqs;
                        }
                    }
                    await _supabase.From<Sale>().Upsert(sale);
                }
            });
        }

        public async Task UpdateSaleAsync(Sale sale, IJSRuntime? js = null)
        {
            if (js != null) await SaveCacheAsync(js);
            NotifyStateChanged();

            if (!IsGuestMode)
                _ = SyncSafeAsync(js, () => _supabase.From<Sale>().Upsert(sale));
        }

        // ── Background sync helper ─────────────────────────────────────────────

        /// <summary>
        /// Runs a Supabase operation in the background.
        /// On failure, marks the service as offline (if not already) but keeps local state intact.
        /// </summary>
        private async Task SyncSafeAsync(IJSRuntime? js, Func<Task> operation)
        {
            try
            {
                await operation();
                if (IsOffline)
                {
                    IsOffline = false;
                    NotifyStateChanged();
                }
                // Re-save cache after confirmed sync to keep it fresh
                if (js != null) await SaveCacheAsync(js);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Background sync failed: {ex.Message}");
                IsOffline = true;
                NotifyStateChanged();
            }
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
            var normalizedId = Guid.TryParse(userId, out var g) ? g.ToString("D") : userId.ToLowerInvariant();
            try { await js.InvokeVoidAsync("localStorage.removeItem", $"{CacheKeyPrefix}{normalizedId}"); }
            catch { }
        }

        public async Task SaveCacheAsync(IJSRuntime js)
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
                        isArchived = p.IsArchived,
                        hasIngredients = p.HasIngredients,
                        stockCount = p.StockCount
                    }),
                    productIngredients = Products.SelectMany(p => p.RequiredIngredients).Select(pi => new
                    {
                        guid = pi.Guid, ownerId = pi.OwnerId, productId = pi.ProductId,
                        ingredientId = pi.IngredientId, quantityRequired = pi.QuantityRequired,
                        usageUnit = pi.UsageUnit
                    }),
                    sales = Sales.Select(s => new
                    {
                        guid = s.Guid, ownerId = s.OwnerId, productId = s.ProductId,
                        productName = s.ProductName, quantitySold = s.QuantitySold,
                        totalAmount = s.TotalAmount, taxAmount = s.TaxAmount,
                        profitAmount = s.ProfitAmount, date = s.Date,
                        note = s.Note, paymentMethod = s.PaymentMethod,
                        isVoided = s.IsVoided, customerName = s.CustomerName,
                        discountAmount = s.DiscountAmount, discountType = s.DiscountType
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
                await js.InvokeVoidAsync("localStorage.setItem", $"{CacheKeyPrefix}{_ownerGuid:D}", json);
            }
            catch (Exception ex) { Console.WriteLine($"SaveCacheAsync error: {ex.Message}"); }
        }

        private async Task<bool> LoadFromCacheAsync(IJSRuntime js, string userId)
        {
            try
            {
                var normalizedId = Guid.TryParse(userId, out var g) ? g.ToString("D") : userId.ToLowerInvariant();
                var json = await js.InvokeAsync<string?>("localStorage.getItem", $"{CacheKeyPrefix}{normalizedId}");
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
                    QuantityRequired = e.GetProperty("quantityRequired").GetDouble(),
                    UsageUnit = e.TryGetProperty("usageUnit", out var uu) ? uu.GetString() ?? "" : ""
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
                        HasIngredients = e.TryGetProperty("hasIngredients", out var hi) && hi.GetBoolean(),
                        StockCount = e.TryGetProperty("stockCount", out var sc) ? sc.GetDouble() : 0,
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
                    PaymentMethod = e.GetProperty("paymentMethod").GetString() ?? "Cash",
                    IsVoided = e.TryGetProperty("isVoided", out var iv) && iv.GetBoolean(),
                    CustomerName = e.TryGetProperty("customerName", out var cn) ? cn.GetString() ?? "" : "",
                    DiscountAmount = e.TryGetProperty("discountAmount", out var da) ? da.GetDouble() : 0,
                    DiscountType = e.TryGetProperty("discountType", out var dt) ? dt.GetString() ?? "None" : "None"
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

        // ── Pending Queue ──────────────────────────────────────────────────────

        private async Task LoadPendingQueueAsync(IJSRuntime js, string userId)
        {
            try
            {
                var normalizedId = Guid.TryParse(userId, out var g) ? g.ToString("D") : userId.ToLowerInvariant();
                var json = await js.InvokeAsync<string?>("localStorage.getItem", $"{PendingKeyPrefix}{normalizedId}");
                if (string.IsNullOrEmpty(json)) return;
                var items = JsonSerializer.Deserialize<List<PendingWriteDto>>(json);
                if (items == null) return;
                _pendingWrites.Clear();
                foreach (var item in items)
                    _pendingWrites.Enqueue(new PendingWrite(item.Op, item.Payload, item.QueuedAt));
            }
            catch { }
        }

        private async Task SavePendingQueueAsync(IJSRuntime js)
        {
            try
            {
                var list = _pendingWrites.Select(p => new PendingWriteDto { Op = p.Op, Payload = p.Payload, QueuedAt = p.QueuedAt }).ToList();
                var json = JsonSerializer.Serialize(list);
                await js.InvokeVoidAsync("localStorage.setItem", $"{PendingKeyPrefix}{_ownerGuid}", json);
            }
            catch { }
        }

        private class PendingWriteDto
        {
            public string Op { get; set; } = "";
            public string Payload { get; set; } = "";
            public DateTime QueuedAt { get; set; }
        }

        // ── Image URL helpers ──────────────────────────────────────────────────

        // Cache signed URLs to avoid re-signing on every load (path → {url, expiry})
        private readonly Dictionary<string, (string Url, DateTime Expiry)> _signedUrlCache = new();
        private static readonly TimeSpan SignedUrlTtl = TimeSpan.FromDays(6); // re-sign before 7-day expiry

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
            var productsWithImages = Products
                .Where(p => !string.IsNullOrEmpty(p.ImageUrl))
                .ToList();
            if (productsWithImages.Count == 0) return;

            var tasks = productsWithImages.Select(async product =>
            {
                var path = ExtractProductImagePath(product.ImageUrl);
                if (string.IsNullOrEmpty(path)) return;
                try
                {
                    product.ImageUrl = await GetOrCreateSignedUrlAsync("product-images", path);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to refresh image URL for {product.Name}: {ex.Message}");
                }
            });
            await Task.WhenAll(tasks);
        }

        private async Task<string> GetOrCreateSignedUrlAsync(string bucket, string path)
        {
            var cacheKey = $"{bucket}/{path}";
            if (_signedUrlCache.TryGetValue(cacheKey, out var cached) && cached.Expiry > DateTime.UtcNow)
                return cached.Url;

            var url = await _supabase.Storage
                .From(bucket)
                .CreateSignedUrl(path, 60 * 60 * 24 * 7);
            _signedUrlCache[cacheKey] = (url, DateTime.UtcNow + SignedUrlTtl);
            return url;
        }
    }
}
