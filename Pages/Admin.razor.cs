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
        protected bool isLoading;
        protected string selectedPlan = "1month";
        protected int _page = 1;
        protected const int PageSize = 10;
        protected void SetPage(int p) { _page = p; StateHasChanged(); }

        protected override void OnInitialized()
        {
            UserManagement.OnStateChanged += HandleStateChanged;
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                isLoading = true;
                StateHasChanged();
                try
                {
                    await UserManagement.LoadAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Admin load error: {ex.Message}");
                }
                isLoading = false;
                StateHasChanged();
            }
        }

        private void HandleStateChanged() => InvokeAsync(StateHasChanged);

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
                IsAdmin = user.IsAdmin,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt,
                SubscriptionExpiresAt = user.SubscriptionExpiresAt
            };
            selectedPlan = "1month";
            showEditModal = true;
        }

        protected void ApplyPlan()
        {
            var from = selectedUser.SubscriptionExpiresAt.HasValue && selectedUser.SubscriptionExpiresAt.Value > DateTime.UtcNow
                ? selectedUser.SubscriptionExpiresAt.Value
                : DateTime.UtcNow;

            selectedUser.SubscriptionExpiresAt = selectedPlan == "1year"
                ? from.AddYears(1)
                : from.AddMonths(1);
            selectedUser.IsActive = true;
        }

        protected async Task SaveUserAsync()
        {
            isSaving = true;
            await UserManagement.UpdateUserAsync(selectedUser);
            showEditModal = false;
            isSaving = false;
        }

        protected void CloseModal() => showEditModal = false;

        protected async Task ToggleUserStatusAsync(SystemUser user)
        {
            user.IsActive = !user.IsActive;
            await UserManagement.UpdateUserAsync(user);
        }
    }
}
