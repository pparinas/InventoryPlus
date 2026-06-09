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
        [Inject] public Microsoft.JSInterop.IJSRuntime JS { get; set; } = default!;
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
        protected bool showPinPrompt;
        private Action? _pendingPinAction;
        protected UnitCategory _selectedUnitCategory = UnitCategory.Weight;
        protected string _originalUnit = "";
        protected double? _itemCount;
        protected double? _costPerItem;

        protected void RecalculateStock()
        {
            if (currentIngredient.ItemSize is > 0 && _itemCount is > 0)
                currentIngredient.Stock = Math.Round(currentIngredient.ItemSize.Value * _itemCount.Value, 4);
        }

        protected void RecalculateCost()
        {
            if (currentIngredient.ItemSize is > 0 && _costPerItem is >= 0)
                currentIngredient.CostPerUnit = _costPerItem.Value / currentIngredient.ItemSize.Value;
        }

        protected void OnItemSizeChanged()
        {
            RecalculateStock();
            RecalculateCost();
        }

        protected void SyncCountFromStock()
        {
            if (currentIngredient.ItemSize is > 0 && currentIngredient.Stock > 0)
                _itemCount = Math.Round(currentIngredient.Stock / currentIngredient.ItemSize.Value, 2);
        }

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
            if (AppSettings.IsPinRequired(PinScope.Stocks))
            {
                _pendingPinAction = () =>
                {
                    currentIngredient = new Ingredient { Type = "Ingredient" };
                    isEditing = false;
                    _itemCount = null;
                    _costPerItem = null;
                    _selectedUnitCategory = UnitCategory.Weight;
                    currentIngredient.Unit = UnitConverter.GetUnitsForCategory(UnitCategory.Weight)[0];
                    showModal = true;
                    StateHasChanged();
                };
                showPinPrompt = true;
            }
            else
            {
                currentIngredient = new Ingredient { Type = "Ingredient" };
                isEditing = false;
                _itemCount = null;
                _costPerItem = null;
                _selectedUnitCategory = UnitCategory.Weight;
                currentIngredient.Unit = UnitConverter.GetUnitsForCategory(UnitCategory.Weight)[0];
                showModal = true;
            }
        }

        protected void Edit(Ingredient item)
        {
            if (AppSettings.IsPinRequired(PinScope.Stocks))
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

        private void DoEdit(Ingredient item)
        {
            currentIngredient = new Ingredient
            {
                Guid = item.Guid,
                Name = item.Name,
                Unit = item.Unit,
                Stock = item.Stock,
                CostPerUnit = item.CostPerUnit,
                Type = item.Type,
                LowStockThreshold = item.LowStockThreshold,
                ItemSize = item.ItemSize
            };
            isEditing = true;
            _originalUnit = item.Unit;
            _itemCount = null;
            SyncCountFromStock();
            _costPerItem = item.ItemSize is > 0
                ? Math.Round(item.CostPerUnit * item.ItemSize.Value, 2)
                : null;
            _selectedUnitCategory = UnitConverter.GetCategory(currentIngredient.Unit);
            showModal = true;
        }

        protected void OnPinVerified()
        {
            _pendingPinAction?.Invoke();
            _pendingPinAction = null;
        }

        protected void OnUnitCategoryChanged(ChangeEventArgs e)
        {
            if (Enum.TryParse<UnitCategory>(e.Value?.ToString(), out var cat))
            {
                _selectedUnitCategory = cat;
                var units = UnitConverter.GetUnitsForCategory(cat);
                currentIngredient.Unit = units.Length > 0 ? units[0] : "";
            }
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
                try
                {
                    await Inventory.DeleteIngredientAsync(deleteTarget, JS);
                    Toast.Show($"\"{deleteTarget.Name}\" removed.", "info");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Delete error: {ex.Message}");
                    Toast.Show("Failed to delete item.", "error");
                }
            }
            showDeleteConfirm = false;
            deleteTarget = null;
        }

        protected async Task Save()
        {
            if (string.IsNullOrWhiteSpace(currentIngredient.Name)) return;

            try
            {
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
                        existing.LowStockThreshold = currentIngredient.LowStockThreshold;
                        existing.ItemSize = currentIngredient.ItemSize;
                        await Inventory.UpdateIngredientAsync(existing, JS);
                        Toast.Show($"\"{existing.Name}\" updated successfully!");
                    }
                }
                else
                {
                    currentIngredient.Guid = Guid.NewGuid();
                    await Inventory.AddIngredientAsync(currentIngredient, JS);
                    Toast.Show($"\"{currentIngredient.Name}\" added to stock!");
                }
                CloseModal();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Save error: {ex.Message}");
                Toast.Show("Failed to save item.", "error");
            }
        }

        protected void CloseModal() => showModal = false;
    }
}
