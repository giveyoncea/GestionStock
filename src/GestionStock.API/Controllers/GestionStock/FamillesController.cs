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
public class FamillesController : ControllerBase
{
    private readonly ITenantService _tenant;
    private readonly IConfiguration _config;
    private readonly ILogger<FamillesController> _logger;
    private string ConnStr { get {
        var t = User.FindFirstValue("tenant");
        return !string.IsNullOrEmpty(t) ? _tenant.GetConnectionString(t) : _config.GetConnectionString("DefaultConnection")!;
    } }
    private string UserId => User.FindFirstValue("sub") ?? "system";

    public FamillesController(ITenantService tenant, IConfiguration config, ILogger<FamillesController> logger)
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
            var where = actifSeulement ? "WHERE f.EstActif = 1" : string.Empty;
            var sql = $@"SELECT f.Id, f.Code, f.Libelle, f.Description,
                         f.ParentId, p.Libelle AS ParentLibelle,
                         f.Couleur, f.Ordre, ISNULL(f.SansSuiviStock,0) AS SansSuiviStock, f.EstActif, f.CreatedAt
                         FROM FamillesArticles f
                         LEFT JOIN FamillesArticles p ON f.ParentId = p.Id
                         {where}
                         ORDER BY f.Ordre ASC, f.Libelle ASC";

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
                    ParentId = reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4),
                    ParentLibelle = reader.IsDBNull(5) ? null : reader.GetString(5),
                    Couleur = reader.IsDBNull(6) ? null : reader.GetString(6),
                    Ordre = reader.GetInt32(7),
                    SansSuiviStock = reader.GetBoolean(8),
                    EstActif = reader.GetBoolean(9),
                    CreatedAt = reader.GetDateTime(10)
                });
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "GetAll Familles"); }
        return Ok(list);
    }

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Creer([FromBody] FamilleRequest dto)
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

            await using (var chk = new SqlCommand("SELECT COUNT(1) FROM FamillesArticles WHERE Code=@c AND EstActif=1", conn))
            {
                chk.Parameters.AddWithValue("@c", code);
                if (Convert.ToInt32(await chk.ExecuteScalarAsync() ?? 0) > 0)
                    return BadRequest(new { succes = false, message = "Ce code est déjà utilisé par une famille active." });
            }

            var id = Guid.NewGuid();
            var sql = @"INSERT INTO FamillesArticles
                        (Id, Code, Libelle, Description, ParentId, Couleur, Ordre, SansSuiviStock, EstActif, CreatedAt, CreatedBy)
                        VALUES (@id,@code,@lib,@desc,@parent,@couleur,@ordre,@sansSuivi,1,GETUTCDATE(),@user)";

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@code", code);
            cmd.Parameters.AddWithValue("@lib", dto.Libelle.Trim());
            cmd.Parameters.AddWithValue("@desc", (object?)dto.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@parent", (object?)dto.ParentId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@couleur", (object?)dto.Couleur ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ordre", dto.Ordre);
            cmd.Parameters.AddWithValue("@sansSuivi", dto.SansSuiviStock);
            cmd.Parameters.AddWithValue("@user", UserId);

            await cmd.ExecuteNonQueryAsync();
            return Ok(new { succes = true, message = "Famille créée avec succès.", id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur création famille");
            return StatusCode(500, new { succes = false, message = $"Erreur: {ex.InnerException?.Message ?? ex.Message}" });
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Modifier(Guid id, [FromBody] FamilleRequest dto)
    {
        try
        {
            if (dto.ParentId == id)
                return BadRequest(new { succes = false, message = "Une famille ne peut pas être son propre parent." });

            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();

            var sql = @"UPDATE FamillesArticles SET
                        Libelle=@lib, Description=@desc, ParentId=@parent,
                        Couleur=@couleur, Ordre=@ordre, SansSuiviStock=@sansSuivi,
                        UpdatedAt=GETUTCDATE(), UpdatedBy=@user
                        WHERE Id=@id";

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@lib", dto.Libelle.Trim());
            cmd.Parameters.AddWithValue("@desc", (object?)dto.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@parent", (object?)dto.ParentId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@couleur", (object?)dto.Couleur ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ordre", dto.Ordre);
            cmd.Parameters.AddWithValue("@sansSuivi", dto.SansSuiviStock);
            cmd.Parameters.AddWithValue("@user", UserId);
            cmd.Parameters.AddWithValue("@id", id);

            int rows = await cmd.ExecuteNonQueryAsync();
            if (rows == 0) return NotFound(new { succes = false, message = "Famille introuvable." });
            return Ok(new { succes = true, message = "Famille modifiée avec succès." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur modification famille {Id}", id);
            return StatusCode(500, new { succes = false, message = $"Erreur: {ex.InnerException?.Message ?? ex.Message}" });
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

            await using (var chk = new SqlCommand("SELECT COUNT(1) FROM FamillesArticles WHERE ParentId=@id AND EstActif=1", conn))
            {
                chk.Parameters.AddWithValue("@id", id);
                if (Convert.ToInt32(await chk.ExecuteScalarAsync() ?? 0) > 0)
                    return BadRequest(new { succes = false, message = "Désactivez d'abord les sous-familles." });
            }

            await using var cmd = new SqlCommand(
                "UPDATE FamillesArticles SET EstActif=0, UpdatedAt=GETUTCDATE() WHERE Id=@id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            int rows = await cmd.ExecuteNonQueryAsync();
            if (rows == 0) return NotFound(new { succes = false, message = "Famille introuvable." });
            return Ok(new { succes = true, message = "Famille désactivée." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { succes = false, message = ex.Message });
        }
    }
}

public class FamilleRequest
{
    public string Code { get; set; } = string.Empty;
    public string Libelle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? ParentId { get; set; }
    public string? Couleur { get; set; }
    public int Ordre { get; set; }
    public bool SansSuiviStock { get; set; }
}
