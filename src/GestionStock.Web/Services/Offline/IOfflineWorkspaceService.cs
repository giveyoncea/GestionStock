using GestionStock.Web.Models;

namespace GestionStock.Web.Services;

public interface IOfflineWorkspaceService
{
    Task<IReadOnlyList<SyncQueueItemLocal>> GetQueueAsync();
    Task<IReadOnlyList<SyncConflictLocal>> GetConflictsAsync();
    Task SaveQueueAsync(IReadOnlyList<SyncQueueItemLocal> items);
    Task SaveConflictsAsync(IReadOnlyList<SyncConflictLocal> items);
    Task EnqueueAsync(OfflineSyncOperationDto operation);
    Task ClearConflictsAsync();
    Task ClearQueueAsync();
}
