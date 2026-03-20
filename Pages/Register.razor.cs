using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Authorization;
using InventoryPlus.Services;

namespace InventoryPlus.Pages
{
    public partial class Register : ComponentBase
    {
        [Inject] public Supabase.Client Supabase { get; set; } = default!;
        [Inject] public NavigationManager NavigationManager { get; set; } = default!;
        [Inject] public SettingsService AppSettings { get; set; } = default!;

        protected string Email { get; set; } = string.Empty;
        protected string Password { get; set; } = string.Empty;
        protected string ErrorMessage { get; set; } = string.Empty;
        protected string SuccessMessage { get; set; } = string.Empty;
        protected bool IsLoading { get; set; } = false;

        protected async Task HandleRegister()
        {
            IsLoading = true;
            ErrorMessage = string.Empty;
            SuccessMessage = string.Empty;

            try
            {
                var session = await Supabase.Auth.SignUp(Email, Password);
                if (session != null && session.User != null)
                {
                    SuccessMessage = "Registration successful! You can now log in.";
                }
                else
                {
                    SuccessMessage = "Please check your email to confirm registration.";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Registration failed: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
