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

        protected void Delete(Ingredient item)
        {
            Inventory.DeleteIngredient(item);
            Toast.Show($"\"{item.Name}\" removed.", "info");
        }

        protected void Save()
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
                    Inventory.UpdateIngredient(existing);
                    Toast.Show($"\"{existing.Name}\" updated successfully!");
                }
            }
            else
            {
                currentIngredient.Guid = Guid.NewGuid();
                Inventory.AddIngredient(currentIngredient);
                Toast.Show($"\"{currentIngredient.Name}\" added to stock!");
            }
            CloseModal();
        }

        protected void CloseModal() => showModal = false;
    }
}
