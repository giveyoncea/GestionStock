using System.Globalization;
using System.Net.Http.Json;
using GestionStock.Web.Models;

namespace GestionStock.Web.Services;

public class OfflineSyncService : IOfflineSyncService
{
    private readonly HttpClient _http;

    public OfflineSyncService(HttpClient http)
    {
        _http = http;
    }

    public async Task<OfflineBootstrapResponseDto?> GetBootstrapAsync(DateTime? sinceUtc = null, CancellationToken cancellationToken = default)
    {
        var url = "api/commercial/sync/bootstrap";
        if (sinceUtc.HasValue)
        {
            var iso = Uri.EscapeDataString(sinceUtc.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            url += $"?sinceUtc={iso}";
        }

        return await _http.GetFromJsonAsync<OfflineBootstrapResponseDto>(url, cancellationToken);
    }

    public async Task<OfflinePushResponseDto?> PushAsync(OfflinePushRequestDto request, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsJsonAsync("api/commercial/sync/push", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OfflinePushResponseDto>(cancellationToken: cancellationToken);
    }
}
