using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using InventoryPlus.Models;

namespace InventoryPlus.Services
{
    public class PilotService
    {
        private readonly Supabase.Client _supabase;

        public PilotSession? ActiveSession { get; private set; }
        public bool IsPilotMode => ActiveSession != null && ActiveSession.IsActive;
        public string OperatorName => ActiveSession?.OperatorName ?? "";
        public Guid OwnerGuid => ActiveSession?.OwnerGuid ?? Guid.Empty;

        // Activity log (owner-side monitoring)
        public List<PilotActivityLog> ActivityLog { get; private set; } = new();
        public List<PilotSession> OwnerSessions { get; private set; } = new();

        public event Action? OnStateChanged;

        public PilotService(Supabase.Client supabase)
        {
            _supabase = supabase;
        }

        // ── Owner: Generate Access Code ────────────────────────────────────

        public string GenerateAccessCode()
        {
            var rng = new Random();
            return rng.Next(100000, 999999).ToString();
        }

        public async Task<PilotSession> CreateSessionAsync(Guid ownerGuid, string accessCode, int expiryHours = 24)
        {
            // End any existing active sessions first
            await EndAllActiveSessionsAsync(ownerGuid);

            var session = new PilotSession
            {
                OwnerGuid = ownerGuid,
                AccessCode = accessCode,
                Status = "active",
                StartedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(expiryHours)
            };

            var resp = await _supabase.From<PilotSession>().Insert(session);
            var saved = resp.Models.FirstOrDefault();
            if (saved != null) session.Guid = saved.Guid;

            await LoadOwnerSessionsAsync(ownerGuid);
            NotifyStateChanged();
            return session;
        }

        // ── Owner: Manage Sessions ─────────────────────────────────────────

        public async Task LoadOwnerSessionsAsync(Guid ownerGuid)
        {
            try
            {
                var resp = await _supabase.From<PilotSession>()
                    .Where(s => s.OwnerGuid == ownerGuid)
                    .Order("started_at", Supabase.Postgrest.Constants.Ordering.Descending)
                    .Get();
                OwnerSessions = resp.Models;
                NotifyStateChanged();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LoadOwnerSessions error: {ex.Message}");
            }
        }

        public async Task EndSessionAsync(PilotSession session)
        {
            session.Status = "ended";
            session.EndedAt = DateTime.UtcNow;
            await _supabase.From<PilotSession>().Upsert(session);

            if (ActiveSession?.Guid == session.Guid)
                ActiveSession = null;

            NotifyStateChanged();
        }

        public async Task EndAllActiveSessionsAsync(Guid ownerGuid)
        {
            try
            {
                var resp = await _supabase.From<PilotSession>()
                    .Where(s => s.OwnerGuid == ownerGuid)
                    .Where(s => s.Status == "active")
                    .Get();

                foreach (var session in resp.Models)
                {
                    session.Status = "ended";
                    session.EndedAt = DateTime.UtcNow;
                    await _supabase.From<PilotSession>().Upsert(session);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"EndAllActiveSessions error: {ex.Message}");
            }
        }

        // ── Operator: Join Session ─────────────────────────────────────────

        public async Task<(bool Success, string Error)> JoinSessionAsync(string accessCode, string operatorName)
        {
            if (string.IsNullOrWhiteSpace(accessCode) || accessCode.Length != 6)
                return (false, "Access code must be 6 digits.");

            if (string.IsNullOrWhiteSpace(operatorName))
                return (false, "Please enter your name.");

            try
            {
                var resp = await _supabase.From<PilotSession>()
                    .Where(s => s.AccessCode == accessCode)
                    .Where(s => s.Status == "active")
                    .Get();

                var session = resp.Models.FirstOrDefault();
                if (session == null)
                    return (false, "Invalid or expired access code.");

                if (session.IsExpired)
                {
                    session.Status = "ended";
                    session.EndedAt = DateTime.UtcNow;
                    await _supabase.From<PilotSession>().Upsert(session);
                    return (false, "This access code has expired.");
                }

                // Update operator name on session
                session.OperatorName = operatorName;
                await _supabase.From<PilotSession>().Upsert(session);

                ActiveSession = session;

                // Log session join
                await LogActivityAsync("session_start", "session", session.Guid,
                    $"{operatorName} joined the session");

                NotifyStateChanged();
                return (true, "");
            }
            catch (Exception ex)
            {
                return (false, $"Connection failed: {ex.Message}");
            }
        }

        public async Task LeaveSessionAsync()
        {
            if (ActiveSession != null)
            {
                await LogActivityAsync("session_end", "session", ActiveSession.Guid,
                    $"{ActiveSession.OperatorName} left the session");
            }
            ActiveSession = null;
            NotifyStateChanged();
        }

        public bool IsSessionStillActive()
        {
            if (ActiveSession == null) return false;
            return ActiveSession.IsActive;
        }

        // ── Activity Logging ───────────────────────────────────────────────

        public async Task LogActivityAsync(string action, string entityType, Guid entityGuid, string detail)
        {
            if (ActiveSession == null) return;

            var log = new PilotActivityLog
            {
                SessionGuid = ActiveSession.Guid,
                OwnerGuid = ActiveSession.OwnerGuid,
                OperatorName = ActiveSession.OperatorName,
                Action = action,
                EntityType = entityType,
                EntityGuid = entityGuid,
                Detail = detail,
                Timestamp = DateTime.UtcNow
            };

            try
            {
                await _supabase.From<PilotActivityLog>().Insert(log);
                ActivityLog.Insert(0, log);
                NotifyStateChanged();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LogActivity error: {ex.Message}");
            }
        }

        // ── Owner: Load Activity Log ───────────────────────────────────────

        public async Task LoadActivityLogAsync(Guid ownerGuid, int limit = 50)
        {
            try
            {
                var resp = await _supabase.From<PilotActivityLog>()
                    .Where(l => l.OwnerGuid == ownerGuid)
                    .Order("timestamp", Supabase.Postgrest.Constants.Ordering.Descending)
                    .Limit(limit)
                    .Get();
                ActivityLog = resp.Models;
                NotifyStateChanged();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LoadActivityLog error: {ex.Message}");
            }
        }

        public async Task RefreshActivityLogAsync(Guid ownerGuid)
        {
            await LoadActivityLogAsync(ownerGuid);
        }

        private void NotifyStateChanged() => OnStateChanged?.Invoke();
    }
}
