using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
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
        [Inject] public HttpClient Http { get; set; } = default!;
        [Inject] public IConfiguration Configuration { get; set; } = default!;

        protected List<SystemUser> Users => UserManagement.Users;
        protected bool showEditModal;
        protected SystemUser selectedUser = new();
        protected bool isSaving;
        protected bool isLoading;
        protected int _page = 1;
        protected const int PageSize = 10;
        protected void SetPage(int p) { _page = p; StateHasChanged(); }

        protected string userFilter = "All";

        protected IEnumerable<SystemUser> FilteredUsers => userFilter switch
        {
            "Active" => Users.Where(u => u.IsOnline),
            "7days"  => Users.Where(u => u.LastSignInAt.HasValue
                            && (DateTime.UtcNow - u.LastSignInAt.Value).TotalDays <= 7),
            _        => Users
        };

        protected void SetUserFilter(string filter)
        {
            userFilter = filter;
            _page = 1;
        }

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
                SubscriptionExpiry = user.SubscriptionExpiry
            };
            _selectedSubscriptionPlan = "";
            showEditModal = true;
        }

        protected async Task SaveUserAsync()
        {
            isSaving = true;
            await UserManagement.UpdateUserAsync(selectedUser);
            showEditModal = false;
            isSaving = false;
        }

        protected void CloseModal() => showEditModal = false;

        protected string _selectedSubscriptionPlan = "";
        protected void ApplySubscriptionPlan(string plan)
        {
            _selectedSubscriptionPlan = plan;
            switch (plan)
            {
                case "free":
                    selectedUser.SubscriptionExpiry = null;
                    break;
                case "1month":
                    selectedUser.SubscriptionExpiry = DateTime.UtcNow.AddMonths(1);
                    break;
                case "3month":
                    selectedUser.SubscriptionExpiry = DateTime.UtcNow.AddMonths(3);
                    break;
                case "6month":
                    selectedUser.SubscriptionExpiry = DateTime.UtcNow.AddMonths(6);
                    break;
                case "1year":
                    selectedUser.SubscriptionExpiry = DateTime.UtcNow.AddYears(1);
                    break;
            }
        }

        protected async Task ToggleUserStatusAsync(SystemUser user)
        {
            user.IsActive = !user.IsActive;
            await UserManagement.UpdateUserAsync(user);
        }

        // ── Login as user ──────────────────────────────────────────────────

        protected Guid? loginAsInFlightUserId;
        protected string? loginAsError;

        protected async Task LoginAsUserAsync(SystemUser user)
        {
            loginAsInFlightUserId = user.Id;
            loginAsError = null;
            try
            {
                var accessToken = Supabase.Auth.CurrentSession?.AccessToken;
                if (string.IsNullOrEmpty(accessToken))
                {
                    loginAsError = "Your session has expired. Please refresh and try again.";
                    return;
                }

                var supabaseUrl = Configuration["Supabase:Url"]!.TrimEnd('/');
                var redirectTo = $"{NavManager.BaseUri.TrimEnd('/')}/auth/callback";

                using var request = new HttpRequestMessage(HttpMethod.Post, $"{supabaseUrl}/functions/v1/login-as-user")
                {
                    Content = JsonContent.Create(new { targetUserId = user.Id, redirectTo })
                };
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

                var response = await Http.SendAsync(request);
                var body = await response.Content.ReadFromJsonAsync<LoginAsResponse>();

                if (!response.IsSuccessStatusCode || string.IsNullOrEmpty(body?.ActionLink))
                {
                    loginAsError = body?.Error ?? "Failed to generate a login link.";
                    return;
                }

                await JSRuntime.InvokeVoidAsync("open", body.ActionLink, "_blank");
            }
            catch (Exception ex)
            {
                loginAsError = $"Failed to generate a login link: {ex.Message}";
            }
            finally
            {
                loginAsInFlightUserId = null;
            }
        }

        private class LoginAsResponse
        {
            [System.Text.Json.Serialization.JsonPropertyName("actionLink")]
            public string? ActionLink { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("error")]
            public string? Error { get; set; }
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
