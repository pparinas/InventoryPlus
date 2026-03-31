using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.JSInterop;
using InventoryPlus.Services;
using InventoryPlus.Models;

namespace InventoryPlus.Pages
{
    public partial class Login : ComponentBase
    {
        [Inject] public Supabase.Client Supabase { get; set; } = default!;
        [Inject] public NavigationManager NavigationManager { get; set; } = default!;
        [Inject] public SettingsService AppSettings { get; set; } = default!;
        [Inject] public InventoryService Inventory { get; set; } = default!;
        [Inject] public IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] public AuthenticationStateProvider AuthStateProvider { get; set; } = default!;
        [Inject] public PilotService PilotService { get; set; } = default!;

        protected string Email { get; set; } = string.Empty;
        protected string Password { get; set; } = string.Empty;
        protected string ErrorMessage { get; set; } = string.Empty;
        protected bool IsLoading { get; set; } = false;
        protected bool RememberMe { get; set; } = false;

        // Pilot mode
        protected bool showPilotJoin = false;
        protected string pilotCode = "";
        protected string pilotName = "";
        protected string pilotError = "";
        protected bool IsPilotJoining = false;

        protected async Task HandleKeyDown(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e)
        {
            if (e.Key == "Enter" && !IsLoading)
                await HandleLogin();
        }

        protected override async Task OnInitializedAsync()
        {
            var uri = new Uri(NavigationManager.Uri);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            if (query["expired"] == "true")
                ErrorMessage = "Your session has expired. Please log in again.";

            try
            {
                var remembered = await JSRuntime.InvokeAsync<string?>("localStorage.getItem", "sb_remembered_email");
                if (!string.IsNullOrEmpty(remembered))
                {
                    Email = remembered;
                    RememberMe = true;
                }
            }
            catch { }
        }

        protected async Task HandleLogin()
        {
            IsLoading = true;
            ErrorMessage = string.Empty;

            try
            {
                var session = await Supabase.Auth.SignIn(Email.Trim(), Password);
                if (session != null && session.User != null)
                {
                    // Save/clear remembered email
                    if (RememberMe)
                        await JSRuntime.InvokeVoidAsync("localStorage.setItem", "sb_remembered_email", Email.Trim());
                    else
                        await JSRuntime.InvokeVoidAsync("localStorage.removeItem", "sb_remembered_email");

                    // Check subscription status
                    if (Guid.TryParse(session.User.Id, out var userId))
                    {
                        var profileResp = await Supabase.From<UserProfile>()
                            .Where(p => p.Guid == userId).Get();
                        var profile = profileResp.Models.FirstOrDefault();
                        if (profile != null)
                        {
                            if (!profile.IsActive)
                            {
                                await Supabase.Auth.SignOut();
                                ErrorMessage = "Your account has been deactivated. Please contact an administrator.";
                                return;
                            }
                            if (profile.SubscriptionExpiry.HasValue && profile.SubscriptionExpiry.Value.ToUniversalTime() < DateTime.UtcNow)
                            {
                                await Supabase.Auth.SignOut();
                                ErrorMessage = "Your subscription has expired. Please contact an administrator to renew your plan.";
                                return;
                            }
                        }
                    }
                    NavigationManager.NavigateTo("dashboard");
                }
                else
                {
                    ErrorMessage = "Invalid email or password.";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message.Contains("Invalid") || ex.Message.Contains("credentials")
                    ? "Invalid email or password."
                    : $"Login failed: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        protected async Task HandleGuestMode()
        {
            AppSettings.IsGuestMode = true;
            var authProvider = (SupabaseAuthenticationStateProvider)AuthStateProvider;
            await authProvider.EnableGuestModeAsync();
            NavigationManager.NavigateTo("dashboard");
        }

        protected async Task HandlePilotJoin()
        {
            pilotError = "";
            IsPilotJoining = true;

            try
            {
                var (success, error) = await PilotService.JoinSessionAsync(pilotCode.Trim(), pilotName.Trim());
                if (success)
                {
                    // Load the owner's data for the operator to use
                    var ownerGuid = PilotService.OwnerGuid;
                    await AppSettings.LoadAsync(ownerGuid.ToString());
                    await Inventory.LoadAsync(ownerGuid.ToString());
                    Inventory.SetPilotService(PilotService);

                    NavigationManager.NavigateTo("sales");
                }
                else
                {
                    pilotError = error;
                }
            }
            catch (Exception ex)
            {
                pilotError = $"Connection failed: {ex.Message}";
            }
            finally
            {
                IsPilotJoining = false;
            }
        }
    }
}

