using GestionStock.Application.Interfaces;
using System.Security.Claims;

namespace GestionStock.API.Services;

public class CommercialConnectionStringProvider : ICommercialConnectionStringProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ITenantService _tenantService;
    private readonly IConfiguration _configuration;

    public CommercialConnectionStringProvider(
        IHttpContextAccessor httpContextAccessor,
        ITenantService tenantService,
        IConfiguration configuration)
    {
        _httpContextAccessor = httpContextAccessor;
        _tenantService = tenantService;
        _configuration = configuration;
    }

    public string GetCurrentConnectionString()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var tenant = user?.FindFirstValue("tenant");
        return !string.IsNullOrWhiteSpace(tenant)
            ? _tenantService.GetConnectionString(tenant)
            : _configuration.GetConnectionString("DefaultConnection")!;
    }
}
