using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Authorization;
using InventoryPlus.Services;
using InventoryPlus.Models;

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
        protected string ErrorMessage { get; set; } = string.Empty;
        protected bool IsLoading { get; set; } = false;
        protected bool IsRegistered { get; set; } = false;
        protected int RedirectCountdown { get; set; } = 10;

        protected async Task HandleRegister()
        {
            IsLoading = true;
            ErrorMessage = string.Empty;

            var username = Username.Trim().ToLower();
            if (string.IsNullOrWhiteSpace(username))
            {
                ErrorMessage = "Username is required.";
                IsLoading = false;
                return;
            }
            if (username.Length < 3 || username.Length > 30)
            {
                ErrorMessage = "Username must be between 3 and 30 characters.";
                IsLoading = false;
                return;
            }
            if (!System.Text.RegularExpressions.Regex.IsMatch(username, @"^[a-z0-9_]+$"))
            {
                ErrorMessage = "Username can only contain letters, numbers, and underscores.";
                IsLoading = false;
                return;
            }

            try
            {
                // Check if username is already taken
                var existing = await Supabase.From<UserProfile>()
                    .Where(p => p.Username == username)
                    .Get();
                if (existing.Models.Any())
                {
                    ErrorMessage = "That username is already taken. Please choose another.";
                    IsLoading = false;
                    return;
                }

                // Pass username as metadata — the DB trigger reads it and stores it in user_profiles
                var signUpOptions = new Supabase.Gotrue.SignUpOptions
                {
                    Data = new Dictionary<string, object> { { "username", username } }
                };
                await Supabase.Auth.SignUp(Email, Password, signUpOptions);

                // Sign out immediately — email confirmation is disabled so Supabase
                // auto-signs in after SignUp, which would redirect to dashboard.
                await Supabase.Auth.SignOut();

                IsLoading = false;
                IsRegistered = true;
                StateHasChanged();

                // Countdown then redirect to login
                for (int i = 10; i > 0; i--)
                {
                    RedirectCountdown = i;
                    StateHasChanged();
                    await Task.Delay(1000);
                }
                NavigationManager.NavigateTo("login");
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Registration failed: {ex.Message}";
                IsLoading = false;
            }
        }
    }
}

