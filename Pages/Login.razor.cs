using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.JSInterop;
using InventoryPlus.Services;
using InventoryPlus.Models;

namespace InventoryPlus.Pages
{
    public partial class Login : ComponentBase
    {
        [Inject] public Supabase.Client Supabase { get; set; } = default!;
        [Inject] public NavigationManager NavigationManager { get; set; } = default!;
        [Inject] public SettingsService AppSettings { get; set; } = default!;
        [Inject] public IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] public AuthenticationStateProvider AuthStateProvider { get; set; } = default!;

        protected string Email { get; set; } = string.Empty;
        protected string Password { get; set; } = string.Empty;
        protected string ErrorMessage { get; set; } = string.Empty;
        protected string SuccessMessage { get; set; } = string.Empty;
        protected bool IsLoading { get; set; } = false;
        protected bool RememberMe { get; set; } = false;

        protected async Task HandleKeyDown(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e)
        {
            if (e.Key == "Enter" && !IsLoading)
                await HandleLogin();
        }

        protected override async Task OnInitializedAsync()
        {
            var uri = new Uri(NavigationManager.Uri);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            if (query["expired"] == "true")
                ErrorMessage = "Your session has expired. Please log in again.";

            var successMsg = query["success"];
            if (!string.IsNullOrEmpty(successMsg))
                SuccessMessage = successMsg;

            // Handle Supabase auth redirect errors (e.g. expired email confirmation link)
            var authError = query["auth_error"];
            if (!string.IsNullOrEmpty(authError))
            {
                var desc = query["auth_error_desc"] ?? "";
                ErrorMessage = authError switch
                {
                    "otp_expired" => "Your email confirmation link has expired. Please ask your administrator to resend the invite.",
                    "access_denied" => !string.IsNullOrEmpty(desc) ? desc.Replace('+', ' ') : "Access denied. Please try again or contact your administrator.",
                    _ => !string.IsNullOrEmpty(desc) ? desc.Replace('+', ' ') : "An authentication error occurred. Please try again."
                };
            }

            try
            {
                var remembered = await JSRuntime.InvokeAsync<string?>("localStorage.getItem", "sb_remembered_email");
                if (!string.IsNullOrEmpty(remembered))
                {
                    Email = remembered;
                    RememberMe = true;
                }
            }
            catch { }
        }

        protected async Task HandleLogin()
        {
            IsLoading = true;
            ErrorMessage = string.Empty;

            try
            {
                var loginInput = Email.Trim();

                // If input doesn't look like an email, resolve username to email
                if (!loginInput.Contains('@'))
                {
                    try
                    {
                        var result = await Supabase.Rpc("get_email_by_username", new Dictionary<string, object> { { "username_input", loginInput } });
                        var resolvedEmail = result?.Content?.Trim('"');
                        if (!string.IsNullOrEmpty(resolvedEmail) && resolvedEmail != "null")
                            loginInput = resolvedEmail;
                        else
                        {
                            ErrorMessage = "No account found with that username.";
                            return;
                        }
                    }
                    catch
                    {
                        ErrorMessage = "Unable to look up username. Try using your email instead.";
                        return;
                    }
                }

                var session = await Supabase.Auth.SignIn(loginInput, Password);
                if (session != null && session.User != null)
                {
                    // Save/clear remembered login
                    if (RememberMe)
                        await JSRuntime.InvokeVoidAsync("localStorage.setItem", "sb_remembered_email", loginInput);
                    else
                        await JSRuntime.InvokeVoidAsync("localStorage.removeItem", "sb_remembered_email");

                    // Check account status
                    if (Guid.TryParse(session.User.Id, out var userId))
                    {
                        var profileResp = await Supabase.From<UserProfile>()
                            .Where(p => p.Guid == userId).Get();
                        var profile = profileResp.Models.FirstOrDefault();
                        if (profile != null)
                        {
                            if (!profile.IsActive)
                            {
                                await Supabase.Auth.SignOut();
                                ErrorMessage = "Your account has been deactivated. Please contact an administrator.";
                                return;
                            }

                            // Update last sign-in timestamp
                            try
                            {
                                await Supabase.Rpc("update_last_sign_in", new Dictionary<string, object>
                                {
                                    { "user_id", userId.ToString() }
                                });
                            }
                            catch (Exception signInEx)
                            {
                                Console.WriteLine($"Failed to update last_sign_in_at: {signInEx.Message}");
                            }
                        }
                    }
                    NavigationManager.NavigateTo("dashboard");
                }
                else
                {
                    ErrorMessage = "Invalid email or password.";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = ParseLoginError(ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private static string ParseLoginError(string message)
        {
            var msg = message.ToLowerInvariant();

            if (msg.Contains("email_not_confirmed") || msg.Contains("email not confirmed"))
                return "Your email address has not been verified. Please check your email for a confirmation link before signing in.";

            if (msg.Contains("invalid") || msg.Contains("credentials") || msg.Contains("invalid_credentials"))
                return "Invalid email or password.";

            if (msg.Contains("user_not_found") || msg.Contains("no user found"))
                return "No account found with that email address.";

            if (msg.Contains("too_many_requests") || msg.Contains("rate limit") || msg.Contains("429"))
                return "Too many login attempts. Please wait a moment and try again.";

            if (msg.Contains("user_banned") || msg.Contains("banned"))
                return "Your account has been suspended. Please contact an administrator.";

            if (msg.Contains("network") || msg.Contains("fetch") || msg.Contains("timeout"))
                return "Unable to connect to the server. Please check your internet connection and try again.";

            if (msg.Contains("server") || msg.Contains("500") || msg.Contains("internal"))
                return "A server error occurred. Please try again later.";

            return "Login failed. Please check your credentials and try again.";
        }
    }
}

