using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;
using Supabase.Interfaces;

namespace InventoryPlus.Services
{
    public class SupabaseAuthenticationStateProvider : AuthenticationStateProvider, IDisposable
    {
        private readonly Supabase.Client _client;

        public SupabaseAuthenticationStateProvider(Supabase.Client client)
        {
            _client = client;
            _client.Auth.AddStateChangedListener(OnAuthStateChanged);
        }

        private void OnAuthStateChanged(IGotrueClient<User, Session> sender, Supabase.Gotrue.Constants.AuthState e)
        {
            NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        }

        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var session = _client.Auth.CurrentSession;
            var user = _client.Auth.CurrentUser;

            if (session == null || user == null)
            {
                return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id ?? string.Empty),
                new Claim(ClaimTypes.Email, user.Email ?? string.Empty)
            };

            if (user.Email?.ToLower() == "pparinas@ucpm.com")
            {
                claims.Add(new Claim(ClaimTypes.Role, "Admin"));
            }

            var identity = new ClaimsIdentity(claims, "Supabase");
            var principal = new ClaimsPrincipal(identity);

            return Task.FromResult(new AuthenticationState(principal));
        }

        public void Dispose()
        {
            if (_client?.Auth != null)
            {
                _client.Auth.RemoveStateChangedListener(OnAuthStateChanged);
            }
        }
    }
}
