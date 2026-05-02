using System;

namespace InventoryPlus.Models
{
    public class SystemUser
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Email { get; set; } = string.Empty;
        public bool IsAdmin { get; set; } = false;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? SubscriptionExpiry { get; set; }

        public DateTime? LastSignInAt { get; set; }

        public bool IsPro =>
            SubscriptionExpiry.HasValue && SubscriptionExpiry.Value > DateTime.UtcNow;

        public bool IsOnline => LastSignInAt.HasValue && (DateTime.UtcNow - LastSignInAt.Value).TotalMinutes <= 10;
    }
}
