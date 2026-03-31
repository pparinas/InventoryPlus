using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Authorization;
using InventoryPlus.Services;
using InventoryPlus.Models;
using InventoryPlus.Components;

namespace InventoryPlus.Pages
{
    [Authorize]
    public partial class Sales : ComponentBase, IDisposable
    {
        [Inject] public InventoryService Inventory { get; set; } = default!;
        [Inject] public SettingsService AppSettings { get; set; } = default!;
        [Inject] public ToastService Toast { get; set; } = default!;
        [Inject] public NavigationManager Nav { get; set; } = default!;

        protected string saleNote = "";
        protected string paymentMethod = "Cash";
        protected string customerName = "";
        protected string discountType = "None";
        protected double discountAmount = 0;
        protected string searchQuery = "";
        protected string selectedCategory = "All";
        protected bool showMobileCart = false;
        protected bool showReceipt = false;
        protected bool showClearConfirm = false;
        protected bool isProcessing = false;

        // Sales history
        protected bool showSalesHistory = false;
        protected string historyFilter = "Daily";
        protected int historyPage = 1;
        protected const int HistoryPageSize = 10;

        // POS mode exit PIN
        protected bool showExitPinPrompt = false;

        // POS mode edit/void
        protected bool showActionPinPrompt = false;
        protected bool showEditSaleModal = false;
        protected bool showVoidConfirm = false;
        protected bool showHistoryReceipt = false;
        protected Sale? editingSale;
        protected Sale? voidingSale;
        protected Sale? historyReceiptSale;
        protected int editQty;
        protected string editPaymentMethod = "Cash";
        protected string editDiscountType = "None";
        protected double editDiscountAmount;
        private Action? _pendingPinAction;

        // Receipt data
        protected List<Receipt.ReceiptItem>? lastSaleItems;
        protected DateTime lastSaleDate;
        protected string lastCustomerName = "";
        protected double lastSubtotal, lastTaxTotal, lastTotal, lastDiscountAmount;
        protected string lastPaymentMethod = "Cash";
        protected string lastDiscountType = "None";

        protected class CartItem
        {
            public Product Product { get; set; } = new();
            public int Quantity { get; set; }
        }

        protected List<CartItem> Cart = new();

        protected override void OnInitialized()
        {
            Inventory.OnStateChanged += HandleStateChanged;
            AppSettings.OnStateChanged += HandleStateChanged;
        }

        private void HandleStateChanged() => InvokeAsync(StateHasChanged);

        public void Dispose()
        {
            Inventory.OnStateChanged -= HandleStateChanged;
            AppSettings.OnStateChanged -= HandleStateChanged;
        }

        protected IEnumerable<string> Categories => Inventory.ActiveProducts
            .Select(p => string.IsNullOrEmpty(p.Category) ? "Other" : p.Category)
            .Distinct().OrderBy(c => c);

        protected IEnumerable<Product> FilteredProducts
        {
            get
            {
                var products = Inventory.ActiveProducts.AsEnumerable();
                if (selectedCategory != "All")
                    products = products.Where(p => (string.IsNullOrEmpty(p.Category) ? "Other" : p.Category) == selectedCategory);
                if (!string.IsNullOrWhiteSpace(searchQuery))
                    products = products.Where(p => p.Name.Contains(searchQuery, StringComparison.OrdinalIgnoreCase));
                return products;
            }
        }

        protected double Subtotal => Cart.Sum(c => c.Product.SellingPrice * c.Quantity);
        protected double TaxTotal => Cart.Sum(c => c.Product.SellingPrice * Math.Min(c.Product.TaxRate, 1.0) * c.Quantity);
        protected double DiscountDisplay
        {
            get
            {
                if (discountType == "Percentage" && discountAmount > 0)
                    return (Subtotal + TaxTotal) * (discountAmount / 100.0);
                if (discountType == "Fixed" && discountAmount > 0)
                    return discountAmount;
                return 0;
            }
        }
        protected double Total => Math.Max(0, Subtotal + TaxTotal - DiscountDisplay);

        protected void AddToCart(Product p)
        {
            var existing = Cart.FirstOrDefault(c => c.Product.Guid == p.Guid);
            if (existing != null)
            {
                if (existing.Quantity < p.AvailableCount)
                    existing.Quantity++;
                else
                    Toast.Show($"Max stock reached for {p.Name}", "info");
            }
            else
            {
                Cart.Add(new CartItem { Product = p, Quantity = 1 });
            }
        }

        protected void IncreaseQty(CartItem item)
        {
            if (item.Quantity < item.Product.AvailableCount)
                item.Quantity++;
            else
                Toast.Show($"Max stock reached for {item.Product.Name}", "info");
        }

        protected void DecreaseQty(CartItem item)
        {
            if (item.Quantity > 1) item.Quantity--;
            else Cart.Remove(item);
        }

        protected void SetQty(CartItem item, ChangeEventArgs e)
        {
            if (int.TryParse(e.Value?.ToString(), out var qty))
            {
                if (qty <= 0) Cart.Remove(item);
                else if (qty > item.Product.AvailableCount) item.Quantity = item.Product.AvailableCount;
                else item.Quantity = qty;
            }
        }

        protected void RemoveFromCart(CartItem item) => Cart.Remove(item);

        protected void ClearCart() => showClearConfirm = true;

        protected void ConfirmClear()
        {
            Cart.Clear();
            showClearConfirm = false;
        }

        protected async Task CompleteSale()
        {
            if (!Cart.Any() || isProcessing) return;
            isProcessing = true;

            try
            {
                // Build receipt items before sale modifies stock
                lastSaleItems = Cart.Select(c => new Receipt.ReceiptItem
                {
                    Name = c.Product.Name,
                    Quantity = c.Quantity,
                    UnitPrice = c.Product.SellingPrice
                }).ToList();
                lastSubtotal = Subtotal;
                lastTaxTotal = TaxTotal;
                lastTotal = Total;
                lastSaleDate = DateTime.Now;
                lastCustomerName = customerName;
                lastPaymentMethod = paymentMethod;
                lastDiscountAmount = discountAmount;
                lastDiscountType = discountType;

                var itemCount = Cart.Sum(c => c.Quantity);
                foreach (var item in Cart)
                {
                    await Inventory.RecordSaleAsync(item.Product, item.Quantity, saleNote, paymentMethod, customerName, discountAmount, discountType);
                }

                Toast.Show($"Sale completed — {itemCount} item(s) sold!", "success");

                Cart.Clear();
                saleNote = "";
                customerName = "";
                discountType = "None";
                discountAmount = 0;
                showMobileCart = false;
                showReceipt = true;
            }
            catch (Exception ex)
            {
                Toast.Show($"Sale failed: {ex.Message}", "error");
            }
            finally
            {
                isProcessing = false;
            }
        }

        // ── POS Mode: Today's stats ──
        protected IEnumerable<Sale> TodaySales => Inventory.Sales
            .Where(s => !s.IsVoided && s.Date.ToLocalTime().Date == DateTime.Today);

        protected double TodayRevenue => TodaySales.Sum(s => s.TotalAmount);
        protected double TodayProfit => TodaySales.Sum(s => s.ProfitAmount);
        protected int TodayTransactions => TodaySales.Count();
        protected int TodayItemsSold => TodaySales.Sum(s => s.QuantitySold);

        // ── POS Mode: Sales history ──
        protected IEnumerable<Sale> HistorySales
        {
            get
            {
                var now = DateTime.Now;
                return historyFilter switch
                {
                    "Daily" => Inventory.Sales.Where(s => s.Date.ToLocalTime().Date == now.Date),
                    "Weekly" => Inventory.Sales.Where(s => s.Date >= now.AddDays(-7)),
                    "Monthly" => Inventory.Sales.Where(s => s.Date >= now.AddDays(-30)),
                    _ => Inventory.Sales.Where(s => s.Date.ToLocalTime().Date == now.Date)
                };
            }
        }

        protected void SetHistoryPage(int p) { historyPage = p; }

        // ── POS Mode: PIN-protected exit ──
        protected void RequestExitPosMode()
        {
            if (AppSettings.HasPin)
            {
                showExitPinPrompt = true;
            }
            else
            {
                AppSettings.IsPosMode = false;
                Nav.NavigateTo("dashboard");
            }
        }

        protected void OnExitPinVerified()
        {
            showExitPinPrompt = false;
            AppSettings.IsPosMode = false;
            Nav.NavigateTo("dashboard");
        }

        // ── POS Mode: Edit / Void / Receipt ──
        protected void PrintHistoryReceipt(Sale sale)
        {
            historyReceiptSale = sale;
            showHistoryReceipt = true;
        }

        protected void StartEditSale(Sale sale)
        {
            if (AppSettings.HasPin)
            {
                _pendingPinAction = () =>
                {
                    DoOpenEditSale(sale);
                    InvokeAsync(StateHasChanged);
                };
                showActionPinPrompt = true;
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
            if (AppSettings.HasPin)
            {
                _pendingPinAction = () =>
                {
                    voidingSale = sale;
                    showVoidConfirm = true;
                    InvokeAsync(StateHasChanged);
                };
                showActionPinPrompt = true;
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

        protected void OnActionPinVerified()
        {
            _pendingPinAction?.Invoke();
            _pendingPinAction = null;
        }
    }
}
