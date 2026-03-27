using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Blazored.LocalStorage;
using GestionStock.Web.Models;
using Microsoft.AspNetCore.Components.Authorization;

namespace GestionStock.Web.Services;

// ─── INTERFACE ────────────────────────────────────────────────────────────────
public interface IAuthService
{
    Task<AuthResult?> LoginAsync(string email, string motDePasse);
    Task<AuthResult?> LoginTenantAsync(string email, string motDePasse, string tenantCode);
    Task LogoutAsync();
}

// ─── IMPLÉMENTATION ───────────────────────────────────────────────────────────
public class AuthService : IAuthService
{
    private readonly HttpClient _http;
    private readonly ILocalStorageService _storage;
    private readonly JwtAuthStateProvider _authState;

    public AuthService(HttpClient http, ILocalStorageService storage,
        AuthenticationStateProvider authState)
    {
        _http = http;
        _storage = storage;
        // Résolution directe via le type concret enregistré
        _authState = (JwtAuthStateProvider)authState;
    }

    public async Task<AuthResult?> LoginAsync(string email, string motDePasse)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/auth/login",
                new LoginRequest(email, motDePasse));

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                var msg = response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    ? "Email ou mot de passe incorrect."
                    : $"Erreur serveur ({(int)response.StatusCode}). Consultez les logs de l'API.";
                return new AuthResult(false, null, null, null, null, email, null, null, msg);
            }

            var result = await response.Content.ReadFromJsonAsync<AuthResult>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result?.Token is not null)
            {
                await _storage.SetItemAsStringAsync("authToken",  result.Token);
                await _storage.SetItemAsStringAsync("userNom",    result.NomComplet ?? "");
                await _storage.SetItemAsStringAsync("userEmail",  result.Email ?? email);
                await _storage.SetItemAsStringAsync("userRole",   result.Role ?? "");
                await _storage.SetItemAsStringAsync("tenantCode", ""); // super admin: pas de tenant
                _authState.NotifyUserAuthenticated(result.Token);
            }

            return result;
        }
        catch (Exception ex)
        {
            return new AuthResult(false, null, null, null, null, email, null, null,
                $"Erreur de connexion : {ex.Message}");
        }
    }

    public async Task<AuthResult?> LoginTenantAsync(string email, string motDePasse, string tenantCode)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("api/auth/tenant/login",
                new { Email = email, MotDePasse = motDePasse, TenantCode = tenantCode });
            var result = await response.Content.ReadFromJsonAsync<TenantLoginResult>(
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (result?.Succes == true && !string.IsNullOrEmpty(result.Token))
            {
                await _storage.SetItemAsStringAsync("authToken",       result.Token);
                await _storage.SetItemAsStringAsync("tenantCode",      result.TenantCode ?? "");
                await _storage.SetItemAsStringAsync("userNom",         result.NomComplet ?? "");
                await _storage.SetItemAsStringAsync("userEmail",       result.Email ?? "");
                await _storage.SetItemAsStringAsync("userRole",        result.Role ?? "");
                await _storage.SetItemAsStringAsync("tenantDb",        result.BaseDeDonnees ?? "");
                await _storage.SetItemAsStringAsync("tenantCreatedAt", result.DateCreation?.ToString("dd/MM/yyyy") ?? "");
                _authState.NotifyUserAuthenticated(result.Token);
                return new AuthResult(true, result.Token, null, null, null,
                    result.Email, result.NomComplet, result.Role, result.Message);
            }
            return new AuthResult(false, null, null, null, null, null, null, null, result?.Message ?? "Erreur de connexion.");
        }
        catch (Exception ex)
        {
            return new AuthResult(false, null, null, null, null, null, null, null, ex.Message);
        }
    }

    public async Task LogoutAsync()
    {
        try
        {
            await _storage.RemoveItemAsync("authToken");
            await _storage.RemoveItemAsync("userNom");
            await _storage.RemoveItemAsync("userEmail");
            await _storage.RemoveItemAsync("userRole");
            await _storage.RemoveItemAsync("tenantCode");
            await _storage.RemoveItemAsync("tenantDb");
            await _storage.RemoveItemAsync("tenantCreatedAt");
        }
        catch { /* silencieux */ }
        _authState.NotifyUserLoggedOut();
    }
}

// ─── JWT AUTH STATE PROVIDER ──────────────────────────────────────────────────
public class TenantLoginResult
{
    public bool Succes { get; set; }
    public string? Token { get; set; }
    public string? TenantCode { get; set; }
    public string? BaseDeDonnees { get; set; }
    public DateTime? DateCreation { get; set; }
    public string? NomComplet { get; set; }
    public string? Role { get; set; }
    public string? Email { get; set; }
    public string? Message { get; set; }
}

public class JwtAuthStateProvider : AuthenticationStateProvider
{
    private readonly ILocalStorageService _storage;

    private static readonly AuthenticationState Anonyme =
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    public JwtAuthStateProvider(ILocalStorageService storage)
    {
        _storage = storage;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var token = await _storage.GetItemAsStringAsync("authToken");

            if (string.IsNullOrWhiteSpace(token))
                return Anonyme;

            var claims = ParseClaims(token);
            if (claims is null)
            {
                await _storage.RemoveItemAsync("authToken");
                return Anonyme;
            }

            // Vérifier l'expiration
            var expClaim = claims.FirstOrDefault(c => c.Type == "exp");
            if (expClaim is not null &&
                long.TryParse(expClaim.Value, out var expSeconds))
            {
                var expiry = DateTimeOffset.FromUnixTimeSeconds(expSeconds);
                if (expiry < DateTimeOffset.UtcNow)
                {
                    await _storage.RemoveItemAsync("authToken");
                    return Anonyme;
                }
            }

            // Vérifier que le token a les claims courts (sinon forcer re-login)
            var hasRoleClaim = claims.Any(c => c.Type == "role");
            var hasNameClaim = claims.Any(c => c.Type == "name");
            if (!hasRoleClaim || !hasNameClaim)
            {
                // Token ancien format - invalider
                await _storage.RemoveItemAsync("authToken");
                return Anonyme;
            }
            var identity = new ClaimsIdentity(claims, "jwt", "name", "role");
            return new AuthenticationState(new ClaimsPrincipal(identity));
        }
        catch
        {
            // Toute exception → anonyme (ne jamais crasher ici)
            return Anonyme;
        }
    }

    public void NotifyUserAuthenticated(string token)
    {
        try
        {
            var claims = ParseClaims(token);
            if (claims is null) return;
            var identity = new ClaimsIdentity(claims, "jwt", "name", "role");
            var user = new ClaimsPrincipal(identity);
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(user)));
        }
        catch { /* silencieux */ }
    }

    public void NotifyUserLoggedOut()
        => NotifyAuthenticationStateChanged(Task.FromResult(Anonyme));

    private static List<Claim>? ParseClaims(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3) return null;

            var payload = parts[1]
                .Replace('-', '+')
                .Replace('_', '/');

            // Padding Base64
            payload = (payload.Length % 4) switch
            {
                2 => payload + "==",
                3 => payload + "=",
                _ => payload
            };

            var jsonBytes = Convert.FromBase64String(payload);
            var kvps = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonBytes);
            if (kvps is null) return null;

            return kvps.Select(kvp => new Claim(
                kvp.Key,
                kvp.Value.ValueKind == JsonValueKind.String
                    ? kvp.Value.GetString() ?? string.Empty
                    : kvp.Value.ToString()
            )).ToList();
        }
        catch
        {
            return null;
        }
    }
}
