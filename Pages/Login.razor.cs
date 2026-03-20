using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Authorization;
using InventoryPlus.Services;

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

        protected async Task HandleLogin()
        {
            IsLoading = true;
            ErrorMessage = string.Empty;

            try
            {
                var session = await Supabase.Auth.SignIn(Email, Password);
                if (session != null && session.User != null)
                {
                    NavigationManager.NavigateTo("/dashboard");
                }
                else
                {
                    ErrorMessage = "Invalid credentials. Please try again.";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Login failed: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
