using System;
using Newtonsoft.Json;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace InventoryPlus.Models
{
    [Table("pilot_sessions")]
    public class PilotSession : BaseModel
    {
        [PrimaryKey("guid", false)]
        public Guid Guid { get; set; } = Guid.NewGuid();

        [Column("owner_guid")]
        public Guid OwnerGuid { get; set; }

        [Column("operator_name")]
        public string OperatorName { get; set; } = string.Empty;

        [Column("access_code")]
        public string AccessCode { get; set; } = string.Empty;

        [Column("started_at")]
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;

        [Column("ended_at")]
        public DateTime? EndedAt { get; set; }

        [Column("status")]
        public string Status { get; set; } = "active"; // "active", "ended"

        [Column("expires_at")]
        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(1);

        [JsonIgnore]
        public bool IsExpired => DateTime.UtcNow > ExpiresAt;

        [JsonIgnore]
        public bool IsActive => Status == "active" && !IsExpired;
    }

    [Table("pilot_activity_log")]
    public class PilotActivityLog : BaseModel
    {
        [PrimaryKey("guid", false)]
        public Guid Guid { get; set; } = Guid.NewGuid();

        [Column("session_guid")]
        public Guid SessionGuid { get; set; }

        [Column("owner_guid")]
        public Guid OwnerGuid { get; set; }

        [Column("operator_name")]
        public string OperatorName { get; set; } = string.Empty;

        [Column("action")]
        public string Action { get; set; } = string.Empty; // "sale", "void"

        [Column("entity_type")]
        public string EntityType { get; set; } = string.Empty; // "sale", "product"

        [Column("entity_guid")]
        public Guid EntityGuid { get; set; }

        [Column("detail")]
        public string Detail { get; set; } = string.Empty; // JSON string with extra info

        [Column("timestamp")]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
