using System;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace InventoryPlus.Models
{
    [Table("user_profiles")]
    public class UserProfile : BaseModel
    {
        [PrimaryKey("guid", false)]
        public Guid Guid { get; set; }

        [Column("email")]
        public string Email { get; set; } = string.Empty;

        [Column("username")]
        public string? Username { get; set; }

        [Column("is_admin")]
        public bool IsAdmin { get; set; } = false;

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("trial_expires_at")]
        public DateTime? TrialExpiresAt { get; set; }

        [Column("subscription_expiry")]
        public DateTime? SubscriptionExpiry { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }
    }
}
