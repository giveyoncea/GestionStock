using GestionStock.Web.Models;

namespace GestionStock.Web.Services;

public interface IOfflineSyncStateService
{
    OfflineSyncSummaryDto BuildSummary(OfflinePushResponseDto? response, int pendingCount = 0);
    IReadOnlyList<SyncConflictLocal> BuildConflicts(OfflinePushRequestDto request, OfflinePushResponseDto? response);
    void ApplyResultsToQueue(IList<SyncQueueItemLocal> queueItems, OfflinePushResponseDto? response);
}
