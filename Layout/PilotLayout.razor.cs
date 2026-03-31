using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using InventoryPlus.Services;

namespace InventoryPlus.Layout
{
    public partial class PilotLayout : LayoutComponentBase, IDisposable
    {
        [Inject] public NavigationManager NavManager { get; set; } = default!;
        [Inject] public IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] public SettingsService AppSettings { get; set; } = default!;
        [Inject] public PilotService PilotService { get; set; } = default!;

        protected bool isLightMode = false;
        private string currentPath = "";
        private ErrorBoundary? errorBoundary;

        protected void ResetError() => errorBoundary?.Recover();

        protected void HandleGlobalClick() { }

        protected void HandleKeyDown(KeyboardEventArgs e) { }

        protected async Task ToggleTheme()
        {
            isLightMode = !isLightMode;
            await JSRuntime.InvokeVoidAsync("themeInterop.toggle", isLightMode);
        }

        protected async Task LeaveSession()
        {
            await PilotService.LeaveSessionAsync();
            NavManager.NavigateTo("login", forceLoad: true);
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                try
                {
                    isLightMode = await JSRuntime.InvokeAsync<bool>("themeInterop.isLight");
                    var scheme = AppSettings.ColorScheme;
                    if (!string.IsNullOrEmpty(scheme))
                        await JSRuntime.InvokeVoidAsync("themeInterop.setScheme", scheme);
                }
                catch { }
                StateHasChanged();
            }
        }

        protected override void OnInitialized()
        {
            PilotService.OnStateChanged += HandleStateChanged;
            NavManager.LocationChanged += OnLocationChanged;
            currentPath = GetRelativePath();

            // If not in pilot mode, redirect to login
            if (!PilotService.IsPilotMode)
                NavManager.NavigateTo("login");
        }

        private async void OnLocationChanged(object? sender, LocationChangedEventArgs e)
        {
            currentPath = GetRelativePath();

            // Block navigation to restricted pages
            var allowed = new[] { "sales", "products" };
            if (!allowed.Any(a => currentPath.StartsWith(a, StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrEmpty(currentPath))
            {
                NavManager.NavigateTo("sales");
                return;
            }

            try { await InvokeAsync(StateHasChanged); } catch { }
        }

        private string GetRelativePath()
        {
            var uri = new Uri(NavManager.Uri);
            return uri.AbsolutePath.TrimStart('/').ToLowerInvariant();
        }

        protected bool IsActive(string page)
        {
            return currentPath.StartsWith(page, StringComparison.OrdinalIgnoreCase);
        }

        private async void HandleStateChanged()
        {
            try { await InvokeAsync(StateHasChanged); } catch { }
        }

        public void Dispose()
        {
            PilotService.OnStateChanged -= HandleStateChanged;
            NavManager.LocationChanged -= OnLocationChanged;
        }
    }
}
