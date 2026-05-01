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

        protected string timeRange = "30d";

        protected override void OnInitialized()
        {
            Inventory.OnStateChanged += HandleStateChanged;
        }

        private void HandleStateChanged() => InvokeAsync(StateHasChanged);

        public void Dispose()
        {
            Inventory.OnStateChanged -= HandleStateChanged;
        }

        protected void Refresh() => StateHasChanged();

        private IEnumerable<Sale> FilteredSales
        {
            get
            {
                var now = DateTime.Now;
                return timeRange switch
                {
                    "today" => Inventory.Sales.Where(s => s.Date.Date == now.Date),
                    "7d" => Inventory.Sales.Where(s => s.Date >= now.AddDays(-7)),
                    "30d" => Inventory.Sales.Where(s => s.Date >= now.AddDays(-30)),
                    _ => Inventory.Sales
                };
            }
        }

        private IEnumerable<Sale> ActiveFilteredSales => FilteredSales.Where(s => !s.IsVoided);
        protected double FilteredRevenue => ActiveFilteredSales.Sum(s => s.TotalAmount);
        protected double FilteredProfit => ActiveFilteredSales.Sum(s => s.ProfitAmount);

        protected List<Ingredient> LowStockIngredients => Inventory.ActiveIngredients
            .Where(i => i.Stock <= i.LowStockThreshold).ToList();

        protected List<Sale> RecentSales => FilteredSales
            .OrderByDescending(s => s.Date).Take(8).ToList();

        // Revenue per day for chart (last 7 entries)
        protected Dictionary<string, double> DailyRevenue => ActiveFilteredSales
            .GroupBy(s => s.Date.ToString("MM/dd"))
            .OrderBy(g => g.Key)
            .TakeLast(7)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.TotalAmount));

        // Profit per day for chart
        protected Dictionary<string, double> DailyProfit => ActiveFilteredSales
            .GroupBy(s => s.Date.ToString("MM/dd"))
            .OrderBy(g => g.Key)
            .TakeLast(7)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.ProfitAmount));

        // Top 5 products by quantity sold
        protected Dictionary<string, int> TopProducts => ActiveFilteredSales
            .GroupBy(s => s.ProductName)
            .Select(g => new { Name = g.Key, Qty = g.Sum(s => s.QuantitySold) })
            .OrderByDescending(x => x.Qty)
            .Take(5)
            .ToDictionary(x => x.Name, x => x.Qty);

        // Payment method breakdown
        protected Dictionary<string, double> PaymentBreakdown => ActiveFilteredSales
            .GroupBy(s => string.IsNullOrEmpty(s.PaymentMethod) ? "Cash" : s.PaymentMethod)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.TotalAmount));

        protected string GetPaymentIcon(string method) => method switch
        {
            "Cash" => "fa-solid fa-money-bill-wave",
            "GCash" => "fa-solid fa-mobile-screen-button",
            "Card" => "fa-solid fa-credit-card",
            "Bank Transfer" => "fa-solid fa-building-columns",
            _ => "fa-solid fa-circle-dollar-to-slot"
        };
    }
}
