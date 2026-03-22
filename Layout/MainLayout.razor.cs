using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using InventoryPlus.Services;
using Microsoft.AspNetCore.Components.Web;
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

        protected void ToggleMobileNav()
        {
            showMobileNav = !showMobileNav;
        }

        protected void CloseMobileNav()
        {
            showMobileNav = false;
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

                await AppSettings.LoadAsync(user.Id);
                await Inventory.LoadAsync(user.Id, JSRuntime);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LoadDataAsync error: {ex.Message}");
            }
        }

        protected override void OnInitialized()
        {
            AppSettings.OnStateChanged += HandleStateChanged;
            Inventory.OnStateChanged += HandleStateChanged;
        }

        private void HandleStateChanged() => StateHasChanged();

        public void Dispose()
        {
            AppSettings.OnStateChanged -= HandleStateChanged;
            Inventory.OnStateChanged -= HandleStateChanged;
        }

        protected async Task SignOut()
        {
            // Clear persisted tokens from localStorage before signing out
            try
            {
                await JSRuntime.InvokeVoidAsync("localStorage.removeItem", "sb_access_token");
                await JSRuntime.InvokeVoidAsync("localStorage.removeItem", "sb_refresh_token");
            }
            catch { }

            var userId = Supabase.Auth.CurrentUser?.Id;
            await Supabase.Auth.SignOut();
            // Reset service state so next login reloads fresh data
            Inventory.IsLoaded = false;
            Inventory.Ingredients = new();
            Inventory.Products = new();
            Inventory.Sales = new();
            if (userId != null) await Inventory.ClearCacheAsync(JSRuntime, userId);
            NavManager.NavigateTo("login");
        }
    }
}
