using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Authorization;
using InventoryPlus.Services;
using InventoryPlus.Models;

namespace InventoryPlus.Pages
{
    [Authorize]
    public partial class Reports : ComponentBase, IDisposable
    {
        [Inject] public InventoryService Inventory { get; set; } = default!;
        [Inject] public SettingsService AppSettings { get; set; } = default!;

        protected string selectedFilter = "All";
        protected DateTime? stockDateFilter = null;

        // Pagination
        protected int salesPage = 1;
        protected int stockPage = 1;
        protected const int PageSize = 10;

        protected void SetSalesPage(int p) { salesPage = p; StateHasChanged(); }
        protected void SetStockPage(int p) { stockPage = p; StateHasChanged(); }

        protected override void OnInitialized()
        {
            Inventory.OnStateChanged += HandleStateChanged;
        }

        private void HandleStateChanged() => StateHasChanged();

        public void Dispose()
        {
            Inventory.OnStateChanged -= HandleStateChanged;
        }

        protected double InventoryValue => Inventory.ActiveIngredients.Sum(i => i.Stock * i.CostPerUnit);

        protected IEnumerable<Ingredient> FilteredStockItems
        {
            get
            {
                if (!stockDateFilter.HasValue)
                    return Inventory.ActiveIngredients;
                return Inventory.ActiveIngredients.Where(i => i.CreatedAt.ToLocalTime().Date == stockDateFilter.Value.Date);
            }
        }
        
        protected IEnumerable<Sale> FilteredSales
        {
            get
            {
                var now = DateTime.Now;
                return selectedFilter switch
                {
                    "Daily" => Inventory.Sales.Where(s => s.Date.Date == now.Date),
                    "Weekly" => Inventory.Sales.Where(s => s.Date >= now.AddDays(-7)),
                    "Monthly" => Inventory.Sales.Where(s => s.Date >= now.AddDays(-30)),
                    "Yearly"  => Inventory.Sales.Where(s => s.Date >= now.AddDays(-365)),
                    _ => Inventory.Sales
                };
            }
        }

        protected double AverageProfitMargin 
        {
            get 
            {
                var sales = FilteredSales;
                var totalSales = sales.Sum(s => s.TotalAmount - s.TaxAmount); // Subtotal
                var totalProfit = sales.Sum(s => s.ProfitAmount);
                
                if (totalSales == 0) return 0;
                return (totalProfit / totalSales) * 100;
            }
        }
    }
}
