using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using InventoryPlus.Services;
using Supabase;
using System.Security.Claims;

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

        [CascadingParameter] private Task<AuthenticationState>? AuthenticationStateTask { get; set; }

        protected bool showDropdown = false;
        protected bool isLightMode = false;
        protected bool showMobileNav = false;
        protected bool showOnboarding = false;
        protected string? currentUserEmail;
        private string currentPath = "";
        private string? _lastAuthUserId = null;
        private DateTime _lastHeartbeat = DateTime.MinValue;
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
            try
            {
                // Await the auth state provider — this handles full session restoration
                // (in-memory check → RetrieveSessionAsync → SetSession from localStorage → RefreshSession)
                // so the user identity is resolved before we decide how to load data.
                var authState = await (AuthenticationStateTask ?? AuthStateProvider.GetAuthenticationStateAsync());
                var userId = authState.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var isAuthenticated = authState.User?.Identity?.IsAuthenticated == true
                                      && !string.IsNullOrEmpty(userId)
                                      && userId != "guest";

                if (!isAuthenticated)
                {
                    // Session truly expired or not present — load from cache for offline use
                    try
                    {
                        var cachedUserId = await JSRuntime.InvokeAsync<string?>("localStorage.getItem", "inv_last_user_id");
                        if (!string.IsNullOrEmpty(cachedUserId))
                        {
                            Console.WriteLine("Session expired — loading cached data for offline use");
                            var settingsTask = !AppSettings.IsLoaded
                                ? AppSettings.LoadAsync(cachedUserId, JSRuntime)   // JSRuntime required for cache read
                                : Task.CompletedTask;
                            var inventoryTask = !Inventory.IsLoaded && !Inventory.IsLoading
                                ? Inventory.LoadAsync(cachedUserId, JSRuntime)
                                : Task.CompletedTask;
                            await Task.WhenAll(settingsTask, inventoryTask);
                        }
                    }
                    catch { }
                    return;
                }

                currentUserEmail = authState.User?.FindFirst(ClaimTypes.Email)?.Value;
                var safeUserId = userId!;  // non-null: guarded by isAuthenticated check above

                // Persist userId for offline fallback on next refresh
                try
                {
                    await JSRuntime.InvokeVoidAsync("localStorage.setItem", "inv_last_user_id", safeUserId);
                }
                catch { }

                // Load settings and inventory in parallel (cache-first, Supabase in background)
                var loadSettingsTask = !AppSettings.IsLoaded
                    ? AppSettings.LoadAsync(safeUserId, JSRuntime)
                    : Task.CompletedTask;
                var loadInventoryTask = !Inventory.IsLoaded && !Inventory.IsLoading
                    ? Inventory.LoadAsync(safeUserId, JSRuntime)
                    : Task.CompletedTask;
                await Task.WhenAll(loadSettingsTask, loadInventoryTask);
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
            AuthStateProvider.AuthenticationStateChanged += OnAuthenticationStateChanged;
        }

        private async void OnLocationChanged(object? sender, LocationChangedEventArgs e)
        {
            currentPath = GetRelativePath();

            // POS mode: redirect non-POS pages to sales
            if (AppSettings.IsPosMode && currentPath != "sales" && currentPath != "login" && currentPath != "register")
            {
                NavManager.NavigateTo("sales");
                return;
            }

            // Heartbeat: update last_sign_in_at every 5 minutes
            if ((DateTime.UtcNow - _lastHeartbeat).TotalMinutes >= 5)
            {
                _lastHeartbeat = DateTime.UtcNow;
                _ = UpdateHeartbeatAsync();
            }

            try { await InvokeAsync(StateHasChanged); } catch { }
        }

        private async Task UpdateHeartbeatAsync()
        {
            try
            {
                var user = Supabase.Auth.CurrentUser;
                if (user != null)
                {
                    await Supabase.Rpc("update_last_sign_in", new Dictionary<string, object>
                    {
                        { "user_id", user.Id }
                    });
                }
            }
            catch { }
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

        private async void OnAuthenticationStateChanged(Task<AuthenticationState> authStateTask)
        {
            try
            {
                var authState = await authStateTask;
                var userId = authState.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var isAuthenticated = authState.User?.Identity?.IsAuthenticated == true
                                      && !string.IsNullOrEmpty(userId)
                                      && userId != "guest";

                // If auth just resolved to an authenticated user we haven't loaded for yet,
                // and inventory is in offline mode (loaded from cache but not synced),
                // reset and trigger a full Supabase load.
                if (isAuthenticated && userId != _lastAuthUserId && (Inventory.IsOffline || !Inventory.IsLoaded) && !Inventory.IsLoading)
                {
                    _lastAuthUserId = userId;
                    Inventory.IsLoaded = false;
                    AppSettings.IsLoaded = false;
                    await InvokeAsync(LoadDataAsync);
                }
                else if (isAuthenticated)
                {
                    _lastAuthUserId = userId;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OnAuthenticationStateChanged error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            AppSettings.OnStateChanged -= HandleStateChanged;
            Inventory.OnStateChanged -= HandleStateChanged;
            NavManager.LocationChanged -= OnLocationChanged;
            AuthStateProvider.AuthenticationStateChanged -= OnAuthenticationStateChanged;
        }

        protected async Task SignOut()
        {
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
