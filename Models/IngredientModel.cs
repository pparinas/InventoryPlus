using System;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace InventoryPlus.Models
{
    [Table("ingredients")]
    public class Ingredient : BaseModel
    {
        [PrimaryKey("guid", true)]
        public Guid Guid { get; set; } = Guid.NewGuid();

        [Column("owner_guid")]
        public Guid OwnerGuid { get; set; }

        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("unit")]
        public string Unit { get; set; } = string.Empty;

        [Column("stock")]
        public double Stock { get; set; }

        [Column("cost_per_unit")]
        public double CostPerUnit { get; set; }

        [Column("type")]
        public string Type { get; set; } = "Ingredient"; // "Ingredient" or "Necessity"

        [Column("is_archived")]
        public bool IsArchived { get; set; } = false;

        [Column("low_stock_threshold")]
        public double LowStockThreshold { get; set; } = 5.0;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
