using GestionStock.API.Services;
using GestionStock.Application.DTOs;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Security.Claims;

namespace GestionStock.API.Controllers;

[ApiController]
[Route("api/fournisseurs")]
[Authorize]
[Tags("Fournisseurs")]
public class FournisseursAdoController : ControllerBase
{
    private readonly ITenantService _tenant;
    private readonly IConfiguration _config;
    private readonly IValidator<CreerFournisseurDto> _validator;

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
    private string UserId => User.FindFirstValue("sub") ?? "system";

    public FournisseursAdoController(ITenantService tenant, IConfiguration config,
        IValidator<CreerFournisseurDto> validator)
    {
        _tenant    = tenant;
        _config    = config;
        _validator = validator;
    }

    [HttpGet]
    [Authorize(Policy = "Lecteur")]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null)
    {
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();

            var where = "WHERE f.Statut <> 3";
            if (!string.IsNullOrWhiteSpace(search))
                where += " AND (f.Code LIKE @s OR f.RaisonSociale LIKE @s OR f.Email LIKE @s)";

            await using var countCmd = new SqlCommand(
                $"SELECT COUNT(1) FROM Fournisseurs f {where}", conn);
            if (!string.IsNullOrWhiteSpace(search))
                countCmd.Parameters.AddWithValue("@s", $"%{search}%");
            var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

            await using var cmd = new SqlCommand($@"
                SELECT f.Id, f.Code, f.RaisonSociale,
                       ISNULL(f.Email,'') AS Email,
                       ISNULL(f.Telephone,'') AS Telephone,
                       ISNULL(f.Adresse,'') AS Adresse,
                       ISNULL(f.Ville,'') AS Ville,
                       ISNULL(f.CodePostal,'') AS CodePostal,
                       ISNULL(f.Pays,'France') AS Pays,
                       ISNULL(f.DelaiLivraisonJours,0) AS DelaiLivraisonJours,
                       ISNULL(f.TauxRemise,0) AS TauxRemise,
                       ISNULL(f.Statut,1) AS Statut,
                       (SELECT COUNT(1) FROM CommandesAchat c WHERE c.FournisseurId=f.Id) AS NombreCommandes
                FROM Fournisseurs f
                {where}
                ORDER BY f.RaisonSociale
                OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY", conn);
            cmd.Parameters.AddWithValue("@skip", (page - 1) * pageSize);
            cmd.Parameters.AddWithValue("@take", pageSize);
            if (!string.IsNullOrWhiteSpace(search))
                cmd.Parameters.AddWithValue("@s", $"%{search}%");

            var items = new List<object>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                items.Add(new
                {
                    id                  = r.GetGuid(0),
                    code                = r.GetString(1),
                    raisonSociale       = r.GetString(2),
                    email               = r.GetString(3),
                    telephone           = r.GetString(4),
                    adresse             = r.GetString(5),
                    ville               = r.GetString(6),
                    codePostal          = r.GetString(7),
                    pays                = r.GetString(8),
                    delaiLivraisonJours = r.GetInt32(9),
                    tauxRemise          = r.GetDecimal(10),
                    statut              = r.GetInt32(11),
                    nombreCommandes     = r.GetInt32(12)
                });
            }
            return Ok(new { items, totalCount = total, page, pageSize,
                totalPages = (int)Math.Ceiling(total / (double)pageSize) });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { succes = false, message = ex.Message });
        }
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "Lecteur")]
    public async Task<IActionResult> GetById(Guid id)
    {
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(@"
                SELECT f.Id, f.Code, f.RaisonSociale,
                       ISNULL(f.Email,''), ISNULL(f.Telephone,''), ISNULL(f.Adresse,''),
                       ISNULL(f.Ville,''), ISNULL(f.CodePostal,''), ISNULL(f.Pays,'France'),
                       ISNULL(f.DelaiLivraisonJours,0), ISNULL(f.TauxRemise,0),
                       ISNULL(f.Statut,1),
                       (SELECT COUNT(1) FROM CommandesAchat c WHERE c.FournisseurId=f.Id)
                FROM Fournisseurs f WHERE f.Id=@id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return NotFound();
            return Ok(new
            {
                id = r.GetGuid(0), code = r.GetString(1), raisonSociale = r.GetString(2),
                email = r.GetString(3), telephone = r.GetString(4), adresse = r.GetString(5),
                ville = r.GetString(6), codePostal = r.GetString(7), pays = r.GetString(8),
                delaiLivraisonJours = r.GetInt32(9), tauxRemise = r.GetDecimal(10),
                statut = r.GetInt32(11), nombreCommandes = r.GetInt32(12)
            });
        }
        catch (Exception ex) { return StatusCode(500, new { succes = false, message = ex.Message }); }
    }

    [HttpPost]
    [Authorize(Policy = "Acheteur")]
    public async Task<IActionResult> Creer([FromBody] CreerFournisseurDto dto)
    {
        var v = await _validator.ValidateAsync(dto);
        if (!v.IsValid) return BadRequest(v.Errors.Select(e => e.ErrorMessage));
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            await using var chk = new SqlCommand(
                "SELECT COUNT(1) FROM Fournisseurs WHERE Code=@c", conn);
            chk.Parameters.AddWithValue("@c", dto.Code.Trim().ToUpper());
            if (Convert.ToInt32(await chk.ExecuteScalarAsync()) > 0)
                return BadRequest(new { succes = false, message = $"Le code '{dto.Code}' existe déjà." });

            var id = Guid.NewGuid();
            await using var cmd = new SqlCommand(@"
                INSERT INTO Fournisseurs
                    (Id,Code,RaisonSociale,Siret,Email,Telephone,Adresse,Ville,CodePostal,
                     Pays,DelaiLivraisonJours,TauxRemise,Statut,CreatedAt,CreatedBy)
                VALUES
                    (@id,@code,@rs,@siret,@email,@tel,@adr,@ville,@cp,
                     @pays,@dlai,0,1,GETUTCDATE(),@user)", conn);
            cmd.Parameters.AddWithValue("@id",    id);
            cmd.Parameters.AddWithValue("@code",  dto.Code.Trim().ToUpper());
            cmd.Parameters.AddWithValue("@rs",    dto.RaisonSociale.Trim());
            cmd.Parameters.AddWithValue("@siret", (object?)dto.Siret    ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@email", dto.Email.Trim());
            cmd.Parameters.AddWithValue("@tel",   dto.Telephone.Trim());
            cmd.Parameters.AddWithValue("@adr",   dto.Adresse.Trim());
            cmd.Parameters.AddWithValue("@ville", dto.Ville.Trim());
            cmd.Parameters.AddWithValue("@cp",    dto.CodePostal.Trim());
            cmd.Parameters.AddWithValue("@pays",  dto.Pays?.Trim() ?? "France");
            cmd.Parameters.AddWithValue("@dlai",  dto.DelaiLivraisonJours);
            cmd.Parameters.AddWithValue("@user",  UserId);
            await cmd.ExecuteNonQueryAsync();
            return Created(string.Empty, new { succes = true, data = id });
        }
        catch (Exception ex) { return StatusCode(500, new { succes = false, message = ex.Message }); }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "Acheteur")]
    public async Task<IActionResult> Modifier(Guid id, [FromBody] CreerFournisseurDto dto)
    {
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(@"
                UPDATE Fournisseurs SET
                    RaisonSociale=@rs, Email=@email, Telephone=@tel,
                    Adresse=@adr, Ville=@ville, CodePostal=@cp, Pays=@pays,
                    DelaiLivraisonJours=@dlai, UpdatedAt=GETUTCDATE(), UpdatedBy=@user
                WHERE Id=@id", conn);
            cmd.Parameters.AddWithValue("@id",    id);
            cmd.Parameters.AddWithValue("@rs",    dto.RaisonSociale.Trim());
            cmd.Parameters.AddWithValue("@email", dto.Email.Trim());
            cmd.Parameters.AddWithValue("@tel",   dto.Telephone.Trim());
            cmd.Parameters.AddWithValue("@adr",   dto.Adresse.Trim());
            cmd.Parameters.AddWithValue("@ville", dto.Ville.Trim());
            cmd.Parameters.AddWithValue("@cp",    dto.CodePostal.Trim());
            cmd.Parameters.AddWithValue("@pays",  dto.Pays?.Trim() ?? "France");
            cmd.Parameters.AddWithValue("@dlai",  dto.DelaiLivraisonJours);
            cmd.Parameters.AddWithValue("@user",  UserId);
            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0
                ? Ok(new { succes = true, message = "Fournisseur modifié." })
                : NotFound(new { succes = false, message = "Fournisseur introuvable." });
        }
        catch (Exception ex) { return StatusCode(500, new { succes = false, message = ex.Message }); }
    }
}
