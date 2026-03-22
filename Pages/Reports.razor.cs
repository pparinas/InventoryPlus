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
