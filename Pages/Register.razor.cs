using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Authorization;
using InventoryPlus.Services;
using InventoryPlus.Models;

namespace InventoryPlus.Pages
{
    public partial class Register : ComponentBase, IDisposable
    {
        [Inject] public Supabase.Client Supabase { get; set; } = default!;
        [Inject] public NavigationManager NavigationManager { get; set; } = default!;
        [Inject] public SettingsService AppSettings { get; set; } = default!;
        [Inject] public InviteTokenService InviteService { get; set; } = default!;

        protected string Username { get; set; } = string.Empty;
        protected string Email { get; set; } = string.Empty;
        protected string Password { get; set; } = string.Empty;
        protected string ConfirmPassword { get; set; } = string.Empty;
        protected string ErrorMessage { get; set; } = string.Empty;
        protected string SuccessMessage { get; set; } = string.Empty;
        protected bool IsLoading { get; set; } = false;
        protected bool isValidating { get; set; } = true;
        protected bool isTokenValid { get; set; } = false;
        protected TimeSpan? tokenTimeRemaining;

        private string? _token;
        private InviteToken? _invite;
        private System.Threading.Timer? _countdownTimer;

        protected override async Task OnInitializedAsync()
        {
            var uri = new Uri(NavigationManager.Uri);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            _token = query["token"];

            if (string.IsNullOrEmpty(_token))
            {
                ErrorMessage = "No invite token provided. You need a valid registration link from an administrator.";
                isValidating = false;
                return;
            }

            _invite = await InviteService.ValidateTokenAsync(_token);
            if (_invite == null)
            {
                ErrorMessage = "This registration link is invalid, expired, or has already been used.";
                isValidating = false;
                return;
            }

            isTokenValid = true;
            isValidating = false;
            UpdateCountdown();

            _countdownTimer = new System.Threading.Timer(async _ =>
            {
                UpdateCountdown();
                if (tokenTimeRemaining == null)
                {
                    isTokenValid = false;
                    ErrorMessage = "This registration link has expired. Please request a new one from your administrator.";
                }
                try { await InvokeAsync(StateHasChanged); } catch { }
            }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        private void UpdateCountdown()
        {
            if (_invite == null) { tokenTimeRemaining = null; return; }
            var remaining = _invite.ExpiresAt - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                tokenTimeRemaining = null;
                _countdownTimer?.Dispose();
                _countdownTimer = null;
            }
            else
            {
                tokenTimeRemaining = remaining;
            }
        }

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

            // Re-validate token before registering
            if (string.IsNullOrEmpty(_token))
            {
                ErrorMessage = "No invite token.";
                IsLoading = false;
                return;
            }

            var freshCheck = await InviteService.ValidateTokenAsync(_token);
            if (freshCheck == null)
            {
                ErrorMessage = "This registration link has expired or has already been used.";
                isTokenValid = false;
                IsLoading = false;
                return;
            }

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

                // Mark token as used after successful registration
                await InviteService.MarkTokenUsedAsync(_token, Email.Trim());

                SuccessMessage = "Account created! Check your email to confirm, then sign in.";
                _countdownTimer?.Dispose();
                _countdownTimer = null;
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

        public void Dispose()
        {
            _countdownTimer?.Dispose();
        }
    }
}

