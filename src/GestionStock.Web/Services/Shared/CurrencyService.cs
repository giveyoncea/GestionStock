using System.Globalization;
using System.Threading;
using Blazored.LocalStorage;

namespace GestionStock.Web.Services;

public interface ICurrencyService
{
    string Code { get; }
    string Symbol { get; }
    int AmountDecimals { get; }
    int QuantityDecimals { get; }
    string AmountStep { get; }
    string QuantityStep { get; }
    Task EnsureLoadedAsync();
    Task RefreshAsync();
    string FormatAmount(decimal value, int? decimals = null);
    string FormatQuantity(decimal value, int? decimals = null);
}

public sealed class CurrencyService : ICurrencyService
{
    private static readonly CultureInfo FrCulture = new("fr-FR");

    private readonly IApiService _api;
    private readonly ILocalStorageService _storage;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private bool _loaded;
    private string? _lastToken;

    public string Code { get; private set; } = "EUR";
    public string Symbol { get; private set; } = "EUR";
    public int AmountDecimals { get; private set; } = 2;
    public int QuantityDecimals { get; private set; } = 3;
    public string AmountStep => BuildStep(AmountDecimals);
    public string QuantityStep => BuildStep(QuantityDecimals);

    public CurrencyService(IApiService api, ILocalStorageService storage)
    {
        _api = api;
        _storage = storage;
    }

    public Task EnsureLoadedAsync() => LoadAsync(force: false);

    public Task RefreshAsync() => LoadAsync(force: true);

    public string FormatAmount(decimal value, int? decimals = null)
        => $"{value.ToString($"N{NormalizeDecimals(decimals ?? AmountDecimals)}", FrCulture)} {Symbol}";

    public string FormatQuantity(decimal value, int? decimals = null)
        => value.ToString($"N{NormalizeDecimals(decimals ?? QuantityDecimals)}", FrCulture);

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
            Code = NormalizeCode(parametres?.Devise);
            Symbol = NormalizeSymbol(parametres?.SymboleDevise, Code);
            AmountDecimals = NormalizeDecimals(parametres?.NombreDecimalesMontant ?? 2);
            QuantityDecimals = NormalizeDecimals(parametres?.NombreDecimalesQuantite ?? 3);
            _lastToken = token;
            _loaded = true;
        }
        catch
        {
            Code = "EUR";
            Symbol = "EUR";
            AmountDecimals = 2;
            QuantityDecimals = 3;
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

    private static string NormalizeCode(string? code)
        => string.IsNullOrWhiteSpace(code) ? "EUR" : code.Trim().ToUpperInvariant();

    private static string NormalizeSymbol(string? symbol, string code)
        => string.IsNullOrWhiteSpace(symbol) ? code : symbol.Trim();

    private static int NormalizeDecimals(int decimals)
        => Math.Clamp(decimals, 0, 6);

    private static string BuildStep(int decimals)
        => decimals <= 0 ? "1" : "0." + new string('0', decimals - 1) + "1";
}
