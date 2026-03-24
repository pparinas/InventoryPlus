using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Authorization;
using Microsoft.JSInterop;
using InventoryPlus.Services;
using InventoryPlus.Models;
using System.Text;

namespace InventoryPlus.Pages
{
    [Authorize]
    public partial class Reports : ComponentBase, IDisposable
    {
        [Inject] public InventoryService Inventory { get; set; } = default!;
        [Inject] public SettingsService AppSettings { get; set; } = default!;
        [Inject] public IJSRuntime JS { get; set; } = default!;

        protected string selectedFilter = "All";
        protected DateTime? stockDateFilter = null;
        protected HashSet<string> openSections = new() { "sales" };

        // Pagination
        protected int salesPage = 1;
        protected int stockPage = 1;
        protected const int PageSize = 10;

        protected void SetSalesPage(int p) { salesPage = p; StateHasChanged(); }
        protected void SetStockPage(int p) { stockPage = p; StateHasChanged(); }
        protected void ToggleSection(string key)
        {
            if (!openSections.Remove(key)) openSections.Add(key);
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

        protected double InventoryValue => Inventory.ActiveIngredients.Sum(i => i.Stock * i.CostPerUnit);

        protected IEnumerable<Ingredient> FilteredStockItems
        {
            get
            {
                if (!stockDateFilter.HasValue) return Inventory.ActiveIngredients;
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
                    "Yearly" => Inventory.Sales.Where(s => s.Date >= now.AddDays(-365)),
                    _ => Inventory.Sales
                };
            }
        }

        protected double FilteredRevenue => FilteredSales.Sum(s => s.TotalAmount);
        protected double FilteredProfit => FilteredSales.Sum(s => s.ProfitAmount);
        protected double FilteredTax => FilteredSales.Sum(s => s.TaxAmount);
        protected double FilteredCOGS => FilteredRevenue - FilteredTax - FilteredProfit;

        protected double AverageProfitMargin
        {
            get
            {
                var subtotal = FilteredSales.Sum(s => s.TotalAmount - s.TaxAmount);
                if (subtotal == 0) return 0;
                return (FilteredProfit / subtotal) * 100;
            }
        }

        // Top products report
        protected record TopProductInfo(string Name, int TotalQty, double TotalRevenue);
        protected List<TopProductInfo> TopProductsReport => FilteredSales
            .GroupBy(s => s.ProductName)
            .Select(g => new TopProductInfo(g.Key, g.Sum(s => s.QuantitySold), g.Sum(s => s.TotalAmount)))
            .OrderByDescending(x => x.TotalQty)
            .Take(10)
            .ToList();

        // Payment methods
        protected Dictionary<string, double> PaymentMethodsReport => FilteredSales
            .GroupBy(s => string.IsNullOrEmpty(s.PaymentMethod) ? "Cash" : s.PaymentMethod)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.TotalAmount));

        // Stock movement
        protected record StockMovementInfo(string Name, int UnitsSold, double Revenue, double AvgPrice);
        protected List<StockMovementInfo> StockMovement => FilteredSales
            .GroupBy(s => s.ProductName)
            .Select(g =>
            {
                var qty = g.Sum(s => s.QuantitySold);
                var rev = g.Sum(s => s.TotalAmount);
                return new StockMovementInfo(g.Key, qty, rev, qty > 0 ? rev / qty : 0);
            })
            .OrderByDescending(x => x.UnitsSold)
            .ToList();

        protected async Task ExportCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Date,Product,Qty,PaymentMethod,Amount,Tax,Profit");
            foreach (var s in FilteredSales.OrderByDescending(s => s.Date))
            {
                sb.AppendLine($"{s.Date:yyyy-MM-dd HH:mm},{EscapeCsv(s.ProductName)},{s.QuantitySold},{s.PaymentMethod},{s.TotalAmount:F2},{s.TaxAmount:F2},{s.ProfitAmount:F2}");
            }
            await JS.InvokeVoidAsync("downloadCsv", $"report_{selectedFilter}_{DateTime.Now:yyyyMMdd}.csv", sb.ToString());
        }

        private static string EscapeCsv(string val) =>
            val.Contains(',') || val.Contains('"') ? $"\"{val.Replace("\"", "\"\"")}\"" : val;
    }
}
