using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using System.Security.Claims;
using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;
using Supabase.Interfaces;

namespace InventoryPlus.Services
{
    public class SupabaseAuthenticationStateProvider : AuthenticationStateProvider, IDisposable
    {
        private readonly Supabase.Client _client;
        private readonly IJSRuntime _jsRuntime;
        private const string AccessTokenKey = "sb_access_token";
        private const string RefreshTokenKey = "sb_refresh_token";

        public SupabaseAuthenticationStateProvider(Supabase.Client client, IJSRuntime jsRuntime)
        {
            _client = client;
            _jsRuntime = jsRuntime;
            _client.Auth.AddStateChangedListener(OnAuthStateChanged);
        }

        private bool _isRestoringSession = false;

        private void OnAuthStateChanged(IGotrueClient<User, Session> sender, Supabase.Gotrue.Constants.AuthState e)
        {
            // During session restore (SetSession), Supabase briefly fires a null-session event.
            // Guard against clearing tokens in that window.
            if (_isRestoringSession) return;

            var session = _client.Auth.CurrentSession;
            if (session != null && !string.IsNullOrEmpty(session.AccessToken))
            {
                _ = SaveTokensAsync(session.AccessToken!, session.RefreshToken!);
            }
            else if (e == Supabase.Gotrue.Constants.AuthState.SignedOut)
            {
                _ = ClearTokensAsync();
            }
            NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var session = _client.Auth.CurrentSession;
            var user = _client.Auth.CurrentUser;

            // 1. Try to restore from Supabase's in-memory session
            if (session == null || user == null)
            {
                try
                {
                    session = await _client.Auth.RetrieveSessionAsync();
                    user = session?.User;
                }
                catch { }
            }

            // 2. If still no session, load tokens from localStorage and call SetSession
            if (session == null || user == null)
            {
                try
                {
                    var accessToken = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", AccessTokenKey);
                    var refreshToken = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", RefreshTokenKey);

                    if (!string.IsNullOrEmpty(accessToken) && !string.IsNullOrEmpty(refreshToken))
                    {
                        _isRestoringSession = true;
                        try
                        {
                            session = await _client.Auth.SetSession(accessToken, refreshToken);
                            user = session?.User;
                            if (session != null && !string.IsNullOrEmpty(session.AccessToken))
                                await SaveTokensAsync(session.AccessToken!, session.RefreshToken!);
                        }
                        finally
                        {
                            _isRestoringSession = false;
                        }
                    }
                }
                catch { _isRestoringSession = false; }
            }

            if (session == null || user == null)
                return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id ?? string.Empty),
                new Claim(ClaimTypes.Email, user.Email ?? string.Empty)
            };

            // Check is_admin from user_profiles table
            try
            {
                if (Guid.TryParse(user.Id, out var userGuid))
                {
                    var profileResp = await _client.From<InventoryPlus.Models.UserProfile>()
                        .Where(p => p.Guid == userGuid)
                        .Single();
                    if (profileResp?.IsAdmin == true)
                        claims.Add(new Claim(ClaimTypes.Role, "Admin"));
                }
            }
            catch { }
            // Fallback: hardcoded super-admin email
            if (user.Email?.ToLower() == "pparinas@ucpm.com" && !claims.Any(c => c.Type == ClaimTypes.Role))
            {
                claims.Add(new Claim(ClaimTypes.Role, "Admin"));
            }

            var identity = new ClaimsIdentity(claims, "Supabase");
            return new AuthenticationState(new ClaimsPrincipal(identity));
        }

        private async Task SaveTokensAsync(string accessToken, string refreshToken)
        {
            try
            {
                await _jsRuntime.InvokeVoidAsync("localStorage.setItem", AccessTokenKey, accessToken);
                await _jsRuntime.InvokeVoidAsync("localStorage.setItem", RefreshTokenKey, refreshToken);
            }
            catch { }
        }

        private async Task ClearTokensAsync()
        {
            try
            {
                await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", AccessTokenKey);
                await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", RefreshTokenKey);
            }
            catch { }
        }

        public void Dispose()
        {
            if (_client?.Auth != null)
                _client.Auth.RemoveStateChangedListener(OnAuthStateChanged);
        }
    }
}

