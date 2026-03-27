namespace GestionStock.Web.Services;

public interface ILocalDbService
{
    Task<bool> IsSupportedAsync(CancellationToken cancellationToken = default);
    Task<OfflineInitializationResult> InitializeAsync(string tenantCode, string? userId = null, CancellationToken cancellationToken = default);
    IReadOnlyList<string> GetSchemaScripts();
}

public sealed record OfflineInitializationResult(
    bool Supported,
    bool Initialized,
    string Provider,
    string DatabaseName,
    int ScriptCount,
    string? Message);
