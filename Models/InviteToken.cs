using System;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace InventoryPlus.Models
{
    [Table("invite_tokens")]
    public class InviteToken : BaseModel
    {
        [PrimaryKey("id", true)]
        [Column("id")]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Column("token")]
        public string Token { get; set; } = string.Empty;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("expires_at")]
        public DateTime ExpiresAt { get; set; }

        [Column("is_used")]
        public bool IsUsed { get; set; } = false;

        [Column("created_by")]
        public Guid CreatedBy { get; set; }

        [Column("used_by_email")]
        public string UsedByEmail { get; set; } = string.Empty;
    }
}
