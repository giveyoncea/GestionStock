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
public class CategoriesController : ControllerBase
{
    private readonly ITenantService _tenant;
    private readonly IConfiguration _config;
    private readonly ILogger<CategoriesController> _logger;
    private string ConnStr
    {
        get
        {
            var tenantCode = User.FindFirstValue("tenant");
            if (!string.IsNullOrEmpty(tenantCode))
                return _tenant.GetConnectionString(tenantCode);
            return _config.GetConnectionString("DefaultConnection")!;
        }
    }
    private string UserId => User.FindFirstValue("sub") ?? "system";

    public CategoriesController(ITenantService tenant, IConfiguration config, ILogger<CategoriesController> logger)
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
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();

            // Créer la table si elle n'existe pas encore
            await using (var init = new SqlCommand(@"
                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='CategoriesArticles' AND xtype='U')
                CREATE TABLE CategoriesArticles (
                    Id uniqueidentifier NOT NULL PRIMARY KEY DEFAULT NEWID(),
                    Code nvarchar(20) NOT NULL,
                    Libelle nvarchar(100) NOT NULL,
                    Description nvarchar(500) NULL,
                    Couleur nvarchar(10) NULL,
                    Ordre int NOT NULL DEFAULT 0,
                    EstActif bit NOT NULL DEFAULT 1,
                    CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
                    UpdatedAt datetime2 NULL,
                    CreatedBy nvarchar(450) NOT NULL DEFAULT '',
                    UpdatedBy nvarchar(450) NULL
                )", conn))
            {
                await init.ExecuteNonQueryAsync();
            }

            var where = actifSeulement ? "WHERE EstActif = 1" : "";
            await using var cmd = new SqlCommand(
                $"SELECT Id, Code, Libelle, Description, Couleur, Ordre, EstActif, CreatedAt FROM CategoriesArticles {where} ORDER BY Ordre ASC, Libelle ASC", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new {
                    Id = r.GetGuid(0), Code = r.GetString(1), Libelle = r.GetString(2),
                    Description = r.IsDBNull(3) ? null : r.GetString(3),
                    Couleur = r.IsDBNull(4) ? null : r.GetString(4),
                    Ordre = r.GetInt32(5), EstActif = r.GetBoolean(6),
                    CreatedAt = r.GetDateTime(7)
                });
        }
        catch (Exception ex) { _logger.LogError(ex, "GetAll Categories"); }
        return Ok(list);
    }

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Creer([FromBody] CategorieRequest dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.Code) || string.IsNullOrWhiteSpace(dto.Libelle))
                return BadRequest(new { succes = false, message = "Code et libellé obligatoires." });

            var code = dto.Code.Trim().ToUpperInvariant();
            if (!Regex.IsMatch(code, @"^[A-Z0-9_-]+$"))
                return BadRequest(new { succes = false, message = "Code invalide (lettres, chiffres, - et _ uniquement)." });

            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();

            await using (var chk = new SqlCommand("SELECT COUNT(1) FROM CategoriesArticles WHERE Code=@c AND EstActif=1", conn))
            {
                chk.Parameters.AddWithValue("@c", code);
                if (Convert.ToInt32(await chk.ExecuteScalarAsync() ?? 0) > 0)
                    return BadRequest(new { succes = false, message = "Ce code est déjà utilisé." });
            }

            var id = Guid.NewGuid();
            await using var cmd = new SqlCommand(@"
                INSERT INTO CategoriesArticles (Id, Code, Libelle, Description, Couleur, Ordre, EstActif, CreatedAt, CreatedBy)
                VALUES (@id, @code, @lib, @desc, @couleur, @ordre, 1, GETUTCDATE(), @user)", conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@code", code);
            cmd.Parameters.AddWithValue("@lib", dto.Libelle.Trim());
            cmd.Parameters.AddWithValue("@desc", (object?)dto.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@couleur", (object?)dto.Couleur ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ordre", dto.Ordre);
            cmd.Parameters.AddWithValue("@user", UserId);
            await cmd.ExecuteNonQueryAsync();

            return Ok(new { succes = true, message = "Catégorie créée.", id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur création catégorie");
            return StatusCode(500, new { succes = false, message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Modifier(Guid id, [FromBody] CategorieRequest dto)
    {
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(@"
                UPDATE CategoriesArticles SET Libelle=@lib, Description=@desc,
                Couleur=@couleur, Ordre=@ordre, UpdatedAt=GETUTCDATE(), UpdatedBy=@user
                WHERE Id=@id", conn);
            cmd.Parameters.AddWithValue("@lib", dto.Libelle.Trim());
            cmd.Parameters.AddWithValue("@desc", (object?)dto.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@couleur", (object?)dto.Couleur ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ordre", dto.Ordre);
            cmd.Parameters.AddWithValue("@user", UserId);
            cmd.Parameters.AddWithValue("@id", id);
            int rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0
                ? Ok(new { succes = true, message = "Catégorie modifiée." })
                : NotFound(new { succes = false, message = "Catégorie introuvable." });
        }
        catch (Exception ex) { return StatusCode(500, new { succes = false, message = ex.Message }); }
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
                "UPDATE CategoriesArticles SET EstActif=0, UpdatedAt=GETUTCDATE() WHERE Id=@id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            int rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0
                ? Ok(new { succes = true, message = "Catégorie désactivée." })
                : NotFound(new { succes = false, message = "Catégorie introuvable." });
        }
        catch (Exception ex) { return StatusCode(500, new { succes = false, message = ex.Message }); }
    }
}

public class CategorieRequest
{
    public string Code { get; set; } = string.Empty;
    public string Libelle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Couleur { get; set; }
    public int Ordre { get; set; }
}
