using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using InventoryPlus.Services;
using Microsoft.AspNetCore.Components.Web;

namespace InventoryPlus.Layout
{
    public partial class MainLayout : LayoutComponentBase, IDisposable
    {
        [Inject] public NavigationManager NavManager { get; set; } = default!;
        [Inject] public IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] public SettingsService AppSettings { get; set; } = default!;

        protected bool showDropdown = false;
        protected bool isLightMode = false;

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

        protected void SignOut()
        {
            NavManager.NavigateTo("login", true);
        }
    }
}
