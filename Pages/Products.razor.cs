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
        protected bool showModal;
        protected bool isEditing;
        protected bool isUploading;
        protected IBrowserFile? pendingImageFile;
        protected Product currentProduct = new();
        
        protected Guid? selectedIngredientId;
        protected double newQuantity;

        protected override void OnInitialized()
        {
            Inventory.OnStateChanged += HandleStateChanged;
        }

        private void HandleStateChanged() => StateHasChanged();

        public void Dispose()
        {
            Inventory.OnStateChanged -= HandleStateChanged;
        }

        protected void OpenAddModal()
        {
            currentProduct = new Product { Guid = Guid.NewGuid() };
            isEditing = false;
            showModal = true;
        }

        protected void Edit(Product item)
        {
            currentProduct = new Product 
            {
                Guid = item.Guid,
                Name = item.Name,
                SellingPrice = item.SellingPrice,
                TaxRate = item.TaxRate,
                ImageUrl = item.ImageUrl,
                RequiredIngredients = item.RequiredIngredients.Select(r => new ProductIngredient
                {
                    IngredientId = r.IngredientId,
                    QuantityRequired = r.QuantityRequired,
                    Ingredient = r.Ingredient
                }).ToList()
            };
            isEditing = true;
            showModal = true;
        }

        protected void Delete(Product item)
        {
            Inventory.DeleteProduct(item);
            Toast.Show($"\"{item.Name}\" removed.", "info");
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

                var format = "image/png";
                var resizedImage = await pendingImageFile.RequestImageFileAsync(format, 300, 300);
                var buffer = new byte[resizedImage.Size];
                await resizedImage.OpenReadStream().ReadAsync(buffer);

                var path = $"{userId}/{currentProduct.Guid}.png";
                await SupabaseClient.Storage
                    .From("product-images")
                    .Upload(buffer, path, new Supabase.Storage.FileOptions { ContentType = format, Upsert = true });
                currentProduct.ImageUrl = await SupabaseClient.Storage
                    .From("product-images")
                    .CreateSignedUrl(path, 60 * 60 * 24 * 365);

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
                    Ingredient = ing
                });
            }
            
            selectedIngredientId = null;
            newQuantity = 0;
        }

        protected void RemoveRequirement(ProductIngredient req)
        {
            currentProduct.RequiredIngredients.Remove(req);
        }

        protected void Save()
        {
            if (string.IsNullOrWhiteSpace(currentProduct.Name)) return;

            if (isEditing)
            {
                var existing = Inventory.ActiveProducts.FirstOrDefault(p => p.Guid == currentProduct.Guid);
                if (existing != null)
                {
                    existing.Name = currentProduct.Name;
                    existing.SellingPrice = currentProduct.SellingPrice;
                    existing.TaxRate = currentProduct.TaxRate;
                    existing.ImageUrl = currentProduct.ImageUrl;
                    existing.RequiredIngredients = currentProduct.RequiredIngredients;
                    Inventory.UpdateProduct(existing);
                    Toast.Show($"\"{existing.Name}\" updated successfully!");
                }
            }
            else
            {
                Inventory.AddProduct(currentProduct);
                Toast.Show($"\"{currentProduct.Name}\" added to products!");
            }
            CloseModal();
        }

        protected void CloseModal() => showModal = false;
    }
}
