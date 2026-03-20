using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Authorization;
using InventoryPlus.Services;
using InventoryPlus.Models;

namespace InventoryPlus.Pages
{
    [Authorize(Roles = "Admin")]
    public partial class Admin : ComponentBase, IDisposable
    {
        [Inject] public SettingsService AppSettings { get; set; } = default!;
        [Inject] public UserManagementService UserManagement { get; set; } = default!;
        
        protected List<SystemUser> Users => UserManagement.Users;
        protected bool showEditModal;
        protected SystemUser selectedUser = new();
        protected bool isSaving;

        protected override void OnInitialized()
        {
            UserManagement.OnStateChanged += HandleStateChanged;
        }

        private void HandleStateChanged() => StateHasChanged();

        public void Dispose()
        {
            UserManagement.OnStateChanged -= HandleStateChanged;
        }

        protected void EditUser(SystemUser user)
        {
            selectedUser = new SystemUser
            {
                Id = user.Id,
                Email = user.Email,
                Role = user.Role,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt,
                SubscriptionExpiresAt = user.SubscriptionExpiresAt
            };
            showEditModal = true;
        }

        protected void SaveUser()
        {
            isSaving = true;
            var original = Users.FirstOrDefault(u => u.Id == selectedUser.Id);
            if (original != null)
            {
                original.Email = selectedUser.Email;
                original.Role = selectedUser.Role;
                original.IsActive = selectedUser.IsActive;
                original.SubscriptionExpiresAt = selectedUser.SubscriptionExpiresAt;
                UserManagement.UpdateUser(original);
            }
            
            showEditModal = false;
            isSaving = false;
        }

        protected void CloseModal() => showEditModal = false;

        protected void ToggleUserStatus(SystemUser user)
        {
            user.IsActive = !user.IsActive;
            UserManagement.UpdateUser(user);
        }
    }
}
