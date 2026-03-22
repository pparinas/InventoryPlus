using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Authorization;
using InventoryPlus.Services;
using InventoryPlus.Models;

namespace InventoryPlus.Pages
{
    public partial class Login : ComponentBase
    {
        [Inject] public Supabase.Client Supabase { get; set; } = default!;
        [Inject] public NavigationManager NavigationManager { get; set; } = default!;
        [Inject] public SettingsService AppSettings { get; set; } = default!;

        protected string Email { get; set; } = string.Empty;
        protected string Password { get; set; } = string.Empty;
        protected string ErrorMessage { get; set; } = string.Empty;
        protected bool IsLoading { get; set; } = false;

        protected override void OnInitialized()
        {
            var uri = new Uri(NavigationManager.Uri);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            if (query["expired"] == "true")
                ErrorMessage = "Your session has expired. Please log in again.";
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
    }
}

