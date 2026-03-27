using GestionStock.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace GestionStock.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UtilisateursController : ControllerBase
{
    private readonly ITenantService _tenant;
    private readonly IConfiguration _config;
    private string UserId => User.FindFirstValue("sub") ?? "system";

    // Résout la DB du tenant connecté (claim "tenant") ou la DB principale
    private string ConnStr
    {
        get
        {
            var tenantCode = User.FindFirstValue("tenant");
            if (!string.IsNullOrEmpty(tenantCode))
                return _tenant.GetConnectionString(tenantCode);
            // Super-admin : DB principale
            return _config.GetConnectionString("DefaultConnection")!;
        }
    }

    // Noms des rôles
    private static readonly Dictionary<int, string> Roles = new()
    {
        { 1, "Admin" }, { 2, "Magasinier" }, { 3, "Acheteur" },
        { 4, "Superviseur" }, { 5, "Lecteur" }
    };

    // Permissions par rôle (valeurs par défaut — surchargées par la table PermissionsRoles)
    private static readonly Dictionary<int, string[]> DefaultPermissions = new()
    {
        { 1, new[] { "articles.lire", "articles.ecrire", "stocks.lire", "stocks.ecrire",
                     "mouvements.lire", "mouvements.ecrire", "fournisseurs.lire", "fournisseurs.ecrire",
                     "commandes.lire", "commandes.ecrire", "rapports.lire", "utilisateurs.gerer",
                     "parametres.gerer", "inventaire.valider" } },
        { 2, new[] { "articles.lire", "stocks.lire", "stocks.ecrire",
                     "mouvements.lire", "mouvements.ecrire", "inventaire.saisir" } },
        { 3, new[] { "articles.lire", "stocks.lire", "fournisseurs.lire", "fournisseurs.ecrire",
                     "commandes.lire", "commandes.ecrire", "rapports.lire" } },
        { 4, new[] { "articles.lire", "articles.ecrire", "stocks.lire", "stocks.ecrire",
                     "mouvements.lire", "mouvements.ecrire", "fournisseurs.lire",
                     "commandes.lire", "rapports.lire", "inventaire.valider" } },
        { 5, new[] { "articles.lire", "stocks.lire", "mouvements.lire",
                     "fournisseurs.lire", "commandes.lire", "rapports.lire" } },
    };


    // Catalogue complet des permissions disponibles
    public static readonly string[] ToutesLesPermissions = new[]
    {
        "articles.lire", "articles.ecrire",
        "stocks.lire", "stocks.ecrire",
        "mouvements.lire", "mouvements.ecrire",
        "fournisseurs.lire", "fournisseurs.ecrire",
        "commandes.lire", "commandes.ecrire",
        "rapports.lire",
        "inventaire.saisir", "inventaire.valider",
        "utilisateurs.gerer",
        "parametres.gerer",
        "tracabilite.lire"
    };

    public UtilisateursController(ITenantService tenant, IConfiguration config)
    {
        _tenant = tenant;
        _config = config;
    }

    // ─── Liste des utilisateurs ───────────────────────────────────────────────
    [HttpGet]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetAll()
    {
        var list = new List<object>();
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        await EnsurePermissionsTablesAsync(conn);

        // Pré-charger les permissions de tous les rôles (évite concurrent reader)
        var rolePermsCache = new Dictionary<int, string[]>();
        for (int ri = 1; ri <= 5; ri++)
            rolePermsCache[ri] = await GetPermissionsRoleAsync(ri, conn);

        // Pré-charger les overrides individuels
        var userPermsCache = new Dictionary<string, string[]>();
        try
        {
            await using var upCmd = new SqlCommand("SELECT UserId, Permissions FROM PermissionsUtilisateurs", conn);
            await using var upR = await upCmd.ExecuteReaderAsync();
            while (await upR.ReadAsync())
                userPermsCache[upR.GetString(0)] = upR.GetString(1).Split(',', StringSplitOptions.RemoveEmptyEntries);
        }
        catch { }

        await using var cmd = new SqlCommand(@"
            SELECT Id, Email, ISNULL(UserName,Email),
                   ISNULL(NomComplet, Email), Role,
                   LockoutEnabled, LockoutEnd, ISNULL(EntrepotAssocie,'')
            FROM AspNetUsers
            ORDER BY Role ASC, NomComplet ASC", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var role = r.IsDBNull(4) ? 5 : r.GetInt32(4);
            var nomComplet = r.IsDBNull(3) ? "" : r.GetString(3);
            var userId = r.GetString(0);
            var parts = nomComplet.Split(' ', 2);
            var effectivePerms = userPermsCache.ContainsKey(userId)
                ? userPermsCache[userId]
                : rolePermsCache.GetValueOrDefault(role, Array.Empty<string>());
            list.Add(new {
                Id = userId, Email = r.GetString(1),
                UserName = r.GetString(2),
                FirstName = parts.Length > 0 ? parts[0] : "",
                LastName = parts.Length > 1 ? parts[1] : "",
                NomComplet = nomComplet,
                Role = role, RoleLibelle = Roles.GetValueOrDefault(role, "Inconnu"),
                EstActif = true,
                CreatedAt = DateTime.UtcNow,
                DerniereConnexion = (DateTime?)null,
                EstVerrouille = !r.IsDBNull(5) && r.GetBoolean(5)
                    && !r.IsDBNull(6) && r.GetDateTimeOffset(6) > DateTimeOffset.UtcNow,
                EntrepotAssocie = r.IsDBNull(7) ? null : r.GetString(7),
                Permissions = effectivePerms,
                HasCustomPermissions = userPermsCache.ContainsKey(userId)
            });
        }
        return Ok(list);
    }

    // ─── Créer un utilisateur ─────────────────────────────────────────────────
    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Creer([FromBody] UtilisateurRequest dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.MotDePasse))
            return BadRequest(new { succes = false, message = "Email et mot de passe obligatoires." });
        if (dto.MotDePasse.Length < 8)
            return BadRequest(new { succes = false, message = "Mot de passe trop court (8 car. min)." });

        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();

        await using (var chk = new SqlCommand("SELECT COUNT(1) FROM AspNetUsers WHERE Email=@e", conn))
        {
            chk.Parameters.AddWithValue("@e", dto.Email.Trim().ToLower());
            if (Convert.ToInt32(await chk.ExecuteScalarAsync() ?? 0) > 0)
                return BadRequest(new { succes = false, message = "Cet email est déjà utilisé." });
        }

        var hash = HashPassword(dto.MotDePasse);
        var id = Guid.NewGuid().ToString();
        await using var cmd = new SqlCommand(@"
            INSERT INTO AspNetUsers
                (Id, Email, NormalizedEmail, UserName, NormalizedUserName,
                 PasswordHash, SecurityStamp, ConcurrencyStamp,
                 NomComplet, Role,
                 EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled,
                 LockoutEnabled, AccessFailedCount)
            VALUES
                (@id, @email, @nemailnorm, @email, @usernorm,
                 @hash, NEWID(), NEWID(),
                 @nom, @role,
                 1, 0, 0, 0, 0)", conn);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@email", dto.Email.Trim().ToLower());
        cmd.Parameters.AddWithValue("@nemailnorm", dto.Email.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("@usernorm", dto.Email.Trim().ToUpperInvariant());
        cmd.Parameters.AddWithValue("@hash", hash);
        cmd.Parameters.AddWithValue("@nom", $"{dto.Prenom?.Trim()} {dto.Nom?.Trim()}".Trim());
        cmd.Parameters.AddWithValue("@role", dto.Role);
        await cmd.ExecuteNonQueryAsync();

        return Ok(new { succes = true, message = "Utilisateur créé.", id });
    }

    // ─── Modifier un utilisateur ──────────────────────────────────────────────
    [HttpPut("{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Modifier(string id, [FromBody] ModifierUtilisateurRequest dto)
    {
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(@"
            UPDATE AspNetUsers
            SET NomComplet=@nom, Role=@role,
                EntrepotAssocie=@depot,
                SecurityStamp=NEWID()
            WHERE Id=@id", conn);
        cmd.Parameters.AddWithValue("@nom", $"{dto.Prenom?.Trim()} {dto.Nom?.Trim()}".Trim());
        cmd.Parameters.AddWithValue("@role", dto.Role);
        cmd.Parameters.AddWithValue("@depot", (object?)dto.EntrepotAssocie ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", id);
        int rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0
            ? Ok(new { succes = true, message = "Utilisateur modifié." })
            : NotFound(new { succes = false, message = "Utilisateur introuvable." });
    }

    // ─── Réinitialiser le mot de passe ────────────────────────────────────────
    [HttpPost("{id}/reset-password")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> ResetPassword(string id, [FromBody] ResetMdpRequest dto)
    {
        if (string.IsNullOrWhiteSpace(dto.NouveauMotDePasse) || dto.NouveauMotDePasse.Length < 8)
            return BadRequest(new { succes = false, message = "Mot de passe trop court (8 car. min)." });
        var hash = HashPassword(dto.NouveauMotDePasse);
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(
            "UPDATE AspNetUsers SET PasswordHash=@h, SecurityStamp=NEWID(), AccessFailedCount=0 WHERE Id=@id", conn);
        cmd.Parameters.AddWithValue("@h", hash);
        cmd.Parameters.AddWithValue("@id", id);
        int rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0
            ? Ok(new { succes = true, message = "Mot de passe réinitialisé." })
            : NotFound(new { succes = false, message = "Utilisateur introuvable." });
    }

    // ─── Activer / Désactiver ─────────────────────────────────────────────────
    [HttpPost("{id}/activer")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Activer(string id)
    {
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(
            "UPDATE AspNetUsers SET EstActif=1, LockoutEnd=NULL WHERE Id=@id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
        return Ok(new { succes = true, message = "Compte activé." });
    }

    [HttpPost("{id}/desactiver")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Desactiver(string id)
    {
        // Ne pas désactiver son propre compte
        if (id == UserId) return BadRequest(new { succes = false, message = "Vous ne pouvez pas désactiver votre propre compte." });
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(
            "UPDATE AspNetUsers SET EstActif=0 WHERE Id=@id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
        return Ok(new { succes = true, message = "Compte désactivé." });
    }

    // ─── Déverrouiller ───────────────────────────────────────────────────────
    [HttpPost("{id}/deverrouiller")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Deverrouiller(string id)
    {
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(
            "UPDATE AspNetUsers SET LockoutEnd=NULL, AccessFailedCount=0 WHERE Id=@id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
        return Ok(new { succes = true, message = "Compte déverrouillé." });
    }

    // ─── Rôles et permissions ─────────────────────────────────────────────────
    [HttpGet("roles")]
    public async Task<IActionResult> GetRoles()
    {
        // Charger les overrides depuis la DB (si disponibles)
        var dbPerms = new Dictionary<int, string[]>();
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            await EnsurePermissionsTablesAsync(conn);
            await using var cmd = new SqlCommand("SELECT RoleId, Permissions FROM PermissionsRoles", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                dbPerms[r.GetInt32(0)] = r.GetString(1).Split(',', StringSplitOptions.RemoveEmptyEntries);
        }
        catch { /* DB inaccessible: utilise les défauts */ }

        // ToList() force l'évaluation avant la fin du scope de la connexion
        var result = Roles.Select(kvp => new {
            Id = kvp.Key, Libelle = kvp.Value,
            Permissions = dbPerms.ContainsKey(kvp.Key)
                ? dbPerms[kvp.Key]
                : DefaultPermissions.GetValueOrDefault(kvp.Key, Array.Empty<string>()),
            Description = kvp.Key switch {
                1 => "Accès complet à toutes les fonctionnalités",
                2 => "Gestion des stocks, entrées, sorties et inventaires",
                3 => "Gestion des achats et des fournisseurs",
                4 => "Supervision des opérations, validation inventaires",
                5 => "Consultation uniquement, aucune modification",
                _ => ""
            },
            IsCustom = dbPerms.ContainsKey(kvp.Key)
        }).ToList();
        return Ok(result);
    }

    // ─── Changer son propre mot de passe ─────────────────────────────────────
    [HttpPost("changer-mot-de-passe")]
    public async Task<IActionResult> ChangerMotDePasse([FromBody] ChangerMdpRequest dto)
    {
        if (dto.NouveauMotDePasse.Length < 8)
            return BadRequest(new { succes = false, message = "Nouveau mot de passe trop court." });

        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();

        // Vérifier l'ancien mot de passe
        string? hash = null;
        await using (var sel = new SqlCommand("SELECT PasswordHash FROM AspNetUsers WHERE Id=@id", conn))
        {
            sel.Parameters.AddWithValue("@id", UserId);
            hash = (string?)await sel.ExecuteScalarAsync();
        }
        if (hash == null || !VerifyPassword(dto.AncienMotDePasse, hash))
            return BadRequest(new { succes = false, message = "Mot de passe actuel incorrect." });

        var newHash = HashPassword(dto.NouveauMotDePasse);
        await using var cmd = new SqlCommand(
            "UPDATE AspNetUsers SET PasswordHash=@h, SecurityStamp=NEWID() WHERE Id=@id", conn);
        cmd.Parameters.AddWithValue("@h", newHash);
        cmd.Parameters.AddWithValue("@id", UserId);
        await cmd.ExecuteNonQueryAsync();
        return Ok(new { succes = true, message = "Mot de passe changé." });
    }

    // ─── Lire permissions d'un rôle (DB ou défaut) ──────────────────────────────
    private async Task<string[]> GetPermissionsRoleAsync(int role, SqlConnection conn)
    {
        try
        {
            await using var cmd = new SqlCommand(
                "SELECT Permissions FROM PermissionsRoles WHERE RoleId=@r", conn);
            cmd.Parameters.AddWithValue("@r", role);
            var val = await cmd.ExecuteScalarAsync();
            if (val != null && val != DBNull.Value)
                return val.ToString()!.Split(',', StringSplitOptions.RemoveEmptyEntries);
        }
        catch { }
        return DefaultPermissions.GetValueOrDefault(role, Array.Empty<string>());
    }

    // ─── Lire permissions d'un utilisateur (overrides rôle) ──────────────────
    private async Task<string[]?> GetPermissionsUtilisateurAsync(string userId, SqlConnection conn)
    {
        try
        {
            await using var cmd = new SqlCommand(
                "SELECT Permissions FROM PermissionsUtilisateurs WHERE UserId=@u", conn);
            cmd.Parameters.AddWithValue("@u", userId);
            var val = await cmd.ExecuteScalarAsync();
            if (val != null && val != DBNull.Value)
                return val.ToString()!.Split(',', StringSplitOptions.RemoveEmptyEntries);
        }
        catch { }
        return null; // null = utilise les permissions du rôle
    }

    // ─── Initialiser tables permissions si absentes ───────────────────────────
    private async Task EnsurePermissionsTablesAsync(SqlConnection conn)
    {
        await using var c1 = new SqlCommand(@"
            IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='PermissionsRoles' AND xtype='U')
            CREATE TABLE PermissionsRoles (
                RoleId int NOT NULL PRIMARY KEY,
                Permissions nvarchar(2000) NOT NULL,
                UpdatedAt datetime2 NOT NULL DEFAULT GETUTCDATE()
            )", conn);
        await c1.ExecuteNonQueryAsync();

        await using var c2 = new SqlCommand(@"
            IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='PermissionsUtilisateurs' AND xtype='U')
            CREATE TABLE PermissionsUtilisateurs (
                UserId nvarchar(450) NOT NULL PRIMARY KEY,
                Permissions nvarchar(2000) NOT NULL,
                UpdatedAt datetime2 NOT NULL DEFAULT GETUTCDATE()
            )", conn);
        await c2.ExecuteNonQueryAsync();
    }

    // ─── GET toutes les permissions disponibles ───────────────────────────────
    [HttpGet("permissions/catalogue")]
    public IActionResult GetCatalogue() => Ok(ToutesLesPermissions);

    // ─── GET permissions d'un rôle ────────────────────────────────────────────
    [HttpGet("roles/{roleId:int}/permissions")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetPermissionsRole(int roleId)
    {
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        await EnsurePermissionsTablesAsync(conn);
        var perms = await GetPermissionsRoleAsync(roleId, conn);
        return Ok(new { roleId, permissions = perms, tous = ToutesLesPermissions });
    }

    // ─── PUT permissions d'un rôle ────────────────────────────────────────────
    [HttpPut("roles/{roleId:int}/permissions")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> SetPermissionsRole(int roleId, [FromBody] PermissionsRequest dto)
    {
        if (roleId < 1 || roleId > 5) return BadRequest(new { succes = false, message = "Rôle invalide." });
        var joined = string.Join(",", dto.Permissions.Distinct());
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        await EnsurePermissionsTablesAsync(conn);
        await using var cmd = new SqlCommand(@"
            MERGE PermissionsRoles AS t
            USING (VALUES (@r, @p, GETUTCDATE())) AS s(RoleId, Permissions, UpdatedAt)
            ON t.RoleId = s.RoleId
            WHEN MATCHED THEN UPDATE SET Permissions=s.Permissions, UpdatedAt=s.UpdatedAt
            WHEN NOT MATCHED THEN INSERT (RoleId, Permissions, UpdatedAt) VALUES (s.RoleId, s.Permissions, s.UpdatedAt);",
            conn);
        cmd.Parameters.AddWithValue("@r", roleId);
        cmd.Parameters.AddWithValue("@p", joined);
        await cmd.ExecuteNonQueryAsync();
        return Ok(new { succes = true, message = $"Permissions du rôle mises à jour ({dto.Permissions.Length} permission(s))." });
    }

    // ─── GET permissions d'un utilisateur ────────────────────────────────────
    [HttpGet("{id}/permissions")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> GetPermissionsUser(string id)
    {
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        await EnsurePermissionsTablesAsync(conn);

        // Rôle de l'utilisateur
        int role = 5;
        await using (var cmd = new SqlCommand("SELECT ISNULL(Role,5) FROM AspNetUsers WHERE Id=@id", conn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            var val = await cmd.ExecuteScalarAsync();
            if (val != null && val != DBNull.Value) role = Convert.ToInt32(val);
        }

        var rolePerms = await GetPermissionsRoleAsync(role, conn);
        var userOverride = await GetPermissionsUtilisateurAsync(id, conn);
        return Ok(new {
            userId = id, role,
            permissionsRole = rolePerms,
            permissionsUtilisateur = userOverride,
            permissionsEffectives = userOverride ?? rolePerms,
            tous = ToutesLesPermissions
        });
    }

    // ─── PUT permissions d'un utilisateur ────────────────────────────────────
    [HttpPut("{id}/permissions")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> SetPermissionsUser(string id, [FromBody] PermissionsRequest dto)
    {
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        await EnsurePermissionsTablesAsync(conn);

        if (dto.ResetToRole)
        {
            // Supprimer l'override utilisateur
            await using var del = new SqlCommand("DELETE FROM PermissionsUtilisateurs WHERE UserId=@id", conn);
            del.Parameters.AddWithValue("@id", id);
            await del.ExecuteNonQueryAsync();
            return Ok(new { succes = true, message = "Permissions réinitialisées aux permissions du rôle." });
        }

        var joined = string.Join(",", dto.Permissions.Distinct());
        await using var cmd = new SqlCommand(@"
            MERGE PermissionsUtilisateurs AS t
            USING (VALUES (@u, @p, GETUTCDATE())) AS s(UserId, Permissions, UpdatedAt)
            ON t.UserId = s.UserId
            WHEN MATCHED THEN UPDATE SET Permissions=s.Permissions, UpdatedAt=s.UpdatedAt
            WHEN NOT MATCHED THEN INSERT (UserId, Permissions, UpdatedAt) VALUES (s.UserId, s.Permissions, s.UpdatedAt);",
            conn);
        cmd.Parameters.AddWithValue("@u", id);
        cmd.Parameters.AddWithValue("@p", joined);
        await cmd.ExecuteNonQueryAsync();
        return Ok(new { succes = true, message = $"Permissions personnalisées enregistrées ({dto.Permissions.Length} permission(s))." });
    }

    // ─── Hash helper ─────────────────────────────────────────────────────────
    private static string HashPassword(string password)
    {
        var salt = new byte[16];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(salt);
        var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 10000, HashAlgorithmName.SHA256);
        var key = pbkdf2.GetBytes(32);
        var result = new byte[1 + 4 + 4 + 4 + 16 + 32];
        result[0] = 0x01;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(1), 1);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(5), 10000);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(9), 16);
        salt.CopyTo(result, 13);
        key.CopyTo(result, 29);
        return Convert.ToBase64String(result);
    }

    private static bool VerifyPassword(string password, string base64Hash)
    {
        try
        {
            var hash = Convert.FromBase64String(base64Hash);
            if (hash[0] != 0x01) return false;
            var iterCount = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(5));
            var saltLen = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(9));
            var salt = hash[13..(13 + (int)saltLen)];
            var stored = hash[(13 + (int)saltLen)..];
            var derived = new Rfc2898DeriveBytes(password, salt, (int)iterCount, HashAlgorithmName.SHA256).GetBytes(32);
            return CryptographicOperations.FixedTimeEquals(derived, stored);
        }
        catch { return false; }
    }
}

public class UtilisateurRequest
{
    public string Email { get; set; } = string.Empty;
    public string MotDePasse { get; set; } = string.Empty;
    public string? Prenom { get; set; }
    public string? Nom { get; set; }
    public int Role { get; set; } = 5;
    public string? EntrepotAssocie { get; set; }
}

public class ModifierUtilisateurRequest
{
    public string? Prenom { get; set; }
    public string? Nom { get; set; }
    public int Role { get; set; } = 5;
    public bool EstActif { get; set; } = true;
    public string? EntrepotAssocie { get; set; }
}

public class ResetMdpRequest { public string NouveauMotDePasse { get; set; } = ""; }
public class ChangerMdpRequest
{
    public string AncienMotDePasse { get; set; } = "";
    public string NouveauMotDePasse { get; set; } = "";
}

public class PermissionsRequest
{
    public string[] Permissions { get; set; } = Array.Empty<string>();
    public bool ResetToRole { get; set; } = false;
}
