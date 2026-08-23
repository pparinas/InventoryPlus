using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace InventoryPlus.Pages
{
    /// <summary>
    /// Lands here after clicking a Supabase magic link (e.g. the admin "Login as user"
    /// link) -- Supabase's own /auth/v1/verify redirects here with the new session's
    /// tokens in the URL fragment (#access_token=...&amp;refresh_token=...).
    /// </summary>
    public partial class AuthCallback : ComponentBase
    {
        [Inject] public Supabase.Client SupabaseClient { get; set; } = default!;
        [Inject] public NavigationManager NavManager { get; set; } = default!;
        [Inject] public IJSRuntime JSRuntime { get; set; } = default!;

        protected string ErrorMessage { get; set; } = string.Empty;

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender) return;

            try
            {
                var hash = await JSRuntime.InvokeAsync<HashParams>("authInterop.getHashParams");
                if (!string.IsNullOrEmpty(hash.Error))
                {
                    ErrorMessage = hash.Error.Replace('+', ' ');
                    return;
                }
                if (string.IsNullOrEmpty(hash.AccessToken) || string.IsNullOrEmpty(hash.RefreshToken))
                {
                    ErrorMessage = "This sign-in link is missing or invalid.";
                    return;
                }

                await SupabaseClient.Auth.SetSession(hash.AccessToken, hash.RefreshToken);
                await JSRuntime.InvokeVoidAsync("authInterop.clearHash");
                NavManager.NavigateTo("dashboard", forceLoad: false, replace: true);
            }
            catch (Exception ex)
            {
                ErrorMessage = "This sign-in link has expired or already been used.";
                Console.WriteLine($"AuthCallback error: {ex.Message}");
            }
        }

        private class HashParams
        {
            public string? AccessToken { get; set; }
            public string? RefreshToken { get; set; }
            public string? Error { get; set; }
        }
    }
}
