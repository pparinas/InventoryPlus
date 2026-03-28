using System;
using System.Security.Cryptography;
using System.Text;
using InventoryPlus.Models;

namespace InventoryPlus.Services
{
    public class SettingsService
    {
        private readonly Supabase.Client _supabase;

        public SettingsService(Supabase.Client supabase)
        {
            _supabase = supabase;
        }

        private string _companyName = "InventoryPlus";
        public string CompanyName 
        { 
            get => _companyName; 
            set 
            {
                if (_companyName != value)
                {
                    _companyName = value;
                    NotifyStateChanged();
                }
            } 
        }

        private string? _customLogoUrl = null;
        private string? _customLogoPath = null;

        public string? CustomLogoUrl
        {
            get => _customLogoUrl;
            set
            {
                if (_customLogoUrl == value) return;
                _customLogoUrl = value;
                if (string.IsNullOrEmpty(value))
                    _customLogoPath = null;
                else if (!value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    _customLogoPath = value;
                else
                    _customLogoPath = ExtractStoragePath(value, "branding") ?? _customLogoPath;
                NotifyStateChanged();
            }
        }

        private bool _useLogoForBranding = false;
        public bool UseLogoForBranding
        {
            get => _useLogoForBranding;
            set
            {
                if (_useLogoForBranding != value)
                {
                    _useLogoForBranding = value;
                    NotifyStateChanged();
                }
            }
        }

        // Dashboard widget visibility
        public bool ShowRevenueChart { get; set; } = true;
        public bool ShowTopProducts { get; set; } = true;
        public bool ShowPaymentBreakdown { get; set; } = true;
        public bool ShowTodaySnapshot { get; set; } = true;
        public bool ShowProfitTrend { get; set; } = true;
        public bool ShowLowStock { get; set; } = true;
        public bool ShowRecentSales { get; set; } = true;
        public bool ShowQuickActions { get; set; } = true;

        // Report visibility
        public bool ShowReportSalesSummary { get; set; } = true;
        public bool ShowReportTopProducts { get; set; } = true;
        public bool ShowReportPaymentMethods { get; set; } = true;
        public bool ShowReportProfitLoss { get; set; } = true;
        public bool ShowReportInventoryValuation { get; set; } = true;
        public bool ShowReportStockMovement { get; set; } = true;

        // Onboarding
        public bool OnboardingCompleted { get; set; } = false;
        public bool ShowOnboardingOnLogin { get; set; } = true;

        // Guest mode
        public bool IsGuestMode { get; set; } = false;

        // PIN
        private string _pinHash = string.Empty;
        public string PinHash
        {
            get => _pinHash;
            set { _pinHash = value; }
        }

        public bool HasPin => !string.IsNullOrEmpty(_pinHash);

        public bool VerifyPin(string pin)
        {
            if (string.IsNullOrEmpty(_pinHash) || string.IsNullOrEmpty(pin)) return false;
            return _pinHash == HashPin(pin);
        }

        public void SetPin(string pin)
        {
            _pinHash = HashPin(pin);
            NotifyStateChanged();
        }

        private static string HashPin(string pin)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(pin));
            return Convert.ToHexStringLower(bytes);
        }

        // Inventory tab
        private bool _showInventoryTab = true;
        public bool ShowInventoryTab
        {
            get => _showInventoryTab;
            set
            {
                if (_showInventoryTab != value)
                {
                    _showInventoryTab = value;
                    NotifyStateChanged();
                }
            }
        }

        // Color Scheme
        private string _colorScheme = "indigo";
        public string ColorScheme
        {
            get => _colorScheme;
            set
            {
                if (_colorScheme != value)
                {
                    _colorScheme = value;
                    NotifyStateChanged();
                }
            }
        }

        // Currency display
        public bool ShowDecimals { get; set; } = true;
        public string FormatCurrency(double value) => ShowDecimals ? value.ToString("N2") : value.ToString("N0");
        public string FormatPrice(double value) => ShowDecimals ? value.ToString("0.00") : value.ToString("0");

        private static string? ExtractStoragePath(string? urlOrPath, string bucket)
        {
            if (string.IsNullOrEmpty(urlOrPath)) return null;
            if (!urlOrPath.StartsWith("https://")) return urlOrPath;
            foreach (var marker in new[] { $"/object/public/{bucket}/", $"/object/sign/{bucket}/" })
            {
                var idx = urlOrPath.IndexOf(marker, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    var p = urlOrPath.Substring(idx + marker.Length);
                    var q = p.IndexOf('?');
                    return q >= 0 ? p.Substring(0, q) : p;
                }
            }
            return null;
        }

        public async Task LoadAsync(string userId)
        {
            if (!Guid.TryParse(userId, out var ownerGuid)) return;
            try
            {
                var response = await _supabase.From<AccountSettings>()
                    .Where(s => s.OwnerGuid == ownerGuid)
                    .Get();
                var result = response.Models.FirstOrDefault();
                if (result != null)
                {
                    _companyName = result.CompanyName;
                    _useLogoForBranding = result.UseLogoForBranding;
                    _pinHash = result.PinHash ?? string.Empty;
                    _showInventoryTab = result.ShowInventoryTab;
                    _colorScheme = result.ColorScheme ?? "indigo";

                    // Dashboard settings
                    ShowRevenueChart = result.ShowRevenueChart;
                    ShowTopProducts = result.ShowTopProducts;
                    ShowPaymentBreakdown = result.ShowPaymentBreakdown;
                    ShowTodaySnapshot = result.ShowTodaySnapshot;
                    ShowProfitTrend = result.ShowProfitTrend;
                    ShowLowStock = result.ShowLowStock;
                    ShowRecentSales = result.ShowRecentSales;
                    ShowQuickActions = result.ShowQuickActions;

                    // Report settings
                    ShowReportSalesSummary = result.ShowReportSalesSummary;
                    ShowReportTopProducts = result.ShowReportTopProducts;
                    ShowReportPaymentMethods = result.ShowReportPaymentMethods;
                    ShowReportProfitLoss = result.ShowReportProfitLoss;
                    ShowReportInventoryValuation = result.ShowReportInventoryValuation;
                    ShowReportStockMovement = result.ShowReportStockMovement;

                    OnboardingCompleted = result.OnboardingCompleted;

                    var path = ExtractStoragePath(result.LogoUrl, "branding");
                    _customLogoPath = path;
                    if (!string.IsNullOrEmpty(path))
                    {
                        try
                        {
                            _customLogoUrl = await _supabase.Storage
                                .From("branding")
                                .CreateSignedUrl(path, 60 * 60 * 24 * 7);
                        }
                        catch
                        {
                            _customLogoUrl = null;
                        }
                    }
                    else
                    {
                        _customLogoUrl = null;
                    }

                    NotifyStateChanged();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Settings load error: {ex.Message}");
            }
        }

        public async Task SaveAsync(string userId)
        {
            if (!Guid.TryParse(userId, out var ownerGuid)) return;
            try
            {
                var settings = new AccountSettings
                {
                    OwnerGuid = ownerGuid,
                    CompanyName = _companyName,
                    LogoUrl = _customLogoPath ?? string.Empty,
                    UseLogoForBranding = _useLogoForBranding,
                    PinHash = _pinHash,
                    ShowInventoryTab = _showInventoryTab,
                    ColorScheme = _colorScheme,
                    ShowRevenueChart = ShowRevenueChart,
                    ShowTopProducts = ShowTopProducts,
                    ShowPaymentBreakdown = ShowPaymentBreakdown,
                    ShowTodaySnapshot = ShowTodaySnapshot,
                    ShowProfitTrend = ShowProfitTrend,
                    ShowLowStock = ShowLowStock,
                    ShowRecentSales = ShowRecentSales,
                    ShowQuickActions = ShowQuickActions,
                    ShowReportSalesSummary = ShowReportSalesSummary,
                    ShowReportTopProducts = ShowReportTopProducts,
                    ShowReportPaymentMethods = ShowReportPaymentMethods,
                    ShowReportProfitLoss = ShowReportProfitLoss,
                    ShowReportInventoryValuation = ShowReportInventoryValuation,
                    ShowReportStockMovement = ShowReportStockMovement,
                    OnboardingCompleted = OnboardingCompleted
                };
                await _supabase.From<AccountSettings>().Upsert(settings);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Settings save error: {ex.Message}");
            }
        }

        public event Action? OnStateChanged;
        
        private void NotifyStateChanged() => OnStateChanged?.Invoke();
    }
}
