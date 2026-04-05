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

        private string _companyName = "Negosyo360";
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

        // Dashboard widget visibility (flags-backed)
        private DashboardWidgets _dashboardWidgets = DashboardWidgets.All;
        public DashboardWidgets DashboardWidgetFlags
        {
            get => _dashboardWidgets;
            set { _dashboardWidgets = value; NotifyStateChanged(); }
        }

        public bool ShowRevenueChart     { get => _dashboardWidgets.HasFlag(DashboardWidgets.RevenueChart);     set => SetFlag(ref _dashboardWidgets, DashboardWidgets.RevenueChart, value); }
        public bool ShowTopProducts      { get => _dashboardWidgets.HasFlag(DashboardWidgets.TopProducts);      set => SetFlag(ref _dashboardWidgets, DashboardWidgets.TopProducts, value); }
        public bool ShowPaymentBreakdown { get => _dashboardWidgets.HasFlag(DashboardWidgets.PaymentBreakdown); set => SetFlag(ref _dashboardWidgets, DashboardWidgets.PaymentBreakdown, value); }
        public bool ShowTodaySnapshot    { get => _dashboardWidgets.HasFlag(DashboardWidgets.TodaySnapshot);    set => SetFlag(ref _dashboardWidgets, DashboardWidgets.TodaySnapshot, value); }
        public bool ShowProfitTrend      { get => _dashboardWidgets.HasFlag(DashboardWidgets.ProfitTrend);      set => SetFlag(ref _dashboardWidgets, DashboardWidgets.ProfitTrend, value); }
        public bool ShowLowStock         { get => _dashboardWidgets.HasFlag(DashboardWidgets.LowStock);         set => SetFlag(ref _dashboardWidgets, DashboardWidgets.LowStock, value); }
        public bool ShowRecentSales      { get => _dashboardWidgets.HasFlag(DashboardWidgets.RecentSales);      set => SetFlag(ref _dashboardWidgets, DashboardWidgets.RecentSales, value); }
        public bool ShowQuickActions     { get => _dashboardWidgets.HasFlag(DashboardWidgets.QuickActions);     set => SetFlag(ref _dashboardWidgets, DashboardWidgets.QuickActions, value); }

        // Report visibility (flags-backed)
        private ReportWidgets _reportWidgets = ReportWidgets.All;
        public ReportWidgets ReportWidgetFlags
        {
            get => _reportWidgets;
            set { _reportWidgets = value; NotifyStateChanged(); }
        }

        public bool ShowReportSalesSummary        { get => _reportWidgets.HasFlag(ReportWidgets.SalesSummary);        set => SetFlag(ref _reportWidgets, ReportWidgets.SalesSummary, value); }
        public bool ShowReportTopProducts         { get => _reportWidgets.HasFlag(ReportWidgets.TopProducts);         set => SetFlag(ref _reportWidgets, ReportWidgets.TopProducts, value); }
        public bool ShowReportPaymentMethods      { get => _reportWidgets.HasFlag(ReportWidgets.PaymentMethods);      set => SetFlag(ref _reportWidgets, ReportWidgets.PaymentMethods, value); }
        public bool ShowReportProfitLoss          { get => _reportWidgets.HasFlag(ReportWidgets.ProfitLoss);          set => SetFlag(ref _reportWidgets, ReportWidgets.ProfitLoss, value); }
        public bool ShowReportInventoryValuation  { get => _reportWidgets.HasFlag(ReportWidgets.InventoryValuation);  set => SetFlag(ref _reportWidgets, ReportWidgets.InventoryValuation, value); }
        public bool ShowReportStockMovement       { get => _reportWidgets.HasFlag(ReportWidgets.StockMovement);       set => SetFlag(ref _reportWidgets, ReportWidgets.StockMovement, value); }

        private void SetFlag<T>(ref T flags, T flag, bool on) where T : struct, Enum
        {
            var f = (int)(object)flags;
            var b = (int)(object)flag;
            flags = (T)(object)(on ? f | b : f & ~b);
            NotifyStateChanged();
        }

        // Onboarding
        public bool OnboardingCompleted { get; set; } = false;
        public bool ShowOnboardingOnLogin { get; set; } = true;

        // Guest mode
        public bool IsGuestMode { get; set; } = false;

        // POS Mode
        private bool _isPosMode = false;
        public bool IsPosMode
        {
            get => _isPosMode;
            set
            {
                if (_isPosMode != value)
                {
                    _isPosMode = value;
                    if (!value) PosActiveView = "pos";
                    NotifyStateChanged();
                }
            }
        }

        // POS active view: "pos" or "history"
        private string _posActiveView = "pos";
        public string PosActiveView
        {
            get => _posActiveView;
            set
            {
                if (_posActiveView != value)
                {
                    _posActiveView = value;
                    NotifyStateChanged();
                }
            }
        }

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

        // OPEX tab
        private bool _showOpexTab = false;
        public bool ShowOpexTab
        {
            get => _showOpexTab;
            set
            {
                if (_showOpexTab != value)
                {
                    _showOpexTab = value;
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

        public bool IsLoaded { get; private set; } = false;

        // Cache signed logo URL to avoid re-signing on every load
        private string? _cachedLogoPath;
        private string? _cachedLogoSignedUrl;
        private DateTime _logoUrlExpiry = DateTime.MinValue;

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
                    _showOpexTab = result.ShowOpexTab;
                    _colorScheme = result.ColorScheme ?? "indigo";

                    _dashboardWidgets = (DashboardWidgets)result.DashboardWidgetFlags;
                    _reportWidgets = (ReportWidgets)result.ReportWidgetFlags;

                    OnboardingCompleted = result.OnboardingCompleted;
                    ShowDecimals = result.ShowDecimals;

                    var path = ExtractStoragePath(result.LogoUrl, "branding");
                    _customLogoPath = path;
                    if (!string.IsNullOrEmpty(path))
                    {
                        // Try public URL first (no expiry, no extra round trip)
                        // Requires the 'branding' bucket to be set to Public in Supabase Storage
                        try
                        {
                            var publicUrl = _supabase.Storage.From("branding").GetPublicUrl(path);
                            if (!string.IsNullOrEmpty(publicUrl))
                            {
                                _customLogoUrl = publicUrl;
                                _cachedLogoPath = path;
                                _cachedLogoSignedUrl = publicUrl;
                                _logoUrlExpiry = DateTime.MaxValue; // public URLs never expire
                            }
                            else
                            {
                                throw new Exception("Empty public URL");
                            }
                        }
                        catch
                        {
                            // Bucket is private — fall back to signed URL with long TTL
                            try
                            {
                                if (path == _cachedLogoPath && _cachedLogoSignedUrl != null && _logoUrlExpiry > DateTime.UtcNow)
                                {
                                    _customLogoUrl = _cachedLogoSignedUrl;
                                }
                                else
                                {
                                    _customLogoUrl = await _supabase.Storage
                                        .From("branding")
                                        .CreateSignedUrl(path, 60 * 60 * 24 * 365); // 1-year TTL
                                    _cachedLogoPath = path;
                                    _cachedLogoSignedUrl = _customLogoUrl;
                                    _logoUrlExpiry = DateTime.UtcNow.AddDays(364);
                                }
                            }
                            catch
                            {
                                // Keep last known URL if we have one, rather than blanking it
                                if (_cachedLogoSignedUrl != null)
                                    _customLogoUrl = _cachedLogoSignedUrl;
                                else
                                    _customLogoUrl = null;
                            }
                        }
                    }
                    else
                    {
                        _customLogoUrl = null;
                    }

                    IsLoaded = true;
                    NotifyStateChanged();
                }
                else
                {
                    // No row exists yet — mark as loaded with defaults
                    IsLoaded = true;
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
                    ShowOpexTab = _showOpexTab,
                    ColorScheme = _colorScheme,
                    DashboardWidgetFlags = (int)_dashboardWidgets,
                    ReportWidgetFlags = (int)_reportWidgets,
                    OnboardingCompleted = OnboardingCompleted
                };
                var response = await _supabase.From<AccountSettings>().Upsert(settings);
                if (response.Models.Count == 0)
                    throw new Exception("Upsert returned no rows — check RLS policies on account_settings.");
                IsLoaded = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Settings save error: {ex.Message}");
                throw;
            }
        }

        public event Action? OnStateChanged;
        
        private void NotifyStateChanged() => OnStateChanged?.Invoke();
    }
}
