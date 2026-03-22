using Microsoft.AspNetCore.Components;
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

        protected string Email { get; set; } = string.Empty;
        protected string Password { get; set; } = string.Empty;
        protected string ErrorMessage { get; set; } = string.Empty;
        protected bool IsLoading { get; set; } = false;
        protected bool RememberMe { get; set; } = false;

        protected override async Task OnInitializedAsync()
        {
            var uri = new Uri(NavigationManager.Uri);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            if (query["expired"] == "true")
                ErrorMessage = "Your session has expired. Please log in again.";

            // Pre-fill remembered email/username
            try
            {
                var remembered = await JSRuntime.InvokeAsync<string?>("localStorage.getItem", "sb_remembered_login");
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
                string loginEmail = Email.Trim();

                // Username login: look up email from profiles table
                if (!loginEmail.Contains('@'))
                {
                    var profileResp = await Supabase.From<UserProfile>()
                        .Where(p => p.Username == loginEmail.ToLower())
                        .Get();
                    var profile = profileResp.Models.FirstOrDefault();
                    if (profile == null)
                    {
                        ErrorMessage = "No account found with that username.";
                        return;
                    }
                    loginEmail = profile.Email;
                }

                var session = await Supabase.Auth.SignIn(loginEmail, Password);
                if (session != null && session.User != null)
                {
                    // Handle Remember Me
                    if (RememberMe)
                        await JSRuntime.InvokeVoidAsync("localStorage.setItem", "sb_remembered_login", Email.Trim());
                    else
                        await JSRuntime.InvokeVoidAsync("localStorage.removeItem", "sb_remembered_login");

                    NavigationManager.NavigateTo("dashboard");
                }
                else
                {
                    ErrorMessage = "Invalid credentials. Please try again.";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message.Contains("Invalid") || ex.Message.Contains("credentials")
                    ? "Invalid email/username or password."
                    : $"Login failed: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}

