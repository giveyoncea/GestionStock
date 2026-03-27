using GestionStock.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;

namespace GestionStock.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RegistrationController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ProvisioningService _provisioning;
    private readonly IEmailService _email;
    private readonly ILogger<RegistrationController> _logger;

    public RegistrationController(
        IConfiguration config,
        ProvisioningService provisioning,
        IEmailService email,
        ILogger<RegistrationController> logger)
    {
        _config = config;
        _provisioning = provisioning;
        _email = email;
        _logger = logger;
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Inscrire([FromBody] InscriptionRequest dto)
    {
        // ─── Validation
        if (string.IsNullOrWhiteSpace(dto.RaisonSociale))
            return BadRequest(new { succes = false, message = "La raison sociale est obligatoire." });
        if (string.IsNullOrWhiteSpace(dto.Email) || !dto.Email.Contains('@'))
            return BadRequest(new { succes = false, message = "Email invalide." });
        if (string.IsNullOrWhiteSpace(dto.MotDePasse) || dto.MotDePasse.Length < 8)
            return BadRequest(new { succes = false, message = "Le mot de passe doit contenir au moins 8 caractères." });
        if (dto.MotDePasse != dto.ConfirmationMotDePasse)
            return BadRequest(new { succes = false, message = "Les mots de passe ne correspondent pas." });
        if (!TryValidateSetup(dto.Devise, dto.SymboleDevise, dto.NombreDecimalesMontant, dto.NombreDecimalesQuantite, out var validationMessage))
            return BadRequest(new { succes = false, message = validationMessage });

        // ─── Génération du code tenant
        var tenantCode = GenererCodeTenant(dto.RaisonSociale);

        try
        {
            // ─── Vérifier unicité dans le master
            var masterConn = _config.GetConnectionString("MasterConnection")!;
            await using (var conn = new SqlConnection(masterConn))
            {
                await conn.OpenAsync();

                // Créer table Tenants si pas encore existante
                await using (var createCmd = new SqlCommand(@"
                    IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Tenants' AND xtype='U')
                    CREATE TABLE Tenants (
                        Id uniqueidentifier NOT NULL PRIMARY KEY DEFAULT NEWID(),
                        Code nvarchar(20) NOT NULL UNIQUE,
                        RaisonSociale nvarchar(200) NOT NULL,
                        Email nvarchar(256) NOT NULL,
                        AdminNom nvarchar(200) NOT NULL,
                        BaseDeDonnees nvarchar(100) NOT NULL,
                        EstActif bit NOT NULL DEFAULT 1,
                        DateCreation datetime2 NOT NULL DEFAULT GETUTCDATE()
                    )", conn))
                {
                    await createCmd.ExecuteNonQueryAsync();
                }

                // Vérifier email unique
                await using (var chk = new SqlCommand(
                    "SELECT COUNT(1) FROM Tenants WHERE Email=@e", conn))
                {
                    chk.Parameters.AddWithValue("@e", dto.Email.Trim().ToLower());
                    if (Convert.ToInt32(await chk.ExecuteScalarAsync() ?? 0) > 0)
                        return BadRequest(new { succes = false, message = "Un compte existe déjà avec cet email." });
                }

                // Vérifier code unique et générer un code disponible
                int suffix = 0;
                var finalCode = tenantCode;
                while (true)
                {
                    await using var chk2 = new SqlCommand(
                        "SELECT COUNT(1) FROM Tenants WHERE Code=@c", conn);
                    chk2.Parameters.AddWithValue("@c", finalCode);
                    if (Convert.ToInt32(await chk2.ExecuteScalarAsync() ?? 0) == 0) break;
                    suffix++;
                    finalCode = $"{tenantCode}{suffix}";
                }
                tenantCode = finalCode;
            }

            // ─── Provisioner la base de données
            var adminNom = string.IsNullOrWhiteSpace(dto.NomAdministrateur)
                ? dto.RaisonSociale
                : dto.NomAdministrateur;

            await _provisioning.ProvisionnerTenantAsync(
                tenantCode, dto.Email, dto.MotDePasse, adminNom, dto.RaisonSociale,
                dto.Devise.Trim().ToUpperInvariant(), dto.SymboleDevise.Trim(),
                NormalizeDecimals(dto.NombreDecimalesMontant), NormalizeDecimals(dto.NombreDecimalesQuantite));

            // ─── Enregistrer dans le master
            var dbName = $"GestionStock_{tenantCode.ToUpper()}";
            var masterConn2 = _config.GetConnectionString("MasterConnection")!;
            await using (var conn = new SqlConnection(masterConn2))
            {
                await conn.OpenAsync();
                await using var ins = new SqlCommand(@"
                    INSERT INTO Tenants (Id,Code,RaisonSociale,Email,AdminNom,BaseDeDonnees,EstActif,DateCreation)
                    VALUES (NEWID(),@code,@rs,@email,@nom,@db,1,GETUTCDATE())", conn);
                ins.Parameters.AddWithValue("@code", tenantCode);
                ins.Parameters.AddWithValue("@rs", dto.RaisonSociale.Trim());
                ins.Parameters.AddWithValue("@email", dto.Email.Trim().ToLower());
                ins.Parameters.AddWithValue("@nom", adminNom);
                ins.Parameters.AddWithValue("@db", dbName);
                await ins.ExecuteNonQueryAsync();
            }

            // ─── Envoi email de bienvenue
            var appUrl = _config["AppSettings:AppUrl"] ?? "http://localhost:5000";
            var appName = _config["AppSettings:AppName"] ?? "GestionStock";
            var htmlEmail = BuildWelcomeEmail(appName, dto.RaisonSociale,
                adminNom, dto.Email, tenantCode, appUrl);
            var emailEnvoye = await _email.SendAsync(dto.Email, $"Bienvenue sur {appName} – Vos accès", htmlEmail);

            _logger.LogInformation("Nouveau tenant créé: {Code} – {Rs}", tenantCode, dto.RaisonSociale);

            return Ok(new
            {
                succes = true,
                message = emailEnvoye
                    ? "Compte créé ! Un email avec vos accès a été envoyé."
                    : "Compte créé ! Email non envoyé (vérifiez la config SMTP dans appsettings.json).",
                tenantCode,
                loginEmail = dto.Email,
                emailEnvoye,
                appUrl = $"{appUrl}/login"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur provisioning tenant {Code}", tenantCode);
            return StatusCode(500, new
            {
                succes = false,
                message = $"Erreur lors de la création du compte : {ex.InnerException?.Message ?? ex.Message}"
            });
        }
    }

    [HttpGet("test-smtp")]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> TestSmtp([FromQuery] string to)
    {
        if (string.IsNullOrWhiteSpace(to))
            return BadRequest(new { succes = false, message = "Paramètre ?to= requis." });
        try
        {
            await _email.SendAsync(to, "Test SMTP – GestionStock",
                "<p>Test de configuration SMTP réussi !</p><p>GestionStock</p>");
            return Ok(new { succes = true, message = $"Email de test envoyé à {to}." });
        }
        catch (Exception ex)
        {
            return Ok(new { succes = false, message = $"Échec SMTP : {ex.Message}" });
        }
    }

    [HttpGet("disponibilite")]
    [AllowAnonymous]
    public async Task<IActionResult> VérifierEmail([FromQuery] string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return Ok(new { disponible = false });
        try
        {
            var masterConn = _config.GetConnectionString("MasterConnection")!;
            await using var conn = new SqlConnection(masterConn);
            await conn.OpenAsync();

            // Vérifier que la table existe
            await using var exists = new SqlCommand(
                "SELECT CASE WHEN EXISTS (SELECT 1 FROM sysobjects WHERE name='Tenants' AND xtype='U') THEN 1 ELSE 0 END", conn);
            if (Convert.ToInt32(await exists.ExecuteScalarAsync() ?? 0) == 0)
                return Ok(new { disponible = true });

            await using var chk = new SqlCommand("SELECT COUNT(1) FROM Tenants WHERE Email=@e", conn);
            chk.Parameters.AddWithValue("@e", email.Trim().ToLower());
            var count = Convert.ToInt32(await chk.ExecuteScalarAsync() ?? 0);
            return Ok(new { disponible = count == 0 });
        }
        catch { return Ok(new { disponible = true }); }
    }

    private static string GenererCodeTenant(string raisonSociale)
    {
        var clean = Regex.Replace(raisonSociale.ToUpper().Trim(), @"[^A-Z0-9]", "");
        if (clean.Length >= 4) return clean[..4];
        if (clean.Length > 0) return clean.PadRight(4, '0');
        return "CLIE";
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

    private string BuildWelcomeEmail(string appName, string raisonSociale,
        string adminNom, string email, string tenantCode, string appUrl)
    {
        return $@"
<!DOCTYPE html>
<html lang=""fr"">
<head><meta charset=""UTF-8""><meta name=""viewport"" content=""width=device-width""></head>
<body style=""font-family:Arial,sans-serif;background:#f4f4f4;margin:0;padding:20px"">
  <div style=""max-width:600px;margin:0 auto;background:#fff;border-radius:8px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,.1)"">

    <div style=""background:#1a1a2e;padding:30px;text-align:center"">
      <h1 style=""color:#f5a623;margin:0;font-size:28px"">{appName}</h1>
      <p style=""color:#aaa;margin:8px 0 0"">Votre solution WMS · SCM</p>
    </div>

    <div style=""padding:30px"">
      <h2 style=""color:#333"">Bienvenue, {adminNom} !</h2>
      <p style=""color:#555;line-height:1.6"">
        Votre espace <strong>{appName}</strong> pour <strong>{raisonSociale}</strong>
        a été créé avec succès.
      </p>

      <div style=""background:#f8f4ee;border-left:4px solid #f5a623;padding:20px;border-radius:4px;margin:20px 0"">
        <h3 style=""color:#333;margin:0 0 12px"">Vos informations de connexion</h3>
        <table style=""width:100%;border-collapse:collapse"">
          <tr><td style=""padding:6px 0;color:#888;width:40%"">Identifiant :</td>
              <td style=""padding:6px 0;font-weight:bold;color:#333"">{email}</td></tr>
          <tr><td style=""padding:6px 0;color:#888"">Code tenant :</td>
              <td style=""padding:6px 0;font-family:monospace;background:#e8e4dd;padding:4px 8px;border-radius:3px"">{tenantCode}</td></tr>
          <tr><td style=""padding:6px 0;color:#888"">Mot de passe :</td>
              <td style=""padding:6px 0;color:#555"">Celui choisi à l'inscription</td></tr>
          <tr><td style=""padding:6px 0;color:#888"">Rôle :</td>
              <td style=""padding:6px 0;color:#555"">Administrateur</td></tr>
        </table>
      </div>

      <div style=""text-align:center;margin:30px 0"">
        <a href=""{appUrl}/login""
           style=""background:#f5a623;color:#1a1a2e;padding:14px 32px;border-radius:6px;
                  text-decoration:none;font-weight:bold;font-size:16px;display:inline-block"">
          Accéder à mon espace →
        </a>
      </div>

      <hr style=""border:none;border-top:1px solid #eee;margin:24px 0"">
      <p style=""color:#888;font-size:13px"">
        Conservez ces informations précieusement. Si vous n'êtes pas à l'origine de cette inscription,
        ignorez cet email.
      </p>
    </div>

    <div style=""background:#f4f4f4;padding:16px;text-align:center"">
      <p style=""color:#aaa;font-size:12px;margin:0"">
        © {DateTime.Now.Year} {appName} – Tous droits réservés
      </p>
    </div>
  </div>
</body>
</html>";
    }
}

public class InscriptionRequest
{
    public string RaisonSociale { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? NomAdministrateur { get; set; }
    public string MotDePasse { get; set; } = string.Empty;
    public string ConfirmationMotDePasse { get; set; } = string.Empty;
    public string Devise { get; set; } = "EUR";
    public string SymboleDevise { get; set; } = "EUR";
    public int NombreDecimalesMontant { get; set; } = 2;
    public int NombreDecimalesQuantite { get; set; } = 3;
    public bool AccepterConditions { get; set; }
}
