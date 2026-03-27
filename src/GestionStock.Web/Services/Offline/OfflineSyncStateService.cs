using GestionStock.Web.Models;

namespace GestionStock.Web.Services;

public class OfflineSyncStateService : IOfflineSyncStateService
{
    public OfflineSyncSummaryDto BuildSummary(OfflinePushResponseDto? response, int pendingCount = 0)
    {
        var results = response?.Results ?? new List<OfflineSyncOperationResultDto>();
        return new OfflineSyncSummaryDto
        {
            AppliedCount = results.Count(r => string.Equals(r.Status, OfflineSyncStates.Applied, StringComparison.OrdinalIgnoreCase)),
            ConflictCount = results.Count(r => string.Equals(r.Status, OfflineSyncStates.Conflict, StringComparison.OrdinalIgnoreCase)),
            RejectedCount = results.Count(r => string.Equals(r.Status, OfflineSyncStates.Rejected, StringComparison.OrdinalIgnoreCase)),
            PendingCount = pendingCount
        };
    }

    public IReadOnlyList<SyncConflictLocal> BuildConflicts(OfflinePushRequestDto request, OfflinePushResponseDto? response)
    {
        if (response?.Results is null || response.Results.Count == 0)
            return Array.Empty<SyncConflictLocal>();

        var payloadsByKey = request.Operations.ToDictionary(
            operation => BuildKey(operation.EntityType, operation.EntityLocalId, operation.OperationType),
            operation => operation.PayloadJson,
            StringComparer.OrdinalIgnoreCase);

        return response.Results
            .Where(result => string.Equals(result.Status, OfflineSyncStates.Conflict, StringComparison.OrdinalIgnoreCase))
            .Select(result => new SyncConflictLocal
            {
                EntityType = result.EntityType,
                EntityLocalId = result.EntityLocalId,
                ConflictType = result.Status,
                LocalPayloadJson = payloadsByKey.GetValueOrDefault(BuildKey(result.EntityType, result.EntityLocalId, result.OperationType)),
                ServerPayloadJson = null,
                ResolutionStatus = "Open",
                CreatedAtUtc = DateTime.UtcNow
            })
            .ToList();
    }

    public void ApplyResultsToQueue(IList<SyncQueueItemLocal> queueItems, OfflinePushResponseDto? response)
    {
        if (response?.Results is null || response.Results.Count == 0 || queueItems.Count == 0)
            return;

        var lookup = response.Results.ToDictionary(
            result => BuildKey(result.EntityType, result.EntityLocalId, result.OperationType),
            result => result,
            StringComparer.OrdinalIgnoreCase);

        foreach (var item in queueItems)
        {
            if (!lookup.TryGetValue(BuildKey(item.EntityType, item.EntityLocalId, item.OperationType), out var result))
                continue;

            item.Status = result.Status;
            item.LastError = result.Message;
            item.LastAttemptUtc = DateTime.UtcNow;
            if (!result.Success)
                item.RetryCount += 1;
        }
    }

    private static string BuildKey(string entityType, string entityLocalId, string operationType)
    {
        return $"{entityType}|{entityLocalId}|{operationType}";
    }
}
