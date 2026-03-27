namespace GestionStock.Web.Services;

public class LocalDbService : ILocalDbService
{
    private const string ProviderName = "SQLite";

    public Task<bool> IsSupportedAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public Task<OfflineInitializationResult> InitializeAsync(string tenantCode, string? userId = null, CancellationToken cancellationToken = default)
    {
        var databaseName = BuildDatabaseName(tenantCode, userId);
        return Task.FromResult(new OfflineInitializationResult(
            Supported: false,
            Initialized: false,
            Provider: ProviderName,
            DatabaseName: databaseName,
            ScriptCount: OfflineDbSchema.GetCreateScripts().Count,
            Message: "Le schema offline SQLite est pret, mais le provider SQLite local n'est pas encore branche pour ce client Blazor WebAssembly."));
    }

    public IReadOnlyList<string> GetSchemaScripts()
    {
        return OfflineDbSchema.GetCreateScripts();
    }

    private static string BuildDatabaseName(string tenantCode, string? userId)
    {
        var normalizedTenant = string.IsNullOrWhiteSpace(tenantCode) ? "default" : tenantCode.Trim().ToLowerInvariant();
        var normalizedUser = string.IsNullOrWhiteSpace(userId) ? "anonymous" : userId.Trim().ToLowerInvariant();
        return $"gestionstock-offline-{normalizedTenant}-{normalizedUser}.db";
    }
}
