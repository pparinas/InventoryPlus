using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Authorization;
using InventoryPlus.Services;
using InventoryPlus.Models;

namespace InventoryPlus.Pages
{
    [Authorize]
    public partial class Expenses : ComponentBase, IDisposable
    {
        [Inject] public InventoryService Inventory { get; set; } = default!;
        [Inject] public SettingsService AppSettings { get; set; } = default!;
        [Inject] public ToastService Toast { get; set; } = default!;
        [Inject] public NavigationManager Nav { get; set; } = default!;
        [Inject] public Microsoft.JSInterop.IJSRuntime JS { get; set; } = default!;

        protected string searchTerm = "";
        protected string categoryFilter = "All";
        protected int currentPage = 1;
        protected const int PageSize = 10;

        // Modal state
        protected bool showModal = false;
        protected bool isEditing = false;
        protected Opex editingItem = new();

        // PIN
        protected bool showPinPrompt = false;
        private Action? _pendingPinAction;

        // Delete confirm
        protected bool showDeleteConfirm = false;
        protected Opex? deletingItem;

        protected static readonly string[] Categories = new[]
        {
            "Rent", "Utilities", "Salaries", "Supplies", "Marketing",
            "Insurance", "Maintenance", "Transportation", "Taxes & Fees", "Other"
        };

        protected override void OnInitialized()
        {
            if (!AppSettings.ShowOpexTab) { Nav.NavigateTo("dashboard"); return; }
            Inventory.OnStateChanged += HandleStateChanged;
        }

        private void HandleStateChanged() => InvokeAsync(StateHasChanged);

        public void Dispose()
        {
            Inventory.OnStateChanged -= HandleStateChanged;
        }

        protected IEnumerable<Opex> FilteredItems
        {
            get
            {
                var items = Inventory.ActiveOpex;
                // Free users are restricted to 30 days of data
                if (!AppSettings.IsPro)
                    items = items.Where(o => o.Date >= DateTime.Now.AddDays(-30));
                if (!string.IsNullOrWhiteSpace(searchTerm))
                    items = items.Where(o => o.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
                        || o.Note.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
                if (categoryFilter != "All")
                    items = items.Where(o => o.Category == categoryFilter);
                return items.OrderByDescending(o => o.Date);
            }
        }

        protected double TotalExpenses => FilteredItems.Sum(o => o.Amount);

        protected void SetPage(int p) { currentPage = p; StateHasChanged(); }

        protected void OpenAddModal()
        {
            void DoOpen()
            {
                isEditing = false;
                editingItem = new Opex { Date = DateTime.Now };
                showModal = true;
                StateHasChanged();
            }

            if (AppSettings.IsPinRequired(PinScope.Expenses))
            {
                _pendingPinAction = DoOpen;
                showPinPrompt = true;
            }
            else
            {
                DoOpen();
            }
        }

        protected void OpenEditModal(Opex item)
        {
            void DoOpen()
            {
                isEditing = true;
                editingItem = new Opex
                {
                    Guid = item.Guid,
                    OwnerGuid = item.OwnerGuid,
                    Name = item.Name,
                    Category = item.Category,
                    Amount = item.Amount,
                    Date = item.Date,
                    Note = item.Note,
                    IsRecurring = item.IsRecurring,
                    Recurrence = item.Recurrence
                };
                showModal = true;
                StateHasChanged();
            }

            if (AppSettings.IsPinRequired(PinScope.Expenses))
            {
                _pendingPinAction = DoOpen;
                showPinPrompt = true;
            }
            else
            {
                DoOpen();
            }
        }

        protected async Task SaveExpense()
        {
            if (string.IsNullOrWhiteSpace(editingItem.Name))
            {
                Toast.Show("Please enter a name.", "error");
                return;
            }
            if (editingItem.Amount <= 0)
            {
                Toast.Show("Amount must be greater than zero.", "error");
                return;
            }

            try
            {
                if (isEditing)
                {
                    var existing = Inventory.OpexItems.FirstOrDefault(o => o.Guid == editingItem.Guid);
                    if (existing != null)
                    {
                        existing.Name = editingItem.Name;
                        existing.Category = editingItem.Category;
                        existing.Amount = editingItem.Amount;
                        existing.Date = editingItem.Date;
                        existing.Note = editingItem.Note;
                        existing.IsRecurring = editingItem.IsRecurring;
                        existing.Recurrence = editingItem.Recurrence;
                        await Inventory.UpdateOpexAsync(existing, JS);
                        Toast.Show("Expense updated!");
                    }
                }
                else
                {
                    await Inventory.AddOpexAsync(editingItem, JS);
                    Toast.Show("Expense added!");
                }
            }
            catch (Exception ex)
            {
                Toast.Show($"Error: {ex.Message}", "error");
            }

            showModal = false;
        }

        protected void StartDelete(Opex item)
        {
            void DoDelete()
            {
                deletingItem = item;
                showDeleteConfirm = true;
                StateHasChanged();
            }

            if (AppSettings.IsPinRequired(PinScope.Expenses))
            {
                _pendingPinAction = DoDelete;
                showPinPrompt = true;
            }
            else
            {
                DoDelete();
            }
        }

        protected async Task ConfirmDelete()
        {
            if (deletingItem == null) return;
            try
            {
                await Inventory.DeleteOpexAsync(deletingItem, JS);
                Toast.Show("Expense deleted.");
            }
            catch (Exception ex)
            {
                Toast.Show($"Error: {ex.Message}", "error");
            }
            showDeleteConfirm = false;
            deletingItem = null;
        }

        protected void OnPinVerified()
        {
            _pendingPinAction?.Invoke();
            _pendingPinAction = null;
        }
    }
}
