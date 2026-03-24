using System;
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
