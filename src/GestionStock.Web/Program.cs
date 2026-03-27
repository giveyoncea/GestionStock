using Blazored.LocalStorage;
using GestionStock.Web;
using GestionStock.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
// HTTP client
// HostEnvironment.BaseAddress = URL absolue du serveur qui héberge l'application
// Puisque l'API et le frontend sont sur le même serveur, cette URL est la bonne
// Puisque l'API et le frontend sont sur le mÃªme serveur, c'est l'URL correcte
builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
});
// Stockage local et authentification
// â”€â”€â”€ STORAGE & AUTH â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
builder.Services.AddBlazoredLocalStorage();
builder.Services.AddAuthorizationCore();

builder.Services.AddScoped<JwtAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<JwtAuthStateProvider>());

builder.Services.AddScoped<IAuthService, AuthService>();
// Services métier
// â”€â”€â”€ SERVICES MÃ‰TIER â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
builder.Services.AddScoped<IApiService, ApiService>();
builder.Services.AddScoped<ICurrencyService, CurrencyService>();
builder.Services.AddScoped<IThemeService, ThemeService>();
builder.Services.AddScoped<IPrintService, PrintService>();
builder.Services.AddScoped<ILocalDbService, LocalDbService>();
builder.Services.AddScoped<IOfflineSyncService, OfflineSyncService>();
builder.Services.AddScoped<IOfflineSyncStateService, OfflineSyncStateService>();
builder.Services.AddScoped<IOfflineWorkspaceService, OfflineWorkspaceService>();

await builder.Build().RunAsync();




