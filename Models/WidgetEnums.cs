namespace InventoryPlus.Models
{
    [Flags]
    public enum DashboardWidgets
    {
        None             = 0,
        TodaySnapshot    = 1 << 0,
        RevenueChart     = 1 << 1,
        TopProducts      = 1 << 2,
        PaymentBreakdown = 1 << 3,
        ProfitTrend      = 1 << 4,
        LowStock         = 1 << 5,
        RecentSales      = 1 << 6,
        QuickActions     = 1 << 7,
        All              = TodaySnapshot | RevenueChart | TopProducts | PaymentBreakdown
                         | ProfitTrend | LowStock | RecentSales | QuickActions
    }

    [Flags]
    public enum ReportWidgets
    {
        None                = 0,
        SalesSummary        = 1 << 0,
        TopProducts         = 1 << 1,
        PaymentMethods      = 1 << 2,
        ProfitLoss          = 1 << 3,
        InventoryValuation  = 1 << 4,
        StockMovement       = 1 << 5,
        All                 = SalesSummary | TopProducts | PaymentMethods
                            | ProfitLoss | InventoryValuation | StockMovement
    }
}
