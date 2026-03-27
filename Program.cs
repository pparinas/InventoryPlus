using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using InventoryPlus;
using Microsoft.AspNetCore.Components.Authorization;
using Supabase;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var supabaseUrl = builder.Configuration["Supabase:Url"];
if (string.IsNullOrEmpty(supabaseUrl) || supabaseUrl.StartsWith("__")) 
{
    supabaseUrl = "https://boixagidpfuyemzvmeva.supabase.co";
}

var supabaseKey = builder.Configuration["Supabase:Key"];
if (string.IsNullOrEmpty(supabaseKey) || supabaseKey.StartsWith("__"))
{
    supabaseKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImJvaXhhZ2lkcGZ1eWVtenZtZXZhIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NzM4NDg3NjAsImV4cCI6MjA4OTQyNDc2MH0.HemZ7yVnkcceTDNPrAaFlUm6ktRaifZuw7APO70uHm4";
}

var options = new SupabaseOptions
{
    AutoRefreshToken = false,
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
