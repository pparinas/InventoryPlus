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
                    StateHasChanged();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error initializing theme: {ex.Message}");
                }
            }
        }

        protected override void OnInitialized()
        {
            AppSettings.OnStateChanged += HandleStateChanged;
        }

        private void HandleStateChanged() => StateHasChanged();

        public void Dispose()
        {
            AppSettings.OnStateChanged -= HandleStateChanged;
        }

        protected async Task SignOut()
        {
            await Supabase.Auth.SignOut();
            NavManager.NavigateTo("login");
        }
    }
}
