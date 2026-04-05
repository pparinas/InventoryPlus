using System;
using Newtonsoft.Json;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace InventoryPlus.Models
{
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

        // Dashboard widget visibility
        [Column("show_revenue_chart")]
        public bool ShowRevenueChart { get; set; } = true;
        [Column("show_top_products")]
        public bool ShowTopProducts { get; set; } = true;
        [Column("show_payment_breakdown")]
        public bool ShowPaymentBreakdown { get; set; } = true;
        [Column("show_today_snapshot")]
        public bool ShowTodaySnapshot { get; set; } = true;
        [Column("show_profit_trend")]
        public bool ShowProfitTrend { get; set; } = true;
        [Column("show_low_stock")]
        public bool ShowLowStock { get; set; } = true;
        [Column("show_recent_sales")]
        public bool ShowRecentSales { get; set; } = true;
        [Column("show_quick_actions")]
        public bool ShowQuickActions { get; set; } = true;

        // Report visibility
        [Column("show_report_sales_summary")]
        public bool ShowReportSalesSummary { get; set; } = true;
        [Column("show_report_top_products")]
        public bool ShowReportTopProducts { get; set; } = true;
        [Column("show_report_payment_methods")]
        public bool ShowReportPaymentMethods { get; set; } = true;
        [Column("show_report_profit_loss")]
        public bool ShowReportProfitLoss { get; set; } = true;
        [Column("show_report_inventory_valuation")]
        public bool ShowReportInventoryValuation { get; set; } = true;
        [Column("show_report_stock_movement")]
        public bool ShowReportStockMovement { get; set; } = true;

        // Onboarding
        [Column("onboarding_completed")]
        public bool OnboardingCompleted { get; set; } = false;

        // POS Mode
        [JsonIgnore] public bool IsPosMode { get; set; } = false;

        // Currency display
        [Column("show_decimals")]
        public bool ShowDecimals { get; set; } = true;
    }
}
