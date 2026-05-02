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
        [Inject] public ToastService Toast { get; set; } = default!;
        [Inject] public IJSRuntime JS { get; set; } = default!;
        [Inject] public NavigationManager Nav { get; set; } = default!;

        protected string selectedFilter = "Daily";
        protected DateTime? stockDateFilter = null;
        protected HashSet<string> openSections = new() { "sales" };

        // Receipt
        protected bool showReceipt = false;
        protected Sale? receiptSale = null;

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

        protected void PrintSaleReceipt(Sale sale)
        {
            receiptSale = sale;
            showReceipt = true;
        }

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
                // Free users are restricted to 15 days max (Daily/Weekly only)
                var effectiveFilter = !AppSettings.IsPro && selectedFilter is "Monthly" or "Yearly" or "All"
                    ? "Weekly"
                    : selectedFilter;
                return effectiveFilter switch
                {
                    "Daily" => Inventory.Sales.Where(s => s.Date.Date == now.Date),
                    "Weekly" => Inventory.Sales.Where(s => s.Date >= now.AddDays(-15)),
                    "Monthly" => Inventory.Sales.Where(s => s.Date >= now.AddDays(-30)),
                    "Yearly" => Inventory.Sales.Where(s => s.Date >= now.AddDays(-365)),
                    _ => Inventory.Sales
                };
            }
        }

        // Active sales (non-voided) for computations
        protected IEnumerable<Sale> ActiveFilteredSales => FilteredSales.Where(s => !s.IsVoided);

        protected double FilteredRevenue => ActiveFilteredSales.Sum(s => s.TotalAmount);
        protected double FilteredProfit => ActiveFilteredSales.Sum(s => s.ProfitAmount);
        protected double FilteredTax => ActiveFilteredSales.Sum(s => s.TaxAmount);
        protected double FilteredCOGS => FilteredRevenue - FilteredTax - FilteredProfit;

        protected double AverageProfitMargin
        {
            get
            {
                var subtotal = ActiveFilteredSales.Sum(s => s.TotalAmount - s.TaxAmount);
                if (subtotal == 0) return 0;
                return (FilteredProfit / subtotal) * 100;
            }
        }

        // Top products report
        protected record TopProductInfo(string Name, int TotalQty, double TotalRevenue);
        protected List<TopProductInfo> TopProductsReport => ActiveFilteredSales
            .GroupBy(s => s.ProductName)
            .Select(g => new TopProductInfo(g.Key, g.Sum(s => s.QuantitySold), g.Sum(s => s.TotalAmount)))
            .OrderByDescending(x => x.TotalQty)
            .Take(10)
            .ToList();

        // Payment methods
        protected Dictionary<string, double> PaymentMethodsReport => ActiveFilteredSales
            .GroupBy(s => string.IsNullOrEmpty(s.PaymentMethod) ? "Cash" : s.PaymentMethod)
            .ToDictionary(g => g.Key, g => g.Sum(s => s.TotalAmount));

        // Stock movement
        protected record StockMovementInfo(string Name, int UnitsSold, double Revenue, double AvgPrice);
        protected List<StockMovementInfo> StockMovement => ActiveFilteredSales
            .GroupBy(s => s.ProductName)
            .Select(g =>
            {
                var qty = g.Sum(s => s.QuantitySold);
                var rev = g.Sum(s => s.TotalAmount);
                return new StockMovementInfo(g.Key, qty, rev, qty > 0 ? rev / qty : 0);
            })
            .OrderByDescending(x => x.UnitsSold)
            .ToList();

        // OPEX
        protected IEnumerable<Opex> FilteredOpex
        {
            get
            {
                var now = DateTime.Now;
                var items = Inventory.ActiveOpex;
                var effectiveFilter = !AppSettings.IsPro && selectedFilter is "Yearly" or "All"
                    ? "Monthly"
                    : selectedFilter;
                return effectiveFilter switch
                {
                    "Daily" => items.Where(o => o.Date.Date == now.Date),
                    "Weekly" => items.Where(o => o.Date >= now.AddDays(-7)),
                    "Monthly" => items.Where(o => o.Date >= now.AddDays(-30)),
                    "Yearly" => items.Where(o => o.Date >= now.AddDays(-365)),
                    _ => items
                };
            }
        }

        protected double FilteredOpexTotal => FilteredOpex.Sum(o => o.Amount);

        protected double NetProfitAfterOpex => AppSettings.ShowOpexTab
            ? FilteredProfit - FilteredOpexTotal
            : FilteredProfit;

        protected Dictionary<string, double> OpexByCategory => FilteredOpex
            .GroupBy(o => o.Category)
            .ToDictionary(g => g.Key, g => g.Sum(o => o.Amount));

        protected async Task ExportCsv()
        {
            if (!AppSettings.IsPro) return;
            var sb = new StringBuilder();
            sb.AppendLine("Date,Product,Qty,PaymentMethod,Amount,Tax,Profit");
            foreach (var s in FilteredSales.OrderByDescending(s => s.Date))
            {
                sb.AppendLine($"{s.Date:yyyy-MM-dd HH:mm},{EscapeCsv(s.ProductName)},{s.QuantitySold},{s.PaymentMethod},{s.TotalAmount:F2},{s.TaxAmount:F2},{s.ProfitAmount:F2}");
            }

            if (AppSettings.ShowOpexTab && FilteredOpex.Any())
            {
                sb.AppendLine();
                sb.AppendLine("OPEX Date,Name,Category,Amount,Note");
                foreach (var o in FilteredOpex.OrderByDescending(o => o.Date))
                {
                    sb.AppendLine($"{o.Date:yyyy-MM-dd},{EscapeCsv(o.Name)},{EscapeCsv(o.Category)},{o.Amount:F2},{EscapeCsv(o.Note)}");
                }
            }

            await JS.InvokeVoidAsync("downloadCsv", $"report_{selectedFilter}_{DateTime.Now:yyyyMMdd}.csv", sb.ToString());
        }

        private static string EscapeCsv(string val) =>
            val.Contains(',') || val.Contains('"') ? $"\"{val.Replace("\"", "\"\"")}\"" : val;

        // ── Edit / Void Sales ──────────────────────────────────────────────

        protected bool showPinPrompt;
        protected bool showEditSaleModal;
        protected bool showVoidConfirm;
        protected Sale? editingSale;
        protected Sale? voidingSale;
        protected int editQty;
        protected string editPaymentMethod = "Cash";
        protected string editDiscountType = "None";
        protected double editDiscountAmount;
        private Action? _pendingPinAction;

        protected void StartEditSale(Sale sale)
        {
            if (AppSettings.IsPinRequired(PinScope.Reports))
            {
                _pendingPinAction = () =>
                {
                    DoOpenEditSale(sale);
                    StateHasChanged();
                };
                showPinPrompt = true;
            }
            else
            {
                DoOpenEditSale(sale);
            }
        }

        private void DoOpenEditSale(Sale sale)
        {
            editingSale = sale;
            editQty = sale.QuantitySold;
            editPaymentMethod = sale.PaymentMethod;
            editDiscountType = sale.DiscountType;
            editDiscountAmount = sale.DiscountAmount;
            showEditSaleModal = true;
        }

        protected async Task SaveEditSale()
        {
            if (editingSale == null) return;

            editingSale.QuantitySold = editQty;
            editingSale.PaymentMethod = editPaymentMethod;
            editingSale.DiscountType = editDiscountType;
            editingSale.DiscountAmount = editDiscountAmount;

            try
            {
                await Inventory.UpdateSaleAsync(editingSale);
                Toast.Show("Sale updated successfully!");
            }
            catch (Exception ex)
            {
                Toast.Show($"Failed to update sale: {ex.Message}", "error");
            }

            showEditSaleModal = false;
            editingSale = null;
        }

        protected void StartVoidSale(Sale sale)
        {
            if (AppSettings.IsPinRequired(PinScope.Reports))
            {
                _pendingPinAction = () =>
                {
                    voidingSale = sale;
                    showVoidConfirm = true;
                    StateHasChanged();
                };
                showPinPrompt = true;
            }
            else
            {
                voidingSale = sale;
                showVoidConfirm = true;
            }
        }

        protected async Task ConfirmVoidSale()
        {
            if (voidingSale == null) return;

            try
            {
                await Inventory.VoidSaleAsync(voidingSale);
                Toast.Show("Sale voided. Stock restored.", "info");
            }
            catch (Exception ex)
            {
                Toast.Show($"Failed to void sale: {ex.Message}", "error");
            }

            showVoidConfirm = false;
            voidingSale = null;
        }

        protected void OnPinVerified()
        {
            _pendingPinAction?.Invoke();
            _pendingPinAction = null;
        }
    }
}
