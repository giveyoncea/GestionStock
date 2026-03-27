using GestionStock.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Security.Claims;

namespace GestionStock.API.Controllers;

[ApiController]
[Route("api/admin/tenants")]
[Authorize(Policy = "AdminOnly")]
public class TenantsAdminController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ProvisioningService _provisioning;
    private readonly IEmailService _email;
    private readonly ILogger<TenantsAdminController> _logger;
    private string MasterConn => _config.GetConnectionString("MasterConnection")!;
    private string SqlBase => _config.GetConnectionString("SqlServerBase")!;

    public TenantsAdminController(
        IConfiguration config, ProvisioningService provisioning,
        IEmailService email, ILogger<TenantsAdminController> logger)
    {
        _config = config;
        _provisioning = provisioning;
        _email = email;
        _logger = logger;
    }

    // ─── Liste ─────────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var list = new List<object>();
        try
        {
            await using var conn = new SqlConnection(MasterConn);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                @"SELECT Id, Code, RaisonSociale, Email, AdminNom, BaseDeDonnees,
                         EstActif, DateCreation
                  FROM Tenants ORDER BY DateCreation DESC", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new {
                    Id          = r.GetGuid(0),
                    Code        = r.GetString(1),
                    RaisonSociale = r.GetString(2),
                    Email       = r.GetString(3),
                    AdminNom    = r.GetString(4),
                    BaseDeDonnees = r.GetString(5),
                    EstActif    = r.GetBoolean(6),
                    DateCreation = r.GetDateTime(7)
                });
        }
        catch (Exception ex) { _logger.LogError(ex, "GetAll tenants"); }
        return Ok(list);
    }

    // ─── Créer un tenant (par le super-admin) ──────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Creer([FromBody] TenantCreateRequest dto)
    {
        if (string.IsNullOrWhiteSpace(dto.RaisonSociale) ||
            string.IsNullOrWhiteSpace(dto.Email) ||
            string.IsNullOrWhiteSpace(dto.MotDePasse))
            return BadRequest(new { succes = false, message = "Champs obligatoires manquants." });
        if (!TryValidateSetup(dto.Devise, dto.SymboleDevise, dto.NombreDecimalesMontant, dto.NombreDecimalesQuantite, out var validationMessage))
            return BadRequest(new { succes = false, message = validationMessage });

        var tenantCode = GenererCode(dto.RaisonSociale);
        try
        {
            await using var conn = new SqlConnection(MasterConn);
            await conn.OpenAsync();

            // Unicité email
            await using (var chk = new SqlCommand("SELECT COUNT(1) FROM Tenants WHERE Email=@e", conn))
            {
                chk.Parameters.AddWithValue("@e", dto.Email.Trim().ToLower());
                if (Convert.ToInt32(await chk.ExecuteScalarAsync() ?? 0) > 0)
                    return BadRequest(new { succes = false, message = "Cet email est déjà utilisé." });
            }

            // Code unique
            int suffix = 0;
            var code = tenantCode;
            while (true)
            {
                await using var chk2 = new SqlCommand("SELECT COUNT(1) FROM Tenants WHERE Code=@c", conn);
                chk2.Parameters.AddWithValue("@c", code);
                if (Convert.ToInt32(await chk2.ExecuteScalarAsync() ?? 0) == 0) break;
                suffix++;
                code = $"{tenantCode}{suffix}";
            }
            tenantCode = code;
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { succes = false, message = ex.Message });
        }

        var adminNom = string.IsNullOrWhiteSpace(dto.NomAdmin) ? dto.RaisonSociale : dto.NomAdmin;
        await _provisioning.ProvisionnerTenantAsync(
            tenantCode, dto.Email, dto.MotDePasse, adminNom, dto.RaisonSociale,
            dto.Devise.Trim().ToUpperInvariant(), dto.SymboleDevise.Trim(),
            NormalizeDecimals(dto.NombreDecimalesMontant), NormalizeDecimals(dto.NombreDecimalesQuantite));

        var dbName = $"GestionStock_{tenantCode}";
        await using (var conn2 = new SqlConnection(MasterConn))
        {
            await conn2.OpenAsync();
            await using var ins = new SqlCommand(@"
                INSERT INTO Tenants (Id,Code,RaisonSociale,Email,AdminNom,BaseDeDonnees,EstActif,DateCreation)
                VALUES (NEWID(),@code,@rs,@email,@nom,@db,1,GETUTCDATE())", conn2);
            ins.Parameters.AddWithValue("@code", tenantCode);
            ins.Parameters.AddWithValue("@rs", dto.RaisonSociale.Trim());
            ins.Parameters.AddWithValue("@email", dto.Email.Trim().ToLower());
            ins.Parameters.AddWithValue("@nom", adminNom);
            ins.Parameters.AddWithValue("@db", dbName);
            await ins.ExecuteNonQueryAsync();
        }

        // Email de bienvenue
        if (dto.EnvoyerEmail)
        {
            var appUrl = _config["AppSettings:AppUrl"] ?? "http://localhost:5000";
            await _email.SendAsync(dto.Email, "Votre accès GestionStock",
                $"<p>Bonjour {adminNom},</p><p>Votre espace <b>GestionStock</b> a été créé.</p>" +
                $"<p>Code tenant : <code>{tenantCode}</code><br>Email : {dto.Email}<br>Mot de passe : celui que vous avez choisi.</p>" +
                $"<p><a href='{appUrl}/login'>Accéder</a></p>");
        }

        _logger.LogInformation("Tenant créé par admin: {Code}", tenantCode);
        return Ok(new { succes = true, message = "Compte créé.", tenantCode, dbName });
    }

    // ─── Détails + stats d'un tenant ───────────────────────────────────────────
    [HttpGet("{code}")]
    public async Task<IActionResult> GetDetail(string code)
    {
        code = code.ToUpperInvariant();
        object? tenant = null;
        object? stats = null;

        try
        {
            await using var conn = new SqlConnection(MasterConn);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "SELECT Id,Code,RaisonSociale,Email,AdminNom,BaseDeDonnees,EstActif,DateCreation FROM Tenants WHERE Code=@c", conn);
            cmd.Parameters.AddWithValue("@c", code);
            await using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
                tenant = new {
                    Id = r.GetGuid(0), Code = r.GetString(1),
                    RaisonSociale = r.GetString(2), Email = r.GetString(3),
                    AdminNom = r.GetString(4), BaseDeDonnees = r.GetString(5),
                    EstActif = r.GetBoolean(6), DateCreation = r.GetDateTime(7)
                };
        }
        catch { }

        if (tenant == null) return NotFound(new { succes = false, message = "Tenant introuvable." });

        // Statistiques depuis la DB du tenant
        try
        {
            var tenantConn = $"{SqlBase};Database=GestionStock_{code}";
            await using var conn2 = new SqlConnection(tenantConn);
            await conn2.OpenAsync();
            await using var cmd2 = new SqlCommand(@"
                SELECT
                    (SELECT COUNT(1) FROM Articles WHERE Statut=1) AS Articles,
                    (SELECT COUNT(1) FROM Fournisseurs WHERE EstActif=1) AS Fournisseurs,
                    (SELECT ISNULL(SUM(QuantiteDisponible*0),0) FROM StockArticles) AS ValeurStock,
                    (SELECT COUNT(1) FROM MouvementsStock) AS TotalMouvements,
                    (SELECT COUNT(1) FROM AspNetUsers) AS Utilisateurs", conn2);
            await using var r2 = await cmd2.ExecuteReaderAsync();
            if (await r2.ReadAsync())
                stats = new {
                    Articles = r2.GetInt32(0),
                    Fournisseurs = r2.GetInt32(1),
                    ValeurStock = r2.GetDecimal(2),
                    TotalMouvements = r2.GetInt32(3),
                    Utilisateurs = r2.GetInt32(4)
                };
        }
        catch { stats = null; }

        return Ok(new { tenant, stats });
    }

    // ─── Activer ───────────────────────────────────────────────────────────────
    [HttpPost("{code}/activer")]
    public async Task<IActionResult> Activer(string code)
    {
        await using var conn = new SqlConnection(MasterConn);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand("UPDATE Tenants SET EstActif=1 WHERE Code=@c", conn);
        cmd.Parameters.AddWithValue("@c", code.ToUpperInvariant());
        int rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0
            ? Ok(new { succes = true, message = "Compte activé." })
            : NotFound(new { succes = false, message = "Tenant introuvable." });
    }

    // ─── Suspendre ─────────────────────────────────────────────────────────────
    [HttpPost("{code}/suspendre")]
    public async Task<IActionResult> Suspendre(string code)
    {
        await using var conn = new SqlConnection(MasterConn);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand("UPDATE Tenants SET EstActif=0 WHERE Code=@c", conn);
        cmd.Parameters.AddWithValue("@c", code.ToUpperInvariant());
        int rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0
            ? Ok(new { succes = true, message = "Compte suspendu." })
            : NotFound(new { succes = false, message = "Tenant introuvable." });
    }

    // ─── Réinitialiser le mot de passe admin ───────────────────────────────────
    [HttpPost("{code}/reset-password")]
    public async Task<IActionResult> ResetPassword(string code, [FromBody] ResetPasswordRequest dto)
    {
        if (string.IsNullOrWhiteSpace(dto.NouveauMotDePasse) || dto.NouveauMotDePasse.Length < 8)
            return BadRequest(new { succes = false, message = "Mot de passe trop court (8 car. min)." });

        code = code.ToUpperInvariant();
        var tenantConn = $"{SqlBase};Database=GestionStock_{code}";

        try
        {
            var hash = HashPassword(dto.NouveauMotDePasse);
            await using var conn = new SqlConnection(tenantConn);
            await conn.OpenAsync();

            // Trouver l'admin principal
            await using var cmd = new SqlCommand(@"
                UPDATE AspNetUsers SET PasswordHash=@h, SecurityStamp=NEWID()
                WHERE Role=1", conn); // Role 1 = Admin
            cmd.Parameters.AddWithValue("@h", hash);
            int rows = await cmd.ExecuteNonQueryAsync();
            return Ok(new { succes = true, message = $"Mot de passe réinitialisé ({rows} compte(s))." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { succes = false, message = ex.Message });
        }
    }

    // ─── Supprimer (soft delete + marque DB) ───────────────────────────────────
    [HttpDelete("{code}")]
    public async Task<IActionResult> Supprimer(string code)
    {
        code = code.ToUpperInvariant();
        try
        {
            await using var conn = new SqlConnection(MasterConn);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "UPDATE Tenants SET EstActif=0 WHERE Code=@c", conn);
            cmd.Parameters.AddWithValue("@c", code);
            await cmd.ExecuteNonQueryAsync();
            _logger.LogWarning("Tenant {Code} supprimé par {User}", code,
                User.FindFirstValue("sub") ?? "system");
            return Ok(new { succes = true, message = "Compte désactivé." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { succes = false, message = ex.Message });
        }
    }

    private static string GenererCode(string rs)
    {
        var clean = System.Text.RegularExpressions.Regex.Replace(rs.ToUpper().Trim(), @"[^A-Z0-9]", "");
        return clean.Length >= 4 ? clean[..4] : clean.PadRight(4, '0');
    }

    private static bool TryValidateSetup(
        string? devise,
        string? symboleDevise,
        int nombreDecimalesMontant,
        int nombreDecimalesQuantite,
        out string? message)
    {
        if (string.IsNullOrWhiteSpace(devise))
        {
            message = "La devise est obligatoire.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(symboleDevise))
        {
            message = "Le symbole de devise est obligatoire.";
            return false;
        }

        if (nombreDecimalesMontant is < 0 or > 6)
        {
            message = "Le nombre de décimales montant doit être compris entre 0 et 6.";
            return false;
        }

        if (nombreDecimalesQuantite is < 0 or > 6)
        {
            message = "Le nombre de décimales quantité doit être compris entre 0 et 6.";
            return false;
        }

        message = null;
        return true;
    }

    private static int NormalizeDecimals(int value) => Math.Clamp(value, 0, 6);

    private static string HashPassword(string password)
    {
        var salt = new byte[16];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(salt);
        var pbkdf2 = new System.Security.Cryptography.Rfc2898DeriveBytes(
            password, salt, 10000, System.Security.Cryptography.HashAlgorithmName.SHA256);
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
}

public class TenantCreateRequest
{
    public string RaisonSociale { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? NomAdmin { get; set; }
    public string MotDePasse { get; set; } = string.Empty;
    public string Devise { get; set; } = "EUR";
    public string SymboleDevise { get; set; } = "EUR";
    public int NombreDecimalesMontant { get; set; } = 2;
    public int NombreDecimalesQuantite { get; set; } = 3;
    public bool EnvoyerEmail { get; set; } = true;
}

public class ResetPasswordRequest
{
    public string NouveauMotDePasse { get; set; } = string.Empty;
}
