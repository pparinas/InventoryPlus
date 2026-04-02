using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Authorization;
using Microsoft.JSInterop;
using InventoryPlus.Services;
using InventoryPlus.Models;

namespace InventoryPlus.Pages
{
    [Authorize(Roles = "Admin")]
    public partial class Admin : ComponentBase, IDisposable
    {
        [Inject] public SettingsService AppSettings { get; set; } = default!;
        [Inject] public UserManagementService UserManagement { get; set; } = default!;
        [Inject] public InviteTokenService InviteService { get; set; } = default!;
        [Inject] public NavigationManager NavManager { get; set; } = default!;
        [Inject] public Supabase.Client Supabase { get; set; } = default!;
        [Inject] public IJSRuntime JSRuntime { get; set; } = default!;

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
                    await LoadInviteHistory();
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
            _inviteTimer?.Dispose();
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

        // ── Invite Link ────────────────────────────────────────────────────

        protected bool isGeneratingInvite;
        protected string? generatedInviteUrl;
        protected string? inviteError;
        protected DateTime? inviteExpiresAt;
        protected TimeSpan? inviteTimeRemaining;
        protected bool inviteExpired;
        protected bool inviteCopied;
        protected int inviteExpiryHours = 24;
        private System.Threading.Timer? _inviteTimer;

        protected async Task GenerateInviteLink()
        {
            isGeneratingInvite = true;
            inviteError = null;
            generatedInviteUrl = null;
            inviteExpired = false;
            inviteCopied = false;

            try
            {
                var user = Supabase.Auth.CurrentUser;
                if (user == null || !Guid.TryParse(user.Id, out var adminId))
                {
                    inviteError = "Unable to identify current admin user.";
                    return;
                }

                var invite = await InviteService.GenerateInviteAsync(adminId, inviteExpiryHours);
                var baseUri = NavManager.BaseUri.TrimEnd('/');
                generatedInviteUrl = $"{baseUri}/register?token={invite.Token}";
                inviteExpiresAt = invite.ExpiresAt.ToLocalTime();
                UpdateInviteCountdown();

                _inviteTimer?.Dispose();
                _inviteTimer = new System.Threading.Timer(async _ =>
                {
                    UpdateInviteCountdown();
                    try { await InvokeAsync(StateHasChanged); } catch { }
                }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            }
            catch (Exception ex)
            {
                inviteError = $"Failed to generate invite: {ex.Message}";
            }
            finally
            {
                isGeneratingInvite = false;
            }
        }

        private void UpdateInviteCountdown()
        {
            if (inviteExpiresAt == null) return;
            var remaining = inviteExpiresAt.Value - DateTime.Now;
            if (remaining <= TimeSpan.Zero)
            {
                inviteExpired = true;
                inviteTimeRemaining = null;
                _inviteTimer?.Dispose();
                _inviteTimer = null;
            }
            else
            {
                inviteTimeRemaining = remaining;
            }
        }

        protected async Task CopyInviteLink()
        {
            if (string.IsNullOrEmpty(generatedInviteUrl)) return;
            try
            {
                await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", generatedInviteUrl);
                inviteCopied = true;
                StateHasChanged();
                await Task.Delay(2000);
                inviteCopied = false;
                StateHasChanged();
            }
            catch { }
        }

        // ── Invite History ─────────────────────────────────────────────────

        protected List<InviteToken> inviteHistory = new();
        protected bool isLoadingHistory;

        protected async Task LoadInviteHistory()
        {
            isLoadingHistory = true;
            StateHasChanged();
            try
            {
                inviteHistory = await InviteService.GetAllTokensAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LoadInviteHistory error: {ex.Message}");
            }
            isLoadingHistory = false;
            StateHasChanged();
        }
    }
}
