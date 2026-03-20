using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Authorization;
using InventoryPlus.Services;
using InventoryPlus.Models;

namespace InventoryPlus.Pages
{
    [Authorize]
    public partial class Dashboard : ComponentBase, IDisposable
    {
        [Inject] public InventoryService Inventory { get; set; } = default!;
        [Inject] public SettingsService AppSettings { get; set; } = default!;
        [Inject] public NavigationManager Nav { get; set; } = default!;

        protected override void OnInitialized()
        {
            Inventory.OnStateChanged += HandleStateChanged;
        }

        private void HandleStateChanged() => StateHasChanged();

        public void Dispose()
        {
            Inventory.OnStateChanged -= HandleStateChanged;
        }

        protected void Refresh() => StateHasChanged();

        protected double TotalRevenue => Inventory.Sales.Sum(s => s.TotalAmount);
        protected double TotalProfit => Inventory.Sales.Sum(s => s.ProfitAmount);
        
        protected List<Ingredient> LowStockIngredients => Inventory.Ingredients.Where(i => i.Stock < 5).ToList();
        protected List<Sale> RecentSales => Inventory.Sales.OrderByDescending(s => s.Date).Take(5).ToList();
    }
}
