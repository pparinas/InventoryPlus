using System;
using Newtonsoft.Json;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace InventoryPlus.Models
{
    [Table("opex")]
    public class Opex : BaseModel
    {
        [PrimaryKey("guid", true)]
        public Guid Guid { get; set; } = Guid.NewGuid();

        [Column("owner_guid")]
        public Guid OwnerGuid { get; set; }

        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("category")]
        public string Category { get; set; } = "Other";

        [Column("amount")]
        public double Amount { get; set; }

        [Column("date")]
        public DateTime Date { get; set; } = DateTime.Now;

        [Column("note")]
        public string Note { get; set; } = string.Empty;

        [Column("is_recurring")]
        public bool IsRecurring { get; set; } = false;

        [Column("recurrence")]
        public string Recurrence { get; set; } = "None"; // "None", "Daily", "Weekly", "Monthly", "Yearly"

        [Column("is_archived")]
        public bool IsArchived { get; set; } = false;
    }
}
