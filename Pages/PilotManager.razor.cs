using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Authorization;
using InventoryPlus.Services;
using InventoryPlus.Models;

namespace InventoryPlus.Pages
{
    [Authorize]
    public partial class PilotManager : ComponentBase, IDisposable
    {
        [Inject] public PilotService Pilot { get; set; } = default!;
        [Inject] public SettingsService AppSettings { get; set; } = default!;
        [Inject] public InventoryService Inventory { get; set; } = default!;
        [Inject] public ToastService Toast { get; set; } = default!;
        [Inject] public NavigationManager Nav { get; set; } = default!;
        [Inject] public Supabase.Client SupabaseClient { get; set; } = default!;

        protected string generatedCode = "";
        protected int expiryHours = 24;
        protected bool isCreating = false;
        protected bool isLoading = true;
        protected bool showEndConfirm = false;
        protected PilotSession? endingSession;

        // Activity log
        protected bool isRefreshing = false;
        protected string activityFilter = "All";

        protected Guid CurrentUserId
        {
            get
            {
                var user = SupabaseClient.Auth.CurrentUser;
                return user != null && Guid.TryParse(user.Id, out var id) ? id : Guid.Empty;
            }
        }

        protected override async Task OnInitializedAsync()
        {
            if (AppSettings.IsGuestMode) { Nav.NavigateTo("dashboard"); return; }

            Pilot.OnStateChanged += HandleStateChanged;

            if (CurrentUserId != Guid.Empty)
            {
                await Pilot.LoadOwnerSessionsAsync(CurrentUserId);
                await Pilot.LoadActivityLogAsync(CurrentUserId);
            }

            isLoading = false;
        }

        private async void HandleStateChanged()
        {
            try { await InvokeAsync(StateHasChanged); } catch { }
        }

        public void Dispose()
        {
            Pilot.OnStateChanged -= HandleStateChanged;
        }

        protected PilotSession? CurrentActiveSession =>
            Pilot.OwnerSessions.FirstOrDefault(s => s.Status == "active" && !s.IsExpired);

        protected IEnumerable<PilotSession> PastSessions =>
            Pilot.OwnerSessions.Where(s => s.Status == "ended" || s.IsExpired)
                .OrderByDescending(s => s.StartedAt);

        protected IEnumerable<PilotActivityLog> FilteredActivity
        {
            get
            {
                var logs = Pilot.ActivityLog.AsEnumerable();
                if (activityFilter != "All")
                    logs = logs.Where(l => l.Action == activityFilter);
                return logs;
            }
        }

        protected double PilotSalesTotal
        {
            get
            {
                var sessionGuids = Pilot.OwnerSessions
                    .Where(s => s.Status == "active" || s.EndedAt?.Date == DateTime.UtcNow.Date)
                    .Select(s => s.Guid)
                    .ToHashSet();

                return Pilot.ActivityLog
                    .Where(l => l.Action == "sale" && sessionGuids.Contains(l.SessionGuid))
                    .Count();
            }
        }

        protected async Task GenerateCode()
        {
            isCreating = true;
            try
            {
                generatedCode = Pilot.GenerateAccessCode();
                await Pilot.CreateSessionAsync(CurrentUserId, generatedCode, expiryHours);
                Toast.Show("Pilot session created! Share the access code with your operator.");
            }
            catch (Exception ex)
            {
                Toast.Show($"Error: {ex.Message}", "error");
            }
            isCreating = false;
        }

        protected void StartEndSession(PilotSession session)
        {
            endingSession = session;
            showEndConfirm = true;
        }

        protected async Task ConfirmEndSession()
        {
            if (endingSession == null) return;
            try
            {
                await Pilot.EndSessionAsync(endingSession);
                await Pilot.LoadOwnerSessionsAsync(CurrentUserId);
                Toast.Show("Pilot session ended.");
            }
            catch (Exception ex)
            {
                Toast.Show($"Error: {ex.Message}", "error");
            }
            showEndConfirm = false;
            endingSession = null;
        }

        protected async Task RefreshActivity()
        {
            isRefreshing = true;
            StateHasChanged();
            await Pilot.RefreshActivityLogAsync(CurrentUserId);
            isRefreshing = false;
        }
    }
}
