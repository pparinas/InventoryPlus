using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using InventoryPlus.Services;
using InventoryPlus.Models;

namespace InventoryPlus.Pages
{
    [Authorize]
    public partial class Products : ComponentBase, IDisposable
    {
        [Inject] public InventoryService Inventory { get; set; } = default!;
        [Inject] public SettingsService AppSettings { get; set; } = default!;
        [Inject] public Supabase.Client SupabaseClient { get; set; } = default!;
        [Inject] public ToastService Toast { get; set; } = default!;
        [Inject] public Microsoft.JSInterop.IJSRuntime JS { get; set; } = default!;
        protected bool showModal;
        protected bool isEditing;
        protected bool isUploading;
        protected IBrowserFile? pendingImageFile;
        protected Product currentProduct = new();
        protected Guid? selectedIngredientId;
        protected double newQuantity;
        protected string newUsageUnit = "";
        protected string _selectedIngUnit = "";
        protected int _page = 1;
        protected const int PageSize = 10;

        protected string searchQuery = "";
        protected string categoryFilter = "All";
        protected bool showDeleteConfirm;
        protected Product? deleteTarget;
        protected bool showPinPrompt;
        protected double taxRatePercent;
        private Action? _pendingPinAction;
        protected HashSet<string> validationErrors = new();

        protected void SetPage(int p) { _page = p; StateHasChanged(); }

        protected IEnumerable<Product> FilteredProducts
        {
            get
            {
                var items = Inventory.ActiveProducts.AsEnumerable();
                if (categoryFilter != "All")
                    items = items.Where(p => (string.IsNullOrEmpty(p.Category) ? "Other" : p.Category) == categoryFilter);
                if (!string.IsNullOrWhiteSpace(searchQuery))
                    items = items.Where(p => p.Name.Contains(searchQuery, StringComparison.OrdinalIgnoreCase));
                return items;
            }
        }

        protected override void OnInitialized()
        {
            Inventory.OnStateChanged += HandleStateChanged;
        }

        private void HandleStateChanged() => InvokeAsync(StateHasChanged);

        public void Dispose()
        {
            Inventory.OnStateChanged -= HandleStateChanged;
        }

        protected void OpenAddModal()
        {
            if (AppSettings.HasPin)
            {
                _pendingPinAction = () =>
                {
                    currentProduct = new Product { Guid = Guid.NewGuid(), HasIngredients = AppSettings.ShowInventoryTab };
                    taxRatePercent = 0;
                    validationErrors.Clear();
                    isEditing = false;
                    showModal = true;
                    StateHasChanged();
                };
                showPinPrompt = true;
            }
            else
            {
                currentProduct = new Product { Guid = Guid.NewGuid(), HasIngredients = AppSettings.ShowInventoryTab };
                taxRatePercent = 0;
                validationErrors.Clear();
                isEditing = false;
                showModal = true;
            }
        }

        protected void Edit(Product item)
        {
            if (AppSettings.HasPin)
            {
                _pendingPinAction = () =>
                {
                    DoEdit(item);
                    StateHasChanged();
                };
                showPinPrompt = true;
            }
            else
            {
                DoEdit(item);
            }
        }

        private void DoEdit(Product item)
        {
            validationErrors.Clear();
            currentProduct = new Product 
            {
                Guid = item.Guid,
                Name = item.Name,
                SellingPrice = item.SellingPrice,
                TaxRate = item.TaxRate,
                ImageUrl = item.ImageUrl,
                Category = item.Category,
                HasIngredients = AppSettings.ShowInventoryTab && item.HasIngredients,
                StockCount = item.StockCount,
                RequiredIngredients = item.RequiredIngredients.Select(r => new ProductIngredient
                {
                    IngredientId = r.IngredientId,
                    QuantityRequired = r.QuantityRequired,
                    Ingredient = r.Ingredient
                }).ToList()
            };
            taxRatePercent = item.TaxRate * 100;
            isEditing = true;
            showModal = true;
        }

        protected void OnPinVerified()
        {
            _pendingPinAction?.Invoke();
            _pendingPinAction = null;
        }

        protected void ConfirmDelete(Product item)
        {
            deleteTarget = item;
            showDeleteConfirm = true;
        }

        protected async Task ExecuteDelete()
        {
            if (deleteTarget != null)
            {
                try
                {
                    await Inventory.DeleteProductAsync(deleteTarget, JS);
                    Toast.Show($"\"{deleteTarget.Name}\" removed.", "info");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Delete error: {ex.Message}");
                    Toast.Show("Failed to delete product.", "error");
                }
            }
            showDeleteConfirm = false;
            deleteTarget = null;
        }

        protected async Task HandleFileSelected(InputFileChangeEventArgs e)
        {
            pendingImageFile = e.File;
        }

        protected async Task UploadImage()
        {
            if (pendingImageFile == null) return;

            isUploading = true;
            try
            {
                var userId = SupabaseClient.Auth.CurrentUser?.Id;
                if (string.IsNullOrEmpty(userId)) return;

                var format = "image/jpeg";
                var resizedImage = await pendingImageFile.RequestImageFileAsync(format, 200, 200);
                var buffer = new byte[resizedImage.Size];
                await resizedImage.OpenReadStream().ReadAsync(buffer);

                var path = $"{userId}/{currentProduct.Guid}.jpg";
                await SupabaseClient.Storage
                    .From("product-images")
                    .Upload(buffer, path, new Supabase.Storage.FileOptions { ContentType = format, Upsert = true });
                currentProduct.ImageUrl = await SupabaseClient.Storage
                    .From("product-images")
                    .CreateSignedUrl(path, 60 * 60 * 24 * 7);

                pendingImageFile = null;
                Toast.Show("Product image uploaded!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Image upload error: {ex.Message}");
                Toast.Show("Image upload failed.", "error");
            }
            finally
            {
                isUploading = false;
                StateHasChanged();
            }
        }

        protected void AddRequirement()
        {
            if (selectedIngredientId == null || selectedIngredientId == Guid.Empty || newQuantity <= 0) return;

            var ing = Inventory.ActiveIngredients.FirstOrDefault(i => i.Guid == selectedIngredientId);
            if (ing == null) return;

            var existing = currentProduct.RequiredIngredients.FirstOrDefault(r => r.IngredientId == selectedIngredientId);
            if (existing != null)
            {
                existing.QuantityRequired += newQuantity;
            }
            else
            {
                currentProduct.RequiredIngredients.Add(new ProductIngredient
                {
                    IngredientId = selectedIngredientId.Value,
                    QuantityRequired = newQuantity,
                    UsageUnit = newUsageUnit,
                    Ingredient = ing
                });
            }
            
            selectedIngredientId = null;
            newQuantity = 0;
            newUsageUnit = "";
            _selectedIngUnit = "";
        }

        protected void OnIngredientSelected()
        {
            if (selectedIngredientId != null && selectedIngredientId != Guid.Empty)
            {
                var ing = Inventory.ActiveIngredients.FirstOrDefault(i => i.Guid == selectedIngredientId);
                if (ing != null)
                {
                    _selectedIngUnit = ing.Unit;
                    newUsageUnit = ing.Unit;
                    return;
                }
            }
            _selectedIngUnit = "";
            newUsageUnit = "";
        }

        protected void RemoveRequirement(ProductIngredient req)
        {
            currentProduct.RequiredIngredients.Remove(req);
        }

        protected async Task Save()
        {
            validationErrors.Clear();

            if (string.IsNullOrWhiteSpace(currentProduct.Name))
                validationErrors.Add("Name");
            if (currentProduct.SellingPrice <= 0)
                validationErrors.Add("Price");
            if (string.IsNullOrWhiteSpace(currentProduct.Category))
                validationErrors.Add("Category");

            if (validationErrors.Any())
            {
                Toast.Show("Please fill in all required fields.", "error");
                return;
            }

            // Convert percentage to decimal for storage
            currentProduct.TaxRate = taxRatePercent / 100.0;

            try
            {
                if (isEditing)
                {
                    var existing = Inventory.ActiveProducts.FirstOrDefault(p => p.Guid == currentProduct.Guid);
                    if (existing != null)
                    {
                        existing.Name = currentProduct.Name;
                        existing.SellingPrice = currentProduct.SellingPrice;
                        existing.TaxRate = currentProduct.TaxRate;
                        existing.ImageUrl = currentProduct.ImageUrl;
                        existing.Category = currentProduct.Category;
                        existing.HasIngredients = currentProduct.HasIngredients;
                        existing.StockCount = currentProduct.StockCount;
                        existing.RequiredIngredients = currentProduct.HasIngredients ? currentProduct.RequiredIngredients : new();
                        await Inventory.UpdateProductAsync(existing, JS);
                        Toast.Show($"\"{existing.Name}\" updated successfully!");
                    }
                }
                else
                {
                    if (!currentProduct.HasIngredients)
                        currentProduct.RequiredIngredients = new();
                    await Inventory.AddProductAsync(currentProduct, JS);
                    Toast.Show($"\"{currentProduct.Name}\" added to products!");
                }
                CloseModal();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Save error: {ex.Message}");
                Toast.Show("Failed to save product.", "error");
            }
        }

        protected void CloseModal() => showModal = false;
    }
}
