using Microsoft.AspNetCore.Components;
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

        protected bool showDropdown = false;
        protected bool isLightMode = false;
        protected bool showMobileNav = false;
        protected bool showOnboarding = false;
        protected string? currentUserEmail;
        private ErrorBoundary? errorBoundary;

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

                // Check if onboarding needed after data loads
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
            try
            {
                var user = Supabase.Auth.CurrentUser;
                if (user == null)
                {
                    var session = await Supabase.Auth.RetrieveSessionAsync();
                    user = session?.User;
                }
                if (user == null) return;

                currentUserEmail = user.Email;
                await AppSettings.LoadAsync(user.Id);
                await Inventory.LoadAsync(user.Id, JSRuntime);
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
        }

        private void HandleStateChanged()
        {
            InvokeAsync(StateHasChanged);
        }

        public void Dispose()
        {
            AppSettings.OnStateChanged -= HandleStateChanged;
            Inventory.OnStateChanged -= HandleStateChanged;
        }

        protected async Task SignOut()
        {
            try
            {
                await JSRuntime.InvokeVoidAsync("localStorage.removeItem", "sb_access_token");
                await JSRuntime.InvokeVoidAsync("localStorage.removeItem", "sb_refresh_token");
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
