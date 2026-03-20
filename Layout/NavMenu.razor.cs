using Microsoft.AspNetCore.Components;
using InventoryPlus.Services;

namespace InventoryPlus.Layout
{
    public partial class NavMenu : ComponentBase, IDisposable
    {
        [Inject] public SettingsService AppSettings { get; set; } = default!;

        protected override void OnInitialized()
        {
            AppSettings.OnStateChanged += HandleStateChanged;
        }

        private void HandleStateChanged() => StateHasChanged();

        public void Dispose()
        {
            AppSettings.OnStateChanged -= HandleStateChanged;
        }
    }
}
