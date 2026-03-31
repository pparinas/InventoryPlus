using System;
using Newtonsoft.Json;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace InventoryPlus.Models
{
    [Table("account_settings")]
    public class AccountSettings : BaseModel
    {
        [PrimaryKey("owner_guid", false)]
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

        // Dashboard widget visibility (in-memory only until DB columns are added)
        // To persist: add columns to Supabase, uncomment [Column], remove [JsonIgnore]
        [JsonIgnore] public bool ShowRevenueChart { get; set; } = true;
        [JsonIgnore] public bool ShowTopProducts { get; set; } = true;
        [JsonIgnore] public bool ShowPaymentBreakdown { get; set; } = true;
        [JsonIgnore] public bool ShowTodaySnapshot { get; set; } = true;
        [JsonIgnore] public bool ShowProfitTrend { get; set; } = true;
        [JsonIgnore] public bool ShowLowStock { get; set; } = true;
        [JsonIgnore] public bool ShowRecentSales { get; set; } = true;
        [JsonIgnore] public bool ShowQuickActions { get; set; } = true;

        // Report visibility (in-memory only until DB columns are added)
        [JsonIgnore] public bool ShowReportSalesSummary { get; set; } = true;
        [JsonIgnore] public bool ShowReportTopProducts { get; set; } = true;
        [JsonIgnore] public bool ShowReportPaymentMethods { get; set; } = true;
        [JsonIgnore] public bool ShowReportProfitLoss { get; set; } = true;
        [JsonIgnore] public bool ShowReportInventoryValuation { get; set; } = true;
        [JsonIgnore] public bool ShowReportStockMovement { get; set; } = true;

        // Onboarding (in-memory only until DB column is added)
        [JsonIgnore] public bool OnboardingCompleted { get; set; } = false;

        // POS Mode
        [JsonIgnore] public bool IsPosMode { get; set; } = false;

        // Currency display
        [JsonIgnore] public bool ShowDecimals { get; set; } = true;
    }
}
