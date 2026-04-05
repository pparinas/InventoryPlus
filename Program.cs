using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using InventoryPlus;
using Microsoft.AspNetCore.Components.Authorization;
using Supabase;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var supabaseUrl = builder.Configuration["Supabase:Url"]
    ?? throw new InvalidOperationException("Supabase:Url is not configured.");
var supabaseKey = builder.Configuration["Supabase:Key"]
    ?? throw new InvalidOperationException("Supabase:Key is not configured.");

var options = new SupabaseOptions
{
    AutoRefreshToken = true,
    AutoConnectRealtime = false,
};

var supabaseClient = new Supabase.Client(supabaseUrl, supabaseKey, options);
await supabaseClient.InitializeAsync();
builder.Services.AddSingleton(supabaseClient);
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthenticationStateProvider, InventoryPlus.Services.SupabaseAuthenticationStateProvider>();

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddSingleton<InventoryPlus.Services.InventoryService>();
builder.Services.AddSingleton<InventoryPlus.Services.SettingsService>();
builder.Services.AddSingleton<InventoryPlus.Services.UserManagementService>();
builder.Services.AddSingleton<InventoryPlus.Services.InviteTokenService>();
builder.Services.AddSingleton<InventoryPlus.Services.ToastService>();

await builder.Build().RunAsync();
