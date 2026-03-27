using GestionStock.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Security.Claims;

namespace GestionStock.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "AdminOnly")]
public class RolesController : ControllerBase
{
    private readonly ITenantService _tenant;
    private readonly IConfiguration _config;
    private string UserId => User.FindFirstValue("sub") ?? "system";

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

    // Rôles système immuables (ne peuvent pas être supprimés)
    private static readonly Dictionary<int, (string Libelle, string Description)> RolesSysteme = new()
    {
        { 1, ("Admin", "Accès complet à toutes les fonctionnalités") },
        { 2, ("Magasinier", "Gestion des stocks, entrées, sorties et inventaires") },
        { 3, ("Acheteur", "Gestion des achats et des fournisseurs") },
        { 4, ("Superviseur", "Supervision des opérations, validation inventaires") },
        { 5, ("Lecteur", "Consultation uniquement, aucune modification") },
    };

    private static readonly Dictionary<int, string[]> DefaultPermissions = new()
    {
        { 1, new[] { "articles.lire","articles.ecrire","stocks.lire","stocks.ecrire","mouvements.lire","mouvements.ecrire","fournisseurs.lire","fournisseurs.ecrire","commandes.lire","commandes.ecrire","rapports.lire","utilisateurs.gerer","parametres.gerer","inventaire.valider","tracabilite.lire" } },
        { 2, new[] { "articles.lire","stocks.lire","stocks.ecrire","mouvements.lire","mouvements.ecrire","inventaire.saisir","tracabilite.lire" } },
        { 3, new[] { "articles.lire","stocks.lire","fournisseurs.lire","fournisseurs.ecrire","commandes.lire","commandes.ecrire","rapports.lire" } },
        { 4, new[] { "articles.lire","articles.ecrire","stocks.lire","stocks.ecrire","mouvements.lire","mouvements.ecrire","fournisseurs.lire","commandes.lire","rapports.lire","inventaire.valider","tracabilite.lire" } },
        { 5, new[] { "articles.lire","stocks.lire","mouvements.lire","fournisseurs.lire","commandes.lire","rapports.lire","tracabilite.lire" } },
    };

    public static readonly string[] ToutesLesPermissions =
    {
        "articles.lire","articles.ecrire","stocks.lire","stocks.ecrire",
        "mouvements.lire","mouvements.ecrire","fournisseurs.lire","fournisseurs.ecrire",
        "commandes.lire","commandes.ecrire","rapports.lire",
        "inventaire.saisir","inventaire.valider",
        "utilisateurs.gerer","parametres.gerer","tracabilite.lire"
    };

    public RolesController(ITenantService tenant, IConfiguration config)
    {
        _tenant = tenant;
        _config = config;
    }

    private async Task EnsureTablesAsync(SqlConnection conn)
    {
        await using var c1 = new SqlCommand(@"
            IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='RolesPersonnalises' AND xtype='U')
            CREATE TABLE RolesPersonnalises (
                Id int NOT NULL PRIMARY KEY,
                Libelle nvarchar(100) NOT NULL,
                Description nvarchar(500) NULL,
                Permissions nvarchar(2000) NOT NULL DEFAULT '',
                Couleur nvarchar(20) NULL,
                EstActif bit NOT NULL DEFAULT 1,
                EstSysteme bit NOT NULL DEFAULT 0,
                CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
                UpdatedAt datetime2 NULL,
                CreatedBy nvarchar(450) NOT NULL DEFAULT ''
            )", conn);
        await c1.ExecuteNonQueryAsync();

        // Seed rôles système si absents
        foreach (var (id, (lib, desc)) in RolesSysteme)
        {
            var perms = string.Join(",", DefaultPermissions.GetValueOrDefault(id, Array.Empty<string>()));
            await using var seed = new SqlCommand(@"
                IF NOT EXISTS (SELECT 1 FROM RolesPersonnalises WHERE Id=@id)
                INSERT INTO RolesPersonnalises (Id,Libelle,Description,Permissions,EstActif,EstSysteme,CreatedBy)
                VALUES (@id,@lib,@desc,@perms,1,1,'system')", conn);
            seed.Parameters.AddWithValue("@id", id);
            seed.Parameters.AddWithValue("@lib", lib);
            seed.Parameters.AddWithValue("@desc", desc);
            seed.Parameters.AddWithValue("@perms", perms);
            await seed.ExecuteNonQueryAsync();
        }
    }

    // ─── Liste tous les rôles ─────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var list = new List<object>();
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            await EnsureTablesAsync(conn);
            await using var cmd = new SqlCommand(@"
                SELECT r.Id, r.Libelle, r.Description, r.Permissions,
                       r.Couleur, r.EstActif, r.EstSysteme, r.CreatedAt,
                       (SELECT COUNT(1) FROM AspNetUsers WHERE Role=r.Id) AS NbUtilisateurs
                FROM RolesPersonnalises r
                ORDER BY r.EstSysteme DESC, r.Id ASC", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var perms = r.IsDBNull(3) ? "" : r.GetString(3);
                list.Add(new {
                    Id = r.GetInt32(0), Libelle = r.GetString(1),
                    Description = r.IsDBNull(2) ? "" : r.GetString(2),
                    Permissions = perms.Split(',', StringSplitOptions.RemoveEmptyEntries),
                    Couleur = r.IsDBNull(4) ? null : r.GetString(4),
                    EstActif = r.GetBoolean(5), EstSysteme = r.GetBoolean(6),
                    CreatedAt = r.GetDateTime(7), NbUtilisateurs = r.GetInt32(8)
                });
            }
        }
        catch (Exception ex) { return StatusCode(500, new { succes = false, message = ex.Message }); }
        return Ok(list);
    }

    // ─── Créer un rôle personnalisé ───────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Creer([FromBody] RoleRequest dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Libelle))
            return BadRequest(new { succes = false, message = "Le libellé est obligatoire." });

        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        await EnsureTablesAsync(conn);

        // Trouver le prochain ID libre >= 10
        int newId = 10;
        await using (var maxCmd = new SqlCommand("SELECT ISNULL(MAX(Id),9) FROM RolesPersonnalises WHERE Id >= 10", conn))
            newId = Convert.ToInt32(await maxCmd.ExecuteScalarAsync() ?? 9) + 1;

        // Vérifier unicité du libellé
        await using (var chk = new SqlCommand("SELECT COUNT(1) FROM RolesPersonnalises WHERE Libelle=@l", conn))
        {
            chk.Parameters.AddWithValue("@l", dto.Libelle.Trim());
            if (Convert.ToInt32(await chk.ExecuteScalarAsync() ?? 0) > 0)
                return BadRequest(new { succes = false, message = "Ce libellé existe déjà." });
        }

        var perms = string.Join(",", (dto.Permissions ?? Array.Empty<string>()).Distinct());
        await using var cmd = new SqlCommand(@"
            INSERT INTO RolesPersonnalises (Id,Libelle,Description,Permissions,Couleur,EstActif,EstSysteme,CreatedAt,CreatedBy)
            VALUES (@id,@lib,@desc,@perms,@couleur,1,0,GETUTCDATE(),@user)", conn);
        cmd.Parameters.AddWithValue("@id", newId);
        cmd.Parameters.AddWithValue("@lib", dto.Libelle.Trim());
        cmd.Parameters.AddWithValue("@desc", (object?)dto.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@perms", perms);
        cmd.Parameters.AddWithValue("@couleur", (object?)dto.Couleur ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@user", UserId);
        await cmd.ExecuteNonQueryAsync();

        return Ok(new { succes = true, message = "Rôle créé.", id = newId });
    }

    // ─── Modifier un rôle ────────────────────────────────────────────────────
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Modifier(int id, [FromBody] RoleRequest dto)
    {
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        await EnsureTablesAsync(conn);

        // Toujours autoriser la modification des permissions pour les rôles système
        // Mais interdire le renommage des rôles système
        bool estSysteme = RolesSysteme.ContainsKey(id);
        var perms = string.Join(",", (dto.Permissions ?? Array.Empty<string>()).Distinct());

        SqlCommand cmd;
        if (estSysteme)
        {
            // Rôle système: mise à jour permissions seulement
            cmd = new SqlCommand(@"
                UPDATE RolesPersonnalises
                SET Permissions=@perms, UpdatedAt=GETUTCDATE()
                WHERE Id=@id", conn);
        }
        else
        {
            // Rôle custom: tout modifier
            cmd = new SqlCommand(@"
                UPDATE RolesPersonnalises
                SET Libelle=@lib, Description=@desc, Permissions=@perms,
                    Couleur=@couleur, UpdatedAt=GETUTCDATE()
                WHERE Id=@id", conn);
            cmd.Parameters.AddWithValue("@lib", dto.Libelle?.Trim() ?? "");
            cmd.Parameters.AddWithValue("@desc", (object?)dto.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@couleur", (object?)dto.Couleur ?? DBNull.Value);
        }
        cmd.Parameters.AddWithValue("@perms", perms);
        cmd.Parameters.AddWithValue("@id", id);
        await using var _ = cmd;
        int rows = await cmd.ExecuteNonQueryAsync();

        return rows > 0
            ? Ok(new { succes = true, message = estSysteme ? "Permissions du rôle système mises à jour." : "Rôle modifié." })
            : NotFound(new { succes = false, message = "Rôle introuvable." });
    }

    // ─── Activer / Désactiver un rôle ─────────────────────────────────────────
    [HttpPost("{id:int}/activer")]
    public async Task<IActionResult> Activer(int id)
    {
        if (RolesSysteme.ContainsKey(id)) return BadRequest(new { succes = false, message = "Les rôles système ne peuvent pas être désactivés." });
        return await SetActif(id, true);
    }

    [HttpPost("{id:int}/desactiver")]
    public async Task<IActionResult> Desactiver(int id)
    {
        if (RolesSysteme.ContainsKey(id)) return BadRequest(new { succes = false, message = "Les rôles système ne peuvent pas être désactivés." });
        // Vérifier qu'il n'y a pas d'utilisateurs actifs
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        await using (var chk = new SqlCommand("SELECT COUNT(1) FROM AspNetUsers WHERE Role=@id", conn))
        {
            chk.Parameters.AddWithValue("@id", id);
            int count = Convert.ToInt32(await chk.ExecuteScalarAsync() ?? 0);
            if (count > 0) return BadRequest(new { succes = false, message = $"Ce rôle est assigné à {count} utilisateur(s). Réassignez-les avant de le désactiver." });
        }
        return await SetActif(id, false);
    }

    // ─── Supprimer un rôle custom ─────────────────────────────────────────────
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Supprimer(int id)
    {
        if (RolesSysteme.ContainsKey(id)) return BadRequest(new { succes = false, message = "Les rôles système ne peuvent pas être supprimés." });
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        await using (var chk = new SqlCommand("SELECT COUNT(1) FROM AspNetUsers WHERE Role=@id", conn))
        {
            chk.Parameters.AddWithValue("@id", id);
            if (Convert.ToInt32(await chk.ExecuteScalarAsync() ?? 0) > 0)
                return BadRequest(new { succes = false, message = "Ce rôle est encore utilisé par des utilisateurs." });
        }
        await using var cmd = new SqlCommand("DELETE FROM RolesPersonnalises WHERE Id=@id AND EstSysteme=0", conn);
        cmd.Parameters.AddWithValue("@id", id);
        int rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0 ? Ok(new { succes = true, message = "Rôle supprimé." })
            : NotFound(new { succes = false, message = "Rôle introuvable ou rôle système." });
    }

    [HttpGet("permissions/catalogue")]
    public IActionResult GetCatalogue() => Ok(ToutesLesPermissions);

    private async Task<IActionResult> SetActif(int id, bool actif)
    {
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand("UPDATE RolesPersonnalises SET EstActif=@a, UpdatedAt=GETUTCDATE() WHERE Id=@id", conn);
        cmd.Parameters.AddWithValue("@a", actif);
        cmd.Parameters.AddWithValue("@id", id);
        int rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0 ? Ok(new { succes = true, message = actif ? "Rôle activé." : "Rôle désactivé." })
            : NotFound(new { succes = false, message = "Rôle introuvable." });
    }
}

public class RoleRequest
{
    public string Libelle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string[]? Permissions { get; set; }
    public string? Couleur { get; set; }
}
