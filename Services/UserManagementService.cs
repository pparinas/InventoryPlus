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
        }

        public void AddUser(SystemUser user) { Users.Add(user); NotifyStateChanged(); }
        public void UpdateUser(SystemUser user) { NotifyStateChanged(); }
        public void DeleteUser(SystemUser user) { Users.Remove(user); NotifyStateChanged(); }

        public void NotifyStateChanged() => OnStateChanged?.Invoke();
    }
}
