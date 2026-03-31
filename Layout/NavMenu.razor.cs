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

        private async void HandleStateChanged()
        {
            try { await InvokeAsync(StateHasChanged); } catch { }
        }

        public void Dispose()
        {
            AppSettings.OnStateChanged -= HandleStateChanged;
        }

        protected void SetViewPos() => AppSettings.PosActiveView = "pos";
        protected void SetViewHistory() => AppSettings.PosActiveView = "history";
    }
}
