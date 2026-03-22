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
        protected string SuccessMessage { get; set; } = string.Empty;
        protected bool IsLoading { get; set; } = false;

        protected async Task HandleRegister()
        {
            IsLoading = true;
            ErrorMessage = string.Empty;
            SuccessMessage = string.Empty;

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
                    return;
                }

                var session = await Supabase.Auth.SignUp(Email, Password);
                if (session?.User != null)
                {
                    // Save username + email to profiles table for username login lookups
                    if (Guid.TryParse(session.User.Id, out var userGuid))
                    {
                        var profile = new UserProfile
                        {
                            UserId = userGuid,
                            Username = username,
                            Email = Email.Trim().ToLower()
                        };
                        try { await Supabase.From<UserProfile>().Insert(profile); }
                        catch (Exception ex) { Console.WriteLine($"Profile insert error: {ex.Message}"); }
                    }
                    SuccessMessage = "Registration successful! You can now log in.";
                }
                else
                {
                    SuccessMessage = "Please check your email to confirm your registration.";
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

