using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace InventoryPlus.Models
{
    [Table("orders")]
    public class Order : BaseModel
    {
        [PrimaryKey("guid", true)]
        public Guid Guid { get; set; } = Guid.NewGuid();

        [Column("owner_guid")]
        public Guid OwnerGuid { get; set; }

        [Column("order_number")]
        public int OrderNumber { get; set; }

        [Column("customer_name")]
        public string CustomerName { get; set; } = string.Empty;

        [Column("table_note")]
        public string TableNote { get; set; } = string.Empty;

        [Column("status")]
        public string Status { get; set; } = "pending"; // "pending" | "completed" | "cancelled"

        [Column("total_amount")]
        public double TotalAmount { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Reference(typeof(OrderItem), includeInQuery: false)]
        public List<OrderItem> Items { get; set; } = new();

        [JsonIgnore]
        public double RecomputedTotal => Items.Sum(i => i.UnitPrice * i.Quantity);
    }
}
