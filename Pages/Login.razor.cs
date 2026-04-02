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
        [Inject] public IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] public AuthenticationStateProvider AuthStateProvider { get; set; } = default!;

        protected string Email { get; set; } = string.Empty;
        protected string Password { get; set; } = string.Empty;
        protected string ErrorMessage { get; set; } = string.Empty;
        protected bool IsLoading { get; set; } = false;
        protected bool RememberMe { get; set; } = false;

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
                var loginInput = Email.Trim();

                // If input doesn't look like an email, resolve username to email
                if (!loginInput.Contains('@'))
                {
                    try
                    {
                        var result = await Supabase.Rpc("get_email_by_username", new Dictionary<string, object> { { "username_input", loginInput } });
                        var resolvedEmail = result?.Content?.Trim('"');
                        if (!string.IsNullOrEmpty(resolvedEmail) && resolvedEmail != "null")
                            loginInput = resolvedEmail;
                        else
                        {
                            ErrorMessage = "No account found with that username.";
                            return;
                        }
                    }
                    catch
                    {
                        ErrorMessage = "Unable to look up username. Try using your email instead.";
                        return;
                    }
                }

                var session = await Supabase.Auth.SignIn(loginInput, Password);
                if (session != null && session.User != null)
                {
                    // Save/clear remembered login
                    if (RememberMe)
                        await JSRuntime.InvokeVoidAsync("localStorage.setItem", "sb_remembered_email", loginInput);
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
                            // Admins bypass trial/subscription checks
                            if (!profile.IsAdmin)
                            {
                                var hasActiveTrial = profile.TrialExpiresAt.HasValue && profile.TrialExpiresAt.Value.ToUniversalTime() >= DateTime.UtcNow;
                                var hasActiveSubscription = profile.SubscriptionExpiry.HasValue && profile.SubscriptionExpiry.Value.ToUniversalTime() >= DateTime.UtcNow;

                                if (!hasActiveTrial && !hasActiveSubscription)
                                {
                                    await Supabase.Auth.SignOut();
                                    ErrorMessage = profile.TrialExpiresAt.HasValue && !profile.SubscriptionExpiry.HasValue
                                        ? "Your 7-day free trial has expired. Please contact an administrator to subscribe."
                                        : "Your subscription has expired. Please contact an administrator to renew your plan.";
                                    return;
                                }
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
    }
}

