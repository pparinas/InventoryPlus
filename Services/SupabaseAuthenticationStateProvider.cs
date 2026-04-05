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
        private const string GuestModeKey = "guest_mode";

        private bool _isGuestMode = false;
        public bool IsGuestMode => _isGuestMode;

        // Cache admin status to avoid querying user_profiles on every auth evaluation
        private string? _cachedAdminUserId;
        private bool _cachedIsAdmin;

        public SupabaseAuthenticationStateProvider(Supabase.Client client, IJSRuntime jsRuntime)
        {
            _client = client;
            _jsRuntime = jsRuntime;
            _client.Auth.AddStateChangedListener(OnAuthStateChanged);
        }

        private bool _isRestoringSession = false;
        private bool _isNotifying = false;

        private async void OnAuthStateChanged(IGotrueClient<User, Session> sender, Supabase.Gotrue.Constants.AuthState e)
        {
            if (_isRestoringSession || _isNotifying) return;

            try
            {
                _isNotifying = true;
                var session = _client.Auth.CurrentSession;
                if (session != null && !string.IsNullOrEmpty(session.AccessToken))
                {
                    try { await SaveTokensAsync(session.AccessToken!, session.RefreshToken!); } catch { }
                }
                else if (e == Supabase.Gotrue.Constants.AuthState.SignedOut)
                {
                    try { await ClearTokensAsync(); } catch { }
                }
                NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Auth state change error: {ex.Message}");
            }
            finally
            {
                _isNotifying = false;
            }
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            try
            {
                // Check guest mode first
                if (!_isGuestMode)
                {
                    try
                    {
                        var guestFlag = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", GuestModeKey);
                        _isGuestMode = guestFlag == "true";
                    }
                    catch { }
                }

                if (_isGuestMode)
                {
                    var guestClaims = new List<Claim>
                    {
                        new Claim(ClaimTypes.NameIdentifier, "guest"),
                        new Claim(ClaimTypes.Email, "Guest User"),
                        new Claim(ClaimTypes.Role, "Guest")
                    };
                    var guestIdentity = new ClaimsIdentity(guestClaims, "Guest");
                    return new AuthenticationState(new ClaimsPrincipal(guestIdentity));
                }

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
                            catch
                            {
                                // SetSession failed — try explicit refresh with the refresh token
                                try
                                {
                                    session = await _client.Auth.RefreshSession();
                                    user = session?.User;
                                    if (session != null && !string.IsNullOrEmpty(session.AccessToken))
                                        await SaveTokensAsync(session.AccessToken!, session.RefreshToken!);
                                }
                                catch
                                {
                                    // Both attempts failed — tokens are truly expired
                                    Console.WriteLine("Session restore failed: tokens expired");
                                    await ClearTokensAsync();
                                }
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

                // Check is_admin from user_profiles table (cached per user)
                try
                {
                    if (Guid.TryParse(user.Id, out var userGuid))
                    {
                        bool isAdmin;
                        if (_cachedAdminUserId == user.Id)
                        {
                            isAdmin = _cachedIsAdmin;
                        }
                        else
                        {
                            var profileResp = await _client.From<InventoryPlus.Models.UserProfile>()
                                .Where(p => p.Guid == userGuid)
                                .Single();
                            isAdmin = profileResp?.IsAdmin == true;
                            _cachedAdminUserId = user.Id;
                            _cachedIsAdmin = isAdmin;
                        }
                        if (isAdmin)
                            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
                    }
                }
                catch { }

                var identity = new ClaimsIdentity(claims, "Supabase");
                return new AuthenticationState(new ClaimsPrincipal(identity));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetAuthenticationStateAsync fatal error: {ex.Message}");
                return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
            }
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
            _cachedAdminUserId = null;
            _cachedIsAdmin = false;
            try
            {
                await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", AccessTokenKey);
                await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", RefreshTokenKey);
            }
            catch { }
        }

        public async Task EnableGuestModeAsync()
        {
            _isGuestMode = true;
            try { await _jsRuntime.InvokeVoidAsync("localStorage.setItem", GuestModeKey, "true"); }
            catch { }
            NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        }

        public async Task DisableGuestModeAsync()
        {
            _isGuestMode = false;
            try { await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", GuestModeKey); }
            catch { }
            NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        }

        public void Dispose()
        {
            if (_client?.Auth != null)
                _client.Auth.RemoveStateChangedListener(OnAuthStateChanged);
        }
    }
}

