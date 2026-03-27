using GestionStock.API.Services;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;

namespace GestionStock.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DepotsController : ControllerBase
{
    private readonly ITenantService _tenant;
    private readonly IConfiguration _config;
    private readonly ILogger<DepotsController> _logger;
    private string ConnStr { get {
        var t = User.FindFirstValue("tenant");
        return !string.IsNullOrEmpty(t) ? _tenant.GetConnectionString(t) : _config.GetConnectionString("DefaultConnection")!;
    } }
    private string UserId => User.FindFirstValue("sub") ?? "system";

    public DepotsController(ITenantService tenant, IConfiguration config, ILogger<DepotsController> logger)
    {
        _tenant = tenant;
        _config = config;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool actifSeulement = true)
    {
        var list = new List<object>();
        try
        {
            var where = actifSeulement ? "WHERE EstActif = 1" : string.Empty;
            var sql = $@"SELECT Id, Code, Libelle, Description, Adresse, CodePostal,
                         Ville, Pays, Responsable, Telephone, SurfaceM2, CapacitePalettes,
                         EstPrincipal, EstActif, TypeDepot, CreatedAt
                         FROM Depots {where}
                         ORDER BY EstPrincipal DESC, Libelle ASC";

            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new
                {
                    Id = reader.GetGuid(0),
                    Code = reader.GetString(1),
                    Libelle = reader.GetString(2),
                    Description = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Adresse = reader.GetString(4),
                    CodePostal = reader.GetString(5),
                    Ville = reader.GetString(6),
                    Pays = reader.GetString(7),
                    Responsable = reader.IsDBNull(8) ? null : reader.GetString(8),
                    Telephone = reader.IsDBNull(9) ? null : reader.GetString(9),
                    SurfaceM2 = reader.GetDecimal(10),
                    CapacitePalettes = reader.GetInt32(11),
                    EstPrincipal = reader.GetBoolean(12),
                    EstActif = reader.GetBoolean(13),
                    TypeDepot = reader.GetInt32(14),
                    CreatedAt = reader.GetDateTime(15)
                });
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "GetAll Depots"); }
        return Ok(list);
    }

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Creer([FromBody] DepotRequest dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.Code) || string.IsNullOrWhiteSpace(dto.Libelle))
                return BadRequest(new { succes = false, message = "Code et libellé obligatoires." });

            var code = dto.Code.Trim().ToUpperInvariant();
            if (!Regex.IsMatch(code, @"^[A-Z0-9_-]+$"))
                return BadRequest(new { succes = false, message = "Le code ne peut contenir que des lettres, chiffres, - et _." });

            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();

            // Vérifier unicité
            await using (var chk = new SqlCommand("SELECT COUNT(1) FROM Depots WHERE Code=@c AND EstActif=1", conn))
            {
                chk.Parameters.AddWithValue("@c", code);
                var count = Convert.ToInt32(await chk.ExecuteScalarAsync() ?? 0);
                if (count > 0)
                    return BadRequest(new { succes = false, message = "Ce code est déjà utilisé par un dépôt actif." });
            }

            // Retirer le principal actuel si nécessaire
            if (dto.EstPrincipal)
            {
                await using var upd = new SqlCommand("UPDATE Depots SET EstPrincipal=0 WHERE EstPrincipal=1", conn);
                await upd.ExecuteNonQueryAsync();
            }

            var id = Guid.NewGuid();
            var sql = @"INSERT INTO Depots (Id, Code, Libelle, Description, Adresse, CodePostal,
                        Ville, Pays, Responsable, Telephone, SurfaceM2, CapacitePalettes,
                        EstPrincipal, EstActif, TypeDepot, CreatedAt, CreatedBy)
                        VALUES (@id,@code,@lib,@desc,@adr,@cp,@ville,@pays,@resp,@tel,
                                @surf,@cap,@princ,1,@type,GETUTCDATE(),@user)";

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@code", code);
            cmd.Parameters.AddWithValue("@lib", dto.Libelle.Trim());
            cmd.Parameters.AddWithValue("@desc", (object?)dto.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@adr", dto.Adresse ?? string.Empty);
            cmd.Parameters.AddWithValue("@cp", dto.CodePostal ?? string.Empty);
            cmd.Parameters.AddWithValue("@ville", dto.Ville ?? string.Empty);
            cmd.Parameters.AddWithValue("@pays", dto.Pays ?? "France");
            cmd.Parameters.AddWithValue("@resp", (object?)dto.Responsable ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@tel", (object?)dto.Telephone ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@surf", dto.SurfaceM2);
            cmd.Parameters.AddWithValue("@cap", dto.CapacitePalettes);
            cmd.Parameters.AddWithValue("@princ", dto.EstPrincipal);
            cmd.Parameters.AddWithValue("@type", dto.TypeDepot);
            cmd.Parameters.AddWithValue("@user", UserId);

            await cmd.ExecuteNonQueryAsync();
            return Ok(new { succes = true, message = "Dépôt créé avec succès.", id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur création dépôt");
            return StatusCode(500, new { succes = false, message = $"Erreur: {ex.InnerException?.Message ?? ex.Message}" });
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Modifier(Guid id, [FromBody] DepotRequest dto)
    {
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();

            await using (var chk = new SqlCommand("SELECT COUNT(1) FROM Depots WHERE Id=@id", conn))
            {
                chk.Parameters.AddWithValue("@id", id);
                if (Convert.ToInt32(await chk.ExecuteScalarAsync() ?? 0) == 0)
                    return NotFound(new { succes = false, message = "Dépôt introuvable." });
            }

            if (dto.EstPrincipal)
            {
                await using var upd = new SqlCommand("UPDATE Depots SET EstPrincipal=0 WHERE EstPrincipal=1 AND Id<>@id", conn);
                upd.Parameters.AddWithValue("@id", id);
                await upd.ExecuteNonQueryAsync();
            }

            var sql = @"UPDATE Depots SET Libelle=@lib, Description=@desc, Adresse=@adr,
                        CodePostal=@cp, Ville=@ville, Pays=@pays, Responsable=@resp,
                        Telephone=@tel, SurfaceM2=@surf, CapacitePalettes=@cap,
                        EstPrincipal=@princ, TypeDepot=@type, UpdatedAt=GETUTCDATE(), UpdatedBy=@user
                        WHERE Id=@id";

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@lib", dto.Libelle.Trim());
            cmd.Parameters.AddWithValue("@desc", (object?)dto.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@adr", dto.Adresse ?? string.Empty);
            cmd.Parameters.AddWithValue("@cp", dto.CodePostal ?? string.Empty);
            cmd.Parameters.AddWithValue("@ville", dto.Ville ?? string.Empty);
            cmd.Parameters.AddWithValue("@pays", dto.Pays ?? "France");
            cmd.Parameters.AddWithValue("@resp", (object?)dto.Responsable ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@tel", (object?)dto.Telephone ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@surf", dto.SurfaceM2);
            cmd.Parameters.AddWithValue("@cap", dto.CapacitePalettes);
            cmd.Parameters.AddWithValue("@princ", dto.EstPrincipal);
            cmd.Parameters.AddWithValue("@type", dto.TypeDepot);
            cmd.Parameters.AddWithValue("@user", UserId);
            cmd.Parameters.AddWithValue("@id", id);

            await cmd.ExecuteNonQueryAsync();
            return Ok(new { succes = true, message = "Dépôt modifié avec succès." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur modification dépôt {Id}", id);
            return StatusCode(500, new { succes = false, message = $"Erreur: {ex.InnerException?.Message ?? ex.Message}" });
        }
    }

    [HttpPost("{id:guid}/principal")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> DefinirPrincipal(Guid id)
    {
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            await using (var upd = new SqlCommand("UPDATE Depots SET EstPrincipal=0", conn))
                await upd.ExecuteNonQueryAsync();
            await using var cmd = new SqlCommand("UPDATE Depots SET EstPrincipal=1 WHERE Id=@id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            int rows = await cmd.ExecuteNonQueryAsync();
            if (rows == 0) return NotFound(new { succes = false, message = "Dépôt introuvable." });
            return Ok(new { succes = true, message = "Dépôt principal défini." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { succes = false, message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Desactiver(Guid id)
    {
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "UPDATE Depots SET EstActif=0, EstPrincipal=0, UpdatedAt=GETUTCDATE() WHERE Id=@id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            int rows = await cmd.ExecuteNonQueryAsync();
            if (rows == 0) return NotFound(new { succes = false, message = "Dépôt introuvable." });
            return Ok(new { succes = true, message = "Dépôt désactivé." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { succes = false, message = ex.Message });
        }
    }
}

public class DepotRequest
{
    public string Code { get; set; } = string.Empty;
    public string Libelle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Adresse { get; set; } = string.Empty;
    public string CodePostal { get; set; } = string.Empty;
    public string Ville { get; set; } = string.Empty;
    public string Pays { get; set; } = "France";
    public string? Responsable { get; set; }
    public string? Telephone { get; set; }
    public decimal SurfaceM2 { get; set; }
    public int CapacitePalettes { get; set; }
    public bool EstPrincipal { get; set; }
    public int TypeDepot { get; set; }
}
