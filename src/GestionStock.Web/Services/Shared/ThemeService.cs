using System.Threading;
using Blazored.LocalStorage;
using Microsoft.JSInterop;

namespace GestionStock.Web.Services;

public interface IThemeService
{
    string Template { get; }
    Task EnsureLoadedAsync();
    Task RefreshAsync();
}

public sealed class ThemeService : IThemeService
{
    private readonly IApiService _api;
    private readonly ILocalStorageService _storage;
    private readonly IJSRuntime _js;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private bool _loaded;
    private string? _lastToken;

    public string Template { get; private set; } = "STANDARD";

    public ThemeService(IApiService api, ILocalStorageService storage, IJSRuntime js)
    {
        _api = api;
        _storage = storage;
        _js = js;
    }

    public Task EnsureLoadedAsync() => LoadAsync(force: false);

    public Task RefreshAsync() => LoadAsync(force: true);

    private async Task LoadAsync(bool force)
    {
        var token = await GetTokenAsync();
        if (!force && _loaded && string.Equals(_lastToken, token, StringComparison.Ordinal))
            return;

        await _gate.WaitAsync();
        try
        {
            token = await GetTokenAsync();
            if (!force && _loaded && string.Equals(_lastToken, token, StringComparison.Ordinal))
                return;

            var parametres = await _api.GetParametresAsync();
            Template = NormalizeTemplate(parametres?.GabaritInterface);
            await ApplySafelyAsync();
            _lastToken = token;
            _loaded = true;
        }
        catch
        {
            Template = "STANDARD";
            await ApplySafelyAsync();
            _lastToken = token;
            _loaded = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string?> GetTokenAsync()
    {
        try
        {
            return await _storage.GetItemAsStringAsync("authToken");
        }
        catch
        {
            return null;
        }
    }

    private async Task ApplySafelyAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("applyGestionStockTheme", Template.ToLowerInvariant());
        }
        catch
        {
            // Ne pas bloquer l'application si le hook JS n'est pas encore disponible.
        }
    }

    private static string NormalizeTemplate(string? template)
        => string.Equals(template, "MARBRE_BLEU", StringComparison.OrdinalIgnoreCase)
            ? "MARBRE_BLEU"
            : "STANDARD";
}
