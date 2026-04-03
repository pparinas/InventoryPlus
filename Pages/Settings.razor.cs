using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.JSInterop;
using Supabase;
using Supabase.Interfaces;
using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;
using InventoryPlus.Services;
using User = Supabase.Gotrue.User;
using Session = Supabase.Gotrue.Session;

namespace InventoryPlus.Pages
{
    [Authorize]
    public partial class Settings : ComponentBase, IDisposable
    {
        [Inject] public Supabase.Client SupabaseClient { get; set; } = default!;
        [Inject] public SettingsService AppSettings { get; set; } = default!;
        [Inject] public ToastService Toast { get; set; } = default!;
        [Inject] public NavigationManager Nav { get; set; } = default!;
        [Inject] public IJSRuntime JSRuntime { get; set; } = default!;

        protected string newCompanyName = string.Empty;
        protected bool useLogo;
        protected string? newLogoUrl;
        protected bool showSuccess = false;
        protected bool isUploading = false;
        protected IBrowserFile? pendingLogoFile;

        // Account data
        protected User? currentUser;
        protected string currentPassword = "";
        protected string newPassword = "";
        protected string confirmPassword = "";
        protected string passErrorMessage = "";
        protected bool showPassSuccess = false;
        protected bool isChangingPass = false;

        // PIN
        protected string currentPin = "";
        protected string newPin = "";
        protected string confirmPin = "";
        protected string pinError = "";
        protected bool showPinSuccess = false;

        // Color Scheme
        protected string selectedColorScheme = "indigo";
        protected Dictionary<string, string> colorSchemes = new()
        {
            { "indigo", "#6366f1" },
            { "emerald", "#10b981" },
            { "rose", "#f43f5e" },
            { "amber", "#f59e0b" },
            { "cyan", "#06b6d4" },
            { "violet", "#8b5cf6" },
            { "pink", "#ec4899" }
        };

        // Inventory toggle
        protected bool showInventoryTab = true;
        protected bool showOpexTab = false;

        // Install App
        protected bool showInstallSection = false;
        protected bool canInstallNative = false;
        protected bool isIosDevice = false;
        protected bool isAppStandalone = false;

        protected override async Task OnInitializedAsync()
        {
            if (AppSettings.IsGuestMode) { Nav.NavigateTo("dashboard"); return; }
            AppSettings.OnStateChanged += HandleStateChanged;

            currentUser = SupabaseClient.Auth.CurrentUser;
            if (currentUser == null)
            {
                var session = await SupabaseClient.Auth.RetrieveSessionAsync();
                currentUser = session?.User;
            }

            if (currentUser != null)
                await AppSettings.LoadAsync(currentUser.Id);

            newCompanyName = AppSettings.CompanyName;
            useLogo = AppSettings.UseLogoForBranding;
            newLogoUrl = AppSettings.CustomLogoUrl;
            selectedColorScheme = AppSettings.ColorScheme;
            showInventoryTab = AppSettings.ShowInventoryTab;
            showOpexTab = AppSettings.ShowOpexTab;
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                try
                {
                    isAppStandalone = await JSRuntime.InvokeAsync<bool>("pwaInstall.isStandalone");
                    if (!isAppStandalone)
                    {
                        canInstallNative = await JSRuntime.InvokeAsync<bool>("pwaInstall.canInstall");
                        isIosDevice = await JSRuntime.InvokeAsync<bool>("pwaInstall.isIos");
                        showInstallSection = canInstallNative || isIosDevice;
                        StateHasChanged();
                    }
                }
                catch { }
            }
        }

        protected async Task InstallApp()
        {
            await JSRuntime.InvokeAsync<bool>("pwaInstall.prompt");
            canInstallNative = false;
            showInstallSection = isIosDevice;
        }

        private void HandleStateChanged() => InvokeAsync(StateHasChanged);

        public void Dispose()
        {
            AppSettings.OnStateChanged -= HandleStateChanged;
        }

        protected async Task HandleLogoUpload(InputFileChangeEventArgs e)
        {
            pendingLogoFile = e.File;
        }

        protected async Task UploadLogo()
        {
            if (pendingLogoFile == null || currentUser == null) return;

            isUploading = true;
            try
            {
                var format = "image/jpeg";
                var resizedImage = await pendingLogoFile.RequestImageFileAsync(format, 150, 150);
                var buffer = new byte[resizedImage.Size];
                await resizedImage.OpenReadStream().ReadAsync(buffer);

                var path = $"{currentUser.Id}/logo.jpg";
                await SupabaseClient.Storage
                    .From("branding")
                    .Upload(buffer, path, new Supabase.Storage.FileOptions { ContentType = format, Upsert = true });

                // Generate a signed URL for immediate display and propagate to AppSettings
                newLogoUrl = await SupabaseClient.Storage
                    .From("branding")
                    .CreateSignedUrl(path, 60 * 60 * 24 * 7);
                AppSettings.CustomLogoUrl = newLogoUrl; // setter auto-extracts the storage path

                pendingLogoFile = null;
                Toast.Show("Logo uploaded successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Logo upload error: {ex.Message}");
                Toast.Show("Failed to upload logo.", "error");
            }
            finally
            {
                isUploading = false;
                StateHasChanged();
            }
        }
        protected async Task SaveSettings()
        {
            AppSettings.CompanyName = newCompanyName;
            AppSettings.UseLogoForBranding = useLogo;
            AppSettings.CustomLogoUrl = newLogoUrl;
            AppSettings.ColorScheme = selectedColorScheme;
            await JSRuntime.InvokeVoidAsync("themeInterop.setScheme", selectedColorScheme);

            // Update PWA icon to new logo
            if (!string.IsNullOrEmpty(newLogoUrl))
                await JSRuntime.InvokeVoidAsync("pwaIcon.update", newLogoUrl, newCompanyName);

            try
            {
                if (currentUser != null)
                    await AppSettings.SaveAsync(currentUser.Id);

                Toast.Show("Settings saved successfully!");
                showSuccess = true;
                await Task.Delay(3000);
                showSuccess = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SaveSettings error: {ex.Message}");
                Toast.Show("Failed to save settings.", "error");
            }
            StateHasChanged();
        }

        protected async Task HandleChangePassword()
        {
            passErrorMessage = "";
            showPassSuccess = false;

            if (string.IsNullOrWhiteSpace(currentPassword))
            {
                passErrorMessage = "Please enter your current password.";
                return;
            }

            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
            {
                passErrorMessage = "New password must be at least 6 characters.";
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
                // Verify current password by re-authenticating
                var email = currentUser?.Email;
                if (string.IsNullOrEmpty(email))
                {
                    passErrorMessage = "Unable to verify account.";
                    return;
                }

                try
                {
                    await SupabaseClient.Auth.SignIn(email, currentPassword);
                }
                catch
                {
                    passErrorMessage = "Current password is incorrect.";
                    return;
                }

                var attrs = new UserAttributes { Password = newPassword };
                var response = await SupabaseClient.Auth.Update(attrs);
                
                if (response != null)
                {
                    showPassSuccess = true;
                    currentPassword = "";
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

        protected async Task SavePreferences()
        {
            try
            {
                if (currentUser != null)
                    await AppSettings.SaveAsync(currentUser.Id);
                Toast.Show("Preferences saved!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SavePreferences error: {ex.Message}");
                Toast.Show("Failed to save preferences.", "error");
            }
        }

        protected async Task SavePin()
        {
            pinError = "";
            showPinSuccess = false;

            if (AppSettings.HasPin)
            {
                if (string.IsNullOrEmpty(currentPin) || !AppSettings.VerifyPin(currentPin))
                {
                    pinError = "Current PIN is incorrect.";
                    return;
                }
            }

            if (string.IsNullOrEmpty(newPin) || newPin.Length != 4 || !newPin.All(char.IsDigit))
            {
                pinError = "PIN must be exactly 4 digits.";
                return;
            }

            if (newPin != confirmPin)
            {
                pinError = "PINs do not match.";
                return;
            }

            AppSettings.SetPin(newPin);
            if (currentUser != null)
                await AppSettings.SaveAsync(currentUser.Id);

            currentPin = "";
            newPin = "";
            confirmPin = "";
            showPinSuccess = true;
            Toast.Show("PIN saved successfully!");
            await Task.Delay(3000);
            showPinSuccess = false;
            StateHasChanged();
        }

        protected async Task RemovePin()
        {
            pinError = "";
            showPinSuccess = false;

            if (string.IsNullOrEmpty(currentPin) || !AppSettings.VerifyPin(currentPin))
            {
                pinError = "Enter your current PIN to remove it.";
                return;
            }

            AppSettings.PinHash = string.Empty;
            if (currentUser != null)
                await AppSettings.SaveAsync(currentUser.Id);

            currentPin = "";
            newPin = "";
            confirmPin = "";
            Toast.Show("PIN removed.");
        }

        protected async Task SaveColorScheme()
        {
            AppSettings.ColorScheme = selectedColorScheme;
            await JSRuntime.InvokeVoidAsync("themeInterop.setScheme", selectedColorScheme);
            try
            {
                if (currentUser != null)
                    await AppSettings.SaveAsync(currentUser.Id);
                Toast.Show("Color scheme applied!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SaveColorScheme error: {ex.Message}");
                Toast.Show("Failed to save color scheme.", "error");
            }
        }

        protected async Task SaveNavPreferences()
        {
            AppSettings.ShowInventoryTab = showInventoryTab;
            AppSettings.ShowOpexTab = showOpexTab;
            try
            {
                if (currentUser != null)
                    await AppSettings.SaveAsync(currentUser.Id);
                Toast.Show("Navigation preferences saved!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SaveNavPreferences error: {ex.Message}");
                Toast.Show("Failed to save navigation preferences.", "error");
            }
        }

        protected void TogglePosMode()
        {
            AppSettings.IsPosMode = !AppSettings.IsPosMode;
            if (AppSettings.IsPosMode)
            {
                Toast.Show("POS Mode activated. Only Point of Sale is accessible.", "info");
                Nav.NavigateTo("sales");
            }
            else
            {
                Toast.Show("POS Mode deactivated.", "info");
            }
        }
    }
}
