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

        [Column("pin_scope_flags")]
        public int PinScopeFlags { get; set; } = (int)PinScope.All;

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
