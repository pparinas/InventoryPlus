using System;
using System.Collections.Generic;
using System.Linq;
using InventoryPlus.Models;

namespace InventoryPlus.Services
{
    public class UserManagementService
    {
        private readonly Supabase.Client _supabase;

        public List<SystemUser> Users { get; private set; } = new();
        public event Action? OnStateChanged;

        public UserManagementService(Supabase.Client supabase)
        {
            _supabase = supabase;
        }

        public async Task LoadAsync()
        {
            var resp = await _supabase.From<UserProfile>().Get();
            Users = resp.Models
                .Select(p => new SystemUser
                {
                    Id = p.Guid,
                    Email = p.Email,
                    IsAdmin = p.IsAdmin,
                    IsActive = p.IsActive,
                    CreatedAt = p.CreatedAt,
                    SubscriptionExpiresAt = p.SubscriptionExpiry
                })
                .OrderBy(u => u.CreatedAt)
                .ToList();
            NotifyStateChanged();
        }

        public async Task UpdateUserAsync(SystemUser user)
        {
            await _supabase.From<UserProfile>()
                .Where(p => p.Guid == user.Id)
                .Set(p => p.IsActive, user.IsActive)
                .Set(p => p.IsAdmin, user.IsAdmin)
                .Set(p => (DateTime?)p.SubscriptionExpiry, user.SubscriptionExpiresAt)
                .Update();

            var existing = Users.FirstOrDefault(u => u.Id == user.Id);
            if (existing != null)
            {
                existing.IsActive = user.IsActive;
                existing.IsAdmin = user.IsAdmin;
                existing.SubscriptionExpiresAt = user.SubscriptionExpiresAt;
            }
            NotifyStateChanged();
        }

        public void NotifyStateChanged() => OnStateChanged?.Invoke();
    }
}
