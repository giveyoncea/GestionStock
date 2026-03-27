using System.Net.Http.Json;
using GestionStock.Web.Models;

namespace GestionStock.Web.Services;

public interface IOfflineSyncService
{
    Task<OfflineBootstrapResponseDto?> GetBootstrapAsync(DateTime? sinceUtc = null, CancellationToken cancellationToken = default);
    Task<OfflinePushResponseDto?> PushAsync(OfflinePushRequestDto request, CancellationToken cancellationToken = default);
}
