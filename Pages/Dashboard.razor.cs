using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Authorization;
using InventoryPlus.Components;
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

        private IEnumerable<Sale> FilteredSales
        {
            get
            {
                var now = DateTime.Now;
                // Free users are restricted to 30 days max
                var effectiveRange = !AppSettings.IsPro && timeRange == "all" ? "30d" : timeRange;
                return effectiveRange switch
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

        protected string Greeting
        {
            get
            {
                var h = DateTime.Now.Hour;
                return h < 12 ? "Good morning" : h < 18 ? "Good afternoon" : "Good evening";
            }
        }

        protected string TimeRangeLabel => timeRange switch
        {
            "today" => "Today",
            "7d" => "Last 7 days",
            "30d" => "Last 30 days",
            _ => "All time"
        };

        protected List<SegmentedControl.SegOption> TimeRangeOptions => new()
        {
            new("today", "1D"),
            new("7d", "7D"),
            new("30d", "30D"),
            new("all", AppSettings.IsPro ? "All" : "All 🔒", !AppSettings.IsPro),
        };

        protected int ActiveTransactionCount => ActiveFilteredSales.Count();

        protected double AvgSale => ActiveTransactionCount > 0 ? FilteredRevenue / ActiveTransactionCount : 0;

        // Revenue change vs the equivalent previous period; null when not comparable
        // (all-time range, or no revenue in the previous period).
        protected double? TrendPct
        {
            get
            {
                var now = DateTime.Now;
                (DateTime Start, DateTime End)? prev = timeRange switch
                {
                    "today" => (now.Date.AddDays(-1), now.Date.AddDays(-1).Add(now.TimeOfDay)),
                    "7d" => (now.AddDays(-14), now.AddDays(-7)),
                    "30d" => (now.AddDays(-60), now.AddDays(-30)),
                    _ => ((DateTime, DateTime)?)null
                };
                if (prev is not { } p) return null;
                var prevRevenue = Inventory.Sales
                    .Where(s => !s.IsVoided && s.Date >= p.Start && s.Date < p.End)
                    .Sum(s => s.TotalAmount);
                if (prevRevenue <= 0) return null;
                return (FilteredRevenue - prevRevenue) / prevRevenue * 100;
            }
        }

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
