using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Authorization;
using InventoryPlus.Services;
using InventoryPlus.Models;

namespace InventoryPlus.Pages
{
    [Authorize]
    public partial class Stocks : ComponentBase, IDisposable
    {
        [Inject] public InventoryService Inventory { get; set; } = default!;
        [Inject] public SettingsService AppSettings { get; set; } = default!;
        [Inject] public ToastService Toast { get; set; } = default!;
        protected bool showModal;
        protected bool isEditing;
        protected Ingredient currentIngredient = new();
        protected int _page = 1;
        protected const int PageSize = 10;

        protected string searchQuery = "";
        protected string typeFilter = "All";
        protected string sortBy = "name";
        protected bool showDeleteConfirm;
        protected Ingredient? deleteTarget;

        protected void SetPage(int p) { _page = p; StateHasChanged(); }

        protected IEnumerable<Ingredient> FilteredItems
        {
            get
            {
                var items = Inventory.ActiveIngredients.AsEnumerable();
                if (typeFilter != "All")
                    items = items.Where(i => i.Type == typeFilter);
                if (!string.IsNullOrWhiteSpace(searchQuery))
                    items = items.Where(i => i.Name.Contains(searchQuery, StringComparison.OrdinalIgnoreCase));
                return sortBy switch
                {
                    "stock" => items.OrderBy(i => i.Stock),
                    "value" => items.OrderByDescending(i => i.Stock * i.CostPerUnit),
                    _ => items.OrderBy(i => i.Name)
                };
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
            currentIngredient = new Ingredient { Type = "Ingredient" };
            isEditing = false;
            showModal = true;
        }

        protected void Edit(Ingredient item)
        {
            currentIngredient = new Ingredient
            {
                Guid = item.Guid,
                Name = item.Name,
                Unit = item.Unit,
                Stock = item.Stock,
                CostPerUnit = item.CostPerUnit,
                Type = item.Type
            };
            isEditing = true;
            showModal = true;
        }

        protected void ConfirmDelete(Ingredient item)
        {
            deleteTarget = item;
            showDeleteConfirm = true;
        }

        protected async Task ExecuteDelete()
        {
            if (deleteTarget != null)
            {
                await Inventory.DeleteIngredientAsync(deleteTarget);
                Toast.Show($"\"{deleteTarget.Name}\" removed.", "info");
            }
            showDeleteConfirm = false;
            deleteTarget = null;
        }

        protected async Task Save()
        {
            if (string.IsNullOrWhiteSpace(currentIngredient.Name)) return;

            if (isEditing)
            {
                var existing = Inventory.ActiveIngredients.FirstOrDefault(i => i.Guid == currentIngredient.Guid);
                if (existing != null)
                {
                    existing.Name = currentIngredient.Name;
                    existing.Unit = currentIngredient.Unit;
                    existing.Stock = currentIngredient.Stock;
                    existing.CostPerUnit = currentIngredient.CostPerUnit;
                    existing.Type = currentIngredient.Type;
                    await Inventory.UpdateIngredientAsync(existing);
                    Toast.Show($"\"{existing.Name}\" updated successfully!");
                }
            }
            else
            {
                currentIngredient.Guid = Guid.NewGuid();
                await Inventory.AddIngredientAsync(currentIngredient);
                Toast.Show($"\"{currentIngredient.Name}\" added to stock!");
            }
            CloseModal();
        }

        protected void CloseModal() => showModal = false;
    }
}
