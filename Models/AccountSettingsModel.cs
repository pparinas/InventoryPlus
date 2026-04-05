using System;
using Newtonsoft.Json;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace InventoryPlus.Models
{
    [Flags]
    public enum DashboardWidgets
    {
        None                = 0,
        TodaySnapshot       = 1 << 0,
        RevenueChart        = 1 << 1,
        TopProducts         = 1 << 2,
        PaymentBreakdown    = 1 << 3,
        ProfitTrend         = 1 << 4,
        LowStock            = 1 << 5,
        RecentSales         = 1 << 6,
        QuickActions        = 1 << 7,
        All                 = TodaySnapshot | RevenueChart | TopProducts | PaymentBreakdown
                            | ProfitTrend | LowStock | RecentSales | QuickActions
    }

    [Flags]
    public enum ReportWidgets
    {
        None                    = 0,
        SalesSummary            = 1 << 0,
        TopProducts             = 1 << 1,
        PaymentMethods          = 1 << 2,
        ProfitLoss              = 1 << 3,
        InventoryValuation      = 1 << 4,
        StockMovement           = 1 << 5,
        All                     = SalesSummary | TopProducts | PaymentMethods
                                | ProfitLoss | InventoryValuation | StockMovement
    }

    [Table("account_settings")]
    public class AccountSettings : BaseModel
    {
        [PrimaryKey("owner_guid", true)]
        public Guid OwnerGuid { get; set; }

        [Column("company_name")]
        public string CompanyName { get; set; } = "InventoryPlus";

        [Column("logo_url")]
        public string LogoUrl { get; set; } = string.Empty;

        [Column("use_logo_for_branding")]
        public bool UseLogoForBranding { get; set; } = false;

        [Column("pin_hash")]
        public string PinHash { get; set; } = string.Empty;

        [Column("show_inventory_tab")]
        public bool ShowInventoryTab { get; set; } = true;

        [Column("show_opex_tab")]
        public bool ShowOpexTab { get; set; } = false;

        [Column("color_scheme")]
        public string ColorScheme { get; set; } = "indigo";

        [Column("dashboard_widgets")]
        public int DashboardWidgetFlags { get; set; } = (int)DashboardWidgets.All;

        [Column("report_widgets")]
        public int ReportWidgetFlags { get; set; } = (int)ReportWidgets.All;

        [Column("onboarding_completed")]
        public bool OnboardingCompleted { get; set; } = false;

        [Column("show_onboarding_on_login")]
        public bool ShowOnboardingOnLogin { get; set; } = true;

        [Column("show_decimals")]
        public bool ShowDecimals { get; set; } = true;

        [JsonIgnore] public bool IsPosMode { get; set; } = false;
    }
}
