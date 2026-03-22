using System;
using System.Collections.Generic;
using System.Linq;
using InventoryPlus.Models;

namespace InventoryPlus.Services
{
    public class UserManagementService
    {
        public List<SystemUser> Users { get; set; } = new();
        public event Action? OnStateChanged;

        public UserManagementService()
        {
            // Seed Data
            Users.AddRange(new[]
            {
                new SystemUser { Email = "admin@example.com", IsAdmin = true, IsActive = true },
                new SystemUser { Email = "user1@example.com", IsAdmin = false, IsActive = true, SubscriptionExpiresAt = DateTime.Now.AddMonths(1) },
                new SystemUser { Email = "user2@example.com", IsAdmin = false, IsActive = false },
                new SystemUser { Email = "pparinas@ucpm.com", IsAdmin = true, IsActive = true }
            });
        }

        public void AddUser(SystemUser user) { Users.Add(user); NotifyStateChanged(); }
        public void UpdateUser(SystemUser user) { NotifyStateChanged(); }
        public void DeleteUser(SystemUser user) { Users.Remove(user); NotifyStateChanged(); }

        public void NotifyStateChanged() => OnStateChanged?.Invoke();
    }
}
