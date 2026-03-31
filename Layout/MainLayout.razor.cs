using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using InventoryPlus.Services;
using Supabase;

namespace InventoryPlus.Layout
{
    public partial class MainLayout : LayoutComponentBase, IDisposable
    {
        [Inject] public NavigationManager NavManager { get; set; } = default!;
        [Inject] public IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] public SettingsService AppSettings { get; set; } = default!;
        [Inject] public InventoryService Inventory { get; set; } = default!;
        [Inject] public Client Supabase { get; set; } = default!;
        [Inject] public AuthenticationStateProvider AuthStateProvider { get; set; } = default!;
        [Inject] public PilotService PilotService { get; set; } = default!;

        protected bool showDropdown = false;
        protected bool isLightMode = false;
        protected bool showMobileNav = false;
        protected bool showOnboarding = false;
        protected string? currentUserEmail;
        private string currentPath = "";
        private ErrorBoundary? errorBoundary;
        protected Components.NotificationPanel? notificationPanel;

        protected void ToggleNotifications()
        {
            notificationPanel?.Toggle();
        }

        protected void ResetError()
        {
            errorBoundary?.Recover();
        }

        protected void ToggleMobileNav()
        {
            showMobileNav = !showMobileNav;
        }

        protected void CloseMobileNav()
        {
            showMobileNav = false;
        }

        protected void HandleGlobalClick()
        {
            showDropdown = false;
        }

        protected void HandleKeyDown(KeyboardEventArgs e)
        {
            if (e.Key == "Escape")
            {
                showDropdown = false;
                showMobileNav = false;
            }
        }

        protected void ToggleDropdown()
        {
            showDropdown = !showDropdown;
        }

        protected async Task ToggleTheme()
        {
            try
            {
                isLightMode = !isLightMode;
                await JSRuntime.InvokeVoidAsync("themeInterop.toggle", isLightMode);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error toggling theme: {ex.Message}");
            }
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                try
                {
                    isLightMode = await JSRuntime.InvokeAsync<bool>("themeInterop.isLight");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error initializing theme: {ex.Message}");
                }

                await LoadDataAsync();

                // Apply saved color scheme
                try
                {
                    var scheme = AppSettings.ColorScheme;
                    if (!string.IsNullOrEmpty(scheme))
                        await JSRuntime.InvokeVoidAsync("themeInterop.setScheme", scheme);
                }
                catch { }

                // Update PWA icon to user's logo if set
                try
                {
                    if (!string.IsNullOrEmpty(AppSettings.CustomLogoUrl))
                        await JSRuntime.InvokeVoidAsync("pwaIcon.update", AppSettings.CustomLogoUrl, AppSettings.CompanyName);
                }
                catch { }

                // Show onboarding wizard on first setup (no products/ingredients yet)
                if (Inventory.IsLoaded && !AppSettings.OnboardingCompleted
                    && !Inventory.ActiveProducts.Any() && !Inventory.ActiveIngredients.Any())
                {
                    showOnboarding = true;
                }

                StateHasChanged();
            }
        }

        private async Task LoadDataAsync()
        {
            if (Inventory.IsLoaded || Inventory.IsLoading) return;

            // Guest mode: load from localStorage only, skip Supabase
            if (AppSettings.IsGuestMode)
            {
                currentUserEmail = "Guest User";
                await Inventory.LoadGuestAsync(JSRuntime);
                return;
            }

            try
            {
                var user = Supabase.Auth.CurrentUser;
                if (user == null)
                {
                    var session = await Supabase.Auth.RetrieveSessionAsync();
                    user = session?.User;
                }

                // If still no user, try to get userId from localStorage to load cached data
                if (user == null)
                {
                    try
                    {
                        var cachedUserId = await JSRuntime.InvokeAsync<string?>("localStorage.getItem", "inv_last_user_id");
                        if (!string.IsNullOrEmpty(cachedUserId))
                        {
                            Console.WriteLine("Session expired — loading cached data for offline use");
                            await Inventory.LoadAsync(cachedUserId, JSRuntime);
                        }
                    }
                    catch { }
                    return;
                }

                currentUserEmail = user.Email;

                // Persist userId so we can load cache even when auth fails on refresh
                try
                {
                    await JSRuntime.InvokeVoidAsync("localStorage.setItem", "inv_last_user_id", user.Id);
                }
                catch { }

                await AppSettings.LoadAsync(user.Id);
                await Inventory.LoadAsync(user.Id, JSRuntime);
                Inventory.SetPilotService(PilotService);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LoadDataAsync error: {ex.Message}");
            }
        }

        protected void OnboardingDone()
        {
            showOnboarding = false;
            StateHasChanged();
        }

        protected override void OnInitialized()
        {
            AppSettings.OnStateChanged += HandleStateChanged;
            Inventory.OnStateChanged += HandleStateChanged;
            NavManager.LocationChanged += OnLocationChanged;
            currentPath = GetRelativePath();
        }

        private async void OnLocationChanged(object? sender, LocationChangedEventArgs e)
        {
            currentPath = GetRelativePath();
            try { await InvokeAsync(StateHasChanged); } catch { }
        }

        private string GetRelativePath()
        {
            var uri = new Uri(NavManager.Uri);
            return uri.AbsolutePath.Trim('/').ToLowerInvariant();
        }

        protected bool IsActive(string page) => currentPath == page.ToLowerInvariant();

        private async void HandleStateChanged()
        {
            try { await InvokeAsync(StateHasChanged); } catch { }
        }

        public void Dispose()
        {
            AppSettings.OnStateChanged -= HandleStateChanged;
            Inventory.OnStateChanged -= HandleStateChanged;
            NavManager.LocationChanged -= OnLocationChanged;
        }

        protected async Task SignOut()
        {
            if (AppSettings.IsGuestMode)
            {
                // Clear guest mode and navigate to login
                AppSettings.IsGuestMode = false;
                Inventory.IsGuestMode = false;
                Inventory.IsLoaded = false;
                Inventory.Ingredients = new();
                Inventory.Products = new();
                Inventory.Sales = new();
                var authProvider = (SupabaseAuthenticationStateProvider)AuthStateProvider;
                await authProvider.DisableGuestModeAsync();
                NavManager.NavigateTo("login");
                return;
            }

            try
            {
                await JSRuntime.InvokeVoidAsync("localStorage.removeItem", "sb_access_token");
                await JSRuntime.InvokeVoidAsync("localStorage.removeItem", "sb_refresh_token");
                await JSRuntime.InvokeVoidAsync("localStorage.removeItem", "inv_last_user_id");
            }
            catch { }

            var userId = Supabase.Auth.CurrentUser?.Id;
            await Supabase.Auth.SignOut();
            Inventory.IsLoaded = false;
            Inventory.Ingredients = new();
            Inventory.Products = new();
            Inventory.Sales = new();
            if (userId != null) await Inventory.ClearCacheAsync(JSRuntime, userId);
            NavManager.NavigateTo("login");
        }
    }
}
