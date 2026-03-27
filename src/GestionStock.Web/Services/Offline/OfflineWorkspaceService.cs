using Blazored.LocalStorage;
using GestionStock.Web.Models;

namespace GestionStock.Web.Services;

public class OfflineWorkspaceService : IOfflineWorkspaceService
{
    private const string QueueKey = "offline.sync.queue";
    private const string ConflictsKey = "offline.sync.conflicts";

    private readonly ILocalStorageService _storage;

    public OfflineWorkspaceService(ILocalStorageService storage)
    {
        _storage = storage;
    }

    public async Task<IReadOnlyList<SyncQueueItemLocal>> GetQueueAsync()
    {
        var items = await _storage.GetItemAsync<List<SyncQueueItemLocal>>(QueueKey);
        return items ?? new List<SyncQueueItemLocal>();
    }

    public async Task<IReadOnlyList<SyncConflictLocal>> GetConflictsAsync()
    {
        var items = await _storage.GetItemAsync<List<SyncConflictLocal>>(ConflictsKey);
        return items ?? new List<SyncConflictLocal>();
    }

    public async Task SaveQueueAsync(IReadOnlyList<SyncQueueItemLocal> items)
    {
        await _storage.SetItemAsync(QueueKey, items.ToList());
    }

    public async Task SaveConflictsAsync(IReadOnlyList<SyncConflictLocal> items)
    {
        await _storage.SetItemAsync(ConflictsKey, items.ToList());
    }

    public async Task EnqueueAsync(OfflineSyncOperationDto operation)
    {
        var queue = (await GetQueueAsync()).ToList();
        var nextId = queue.Count == 0 ? 1 : queue.Max(x => x.Id) + 1;

        queue.Add(new SyncQueueItemLocal
        {
            Id = nextId,
            EntityType = operation.EntityType,
            EntityLocalId = operation.EntityLocalId,
            OperationType = operation.OperationType,
            PayloadJson = operation.PayloadJson,
            Status = OfflineSyncStates.Pending,
            RetryCount = 0,
            CreatedAtUtc = operation.CreatedAtUtc,
            LastAttemptUtc = null,
            LastError = null
        });

        await SaveQueueAsync(queue);
    }

    public async Task ClearConflictsAsync()
    {
        await _storage.RemoveItemAsync(ConflictsKey);
    }

    public async Task ClearQueueAsync()
    {
        await _storage.RemoveItemAsync(QueueKey);
    }
}
