using System;
using Newtonsoft.Json;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace InventoryPlus.Models
{
    [Table("sales")]
    public class Sale : BaseModel
    {
        [PrimaryKey("guid", false)]
        public Guid Guid { get; set; } = Guid.NewGuid();

        [Column("owner_guid")]
        public Guid OwnerId { get; set; }

        [Column("product_id")]
        public Guid ProductId { get; set; }

        [Column("product_name")]
        public string ProductName { get; set; } = string.Empty;

        [Column("quantity_sold")]
        public int QuantitySold { get; set; }

        [Column("total_amount")]
        public double TotalAmount { get; set; }

        [Column("tax_amount")]
        public double TaxAmount { get; set; }

        [Column("profit_amount")]
        public double ProfitAmount { get; set; }

        [Column("date")]
        public DateTime Date { get; set; } = DateTime.Now;

        [Column("note")]
        public string Note { get; set; } = string.Empty;

        [Column("payment_method")]
        public string PaymentMethod { get; set; } = "Cash";

        [Column("is_voided")]
        public bool IsVoided { get; set; } = false;

        [Column("customer_name")]
        public string CustomerName { get; set; } = string.Empty;

        [Column("discount_amount")]
        public double DiscountAmount { get; set; }

        [Column("discount_type")]
        public string DiscountType { get; set; } = "None"; // "None", "Percentage", "Fixed"
    }
}
