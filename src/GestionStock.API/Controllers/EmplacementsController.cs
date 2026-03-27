using GestionStock.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Security.Claims;

namespace GestionStock.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class EmplacementsController : ControllerBase
{
    private readonly ITenantService _tenant;
    private readonly IConfiguration _config;

    private string ConnStr
    {
        get
        {
            var t = User.FindFirstValue("tenant");
            return !string.IsNullOrEmpty(t)
                ? _tenant.GetConnectionString(t)
                : _config.GetConnectionString("DefaultConnection")!;
        }
    }

    public EmplacementsController(ITenantService tenant, IConfiguration config)
    {
        _tenant = tenant;
        _config = config;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var list = new List<object>();
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                @"SELECT Id, Code, Zone FROM Emplacements WHERE EstActif = 1
                  ORDER BY Zone, Code", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new { Id = r.GetGuid(0), Code = r.GetString(1), Zone = r.IsDBNull(2) ? "" : r.GetString(2) });
        }
        catch { /* retourne liste vide si table absente */ }
        return Ok(list);
    }
}
