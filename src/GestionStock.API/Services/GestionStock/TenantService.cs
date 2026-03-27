using Microsoft.Data.SqlClient;
using System.Security.Claims;

namespace GestionStock.API.Services;

public interface ITenantService
{
    string GetConnectionString(string tenantCode);
}

public class TenantService : ITenantService
{
    private readonly IConfiguration _config;
    public TenantService(IConfiguration config) => _config = config;

    public string GetConnectionString(string tenantCode)
    {
        var baseConn = _config.GetConnectionString("SqlServerBase")!;
        var dbName = $"GestionStock_{tenantCode.ToUpper()}";
        return $"{baseConn};Database={dbName}";
    }
}

public static class TenantClaimExtensions
{
    public const string TenantClaim = "tenant";
    public static string? GetTenantCode(this ClaimsPrincipal user)
        => user.FindFirstValue(TenantClaim);
}
