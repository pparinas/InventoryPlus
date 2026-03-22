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
                await Supabase.Auth.SignUp(Email.Trim(), Password);
                await Supabase.Auth.SignOut();
                SuccessMessage = "Registration successful! You may now log in.";
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

