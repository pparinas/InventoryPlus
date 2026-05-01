using Microsoft.AspNetCore.Components;
using InventoryPlus.Services;

namespace InventoryPlus.Pages
{
    public partial class Register : ComponentBase
    {
        [Inject] public Supabase.Client Supabase { get; set; } = default!;
        [Inject] public NavigationManager NavigationManager { get; set; } = default!;
        [Inject] public SettingsService AppSettings { get; set; } = default!;

        protected string Username { get; set; } = string.Empty;
        protected string Email { get; set; } = string.Empty;
        protected string Password { get; set; } = string.Empty;
        protected string ConfirmPassword { get; set; } = string.Empty;
        protected string ErrorMessage { get; set; } = string.Empty;
        protected string SuccessMessage { get; set; } = string.Empty;
        protected bool IsLoading { get; set; } = false;

        protected async Task HandleKeyDown(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e)
        {
            if (e.Key == "Enter" && !IsLoading)
                await HandleRegister();
        }

        protected async Task HandleRegister()
        {
            IsLoading = true;
            ErrorMessage = string.Empty;
            SuccessMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(Email) || !Email.Contains("@"))
            {
                ErrorMessage = "Please enter a valid email address.";
                IsLoading = false;
                return;
            }
            if (Password.Length < 6)
            {
                ErrorMessage = "Password must be at least 6 characters.";
                IsLoading = false;
                return;
            }
            if (Password != ConfirmPassword)
            {
                ErrorMessage = "Passwords do not match.";
                IsLoading = false;
                return;
            }

            try
            {
                var signUpOptions = new Supabase.Gotrue.SignUpOptions
                {
                    Data = new Dictionary<string, object>
                    {
                        { "username", string.IsNullOrWhiteSpace(Username) ? Email.Trim().Split('@')[0] : Username.Trim() }
                    }
                };
                await Supabase.Auth.SignUp(Email.Trim(), Password, signUpOptions);
                await Supabase.Auth.SignOut();

                NavigationManager.NavigateTo("/login?success=" + Uri.EscapeDataString("Account created! Check your email to confirm, then sign in."), forceLoad: false);
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

