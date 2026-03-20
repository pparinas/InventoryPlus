using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Authorization;
using Supabase;
using Supabase.Interfaces;
using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;
using InventoryPlus.Services;
using User = Supabase.Gotrue.User;
using Session = Supabase.Gotrue.Session;

namespace InventoryPlus.Pages
{
    [Authorize(Roles = "Admin")]
    public partial class Settings : ComponentBase, IDisposable
    {
        [Inject] public Supabase.Client SupabaseClient { get; set; } = default!;
        [Inject] public SettingsService AppSettings { get; set; } = default!;

        protected string newCompanyName = string.Empty;
        protected bool useLogo;
        protected string? newLogoUrl;
        protected bool showSuccess = false;
        protected bool isUploading = false;

        // Account data
        protected User? currentUser;
        protected string newPassword = "";
        protected string confirmPassword = "";
        protected string passErrorMessage = "";
        protected bool showPassSuccess = false;
        protected bool isChangingPass = false;

        protected override async Task OnInitializedAsync()
        {
            newCompanyName = AppSettings.CompanyName;
            useLogo = AppSettings.UseLogoForBranding;
            newLogoUrl = AppSettings.CustomLogoUrl;
            AppSettings.OnStateChanged += HandleStateChanged;

            currentUser = SupabaseClient.Auth.CurrentUser;
            if (currentUser == null)
            {
                // Fallback if CurrentUser is null at first
                var session = await SupabaseClient.Auth.RetrieveSessionAsync();
                currentUser = session?.User;
            }
        }

        private void HandleStateChanged() => StateHasChanged();

        public void Dispose()
        {
            AppSettings.OnStateChanged -= HandleStateChanged;
        }

        protected async Task HandleLogoUpload(InputFileChangeEventArgs e)
        {
            var file = e.File;
            if (file == null) return;

            isUploading = true;
            try 
            {
                var format = "image/png";
                var resizedImage = await file.RequestImageFileAsync(format, 200, 200);
                var buffer = new byte[resizedImage.Size];
                await resizedImage.OpenReadStream().ReadAsync(buffer);
                newLogoUrl = $"data:{format};base64,{Convert.ToBase64String(buffer)}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Logo upload error: {ex.Message}");
            }
            finally
            {
                isUploading = false;
            }
        }

        protected async Task SaveSettings()
        {
            AppSettings.CompanyName = newCompanyName;
            AppSettings.UseLogoForBranding = useLogo;
            AppSettings.CustomLogoUrl = newLogoUrl;
            showSuccess = true;
            
            await Task.Delay(3000);
            showSuccess = false;
            StateHasChanged();
        }

        protected async Task HandleChangePassword()
        {
            passErrorMessage = "";
            showPassSuccess = false;

            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
            {
                passErrorMessage = "Password must be at least 6 characters.";
                return;
            }

            if (newPassword != confirmPassword)
            {
                passErrorMessage = "Passwords do not match.";
                return;
            }

            isChangingPass = true;
            try
            {
                var attrs = new UserAttributes { Password = newPassword };
                var response = await SupabaseClient.Auth.Update(attrs);
                
                if (response != null)
                {
                    showPassSuccess = true;
                    newPassword = "";
                    confirmPassword = "";
                }
            }
            catch (Exception ex)
            {
                passErrorMessage = $"Failed to update password: {ex.Message}";
            }
            finally
            {
                isChangingPass = false;
            }
        }
    }
}
