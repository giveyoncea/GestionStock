using GestionStock.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GestionStock.Domain.Enums;

namespace GestionStock.API.Controllers;

[ApiController]
[Route("api/auth/tenant")]
[AllowAnonymous]
public class TenantAuthController : ControllerBase
{
    private const int MaxFailedAccessAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private readonly IConfiguration _config;
    private readonly ITenantService _tenant;
    private readonly ILogger<TenantAuthController> _logger;
    private string MasterConn => _config.GetConnectionString("MasterConnection")!;

    public TenantAuthController(
        IConfiguration config, ITenantService tenant,
        ILogger<TenantAuthController> logger)
    {
        _config = config;
        _tenant = tenant;
        _logger = logger;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] TenantLoginRequest dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email) ||
            string.IsNullOrWhiteSpace(dto.MotDePasse) ||
            string.IsNullOrWhiteSpace(dto.TenantCode))
            return BadRequest(new { succes = false, message = "Email, mot de passe et code tenant obligatoires." });

        var tenantCode = dto.TenantCode.Trim().ToUpperInvariant();
        var connStr = _tenant.GetConnectionString(tenantCode);

        try
        {
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            // VÃ©rifier que la DB existe et rÃ©cupÃ©rer l'utilisateur
            await using var cmd = new SqlCommand(
                @"SELECT Id, Email, PasswordHash, NomComplet, Role, LockoutEnabled,
                         LockoutEnd, AccessFailedCount
                  FROM AspNetUsers
                  WHERE NormalizedEmail = @email", conn);
            cmd.Parameters.AddWithValue("@email", dto.Email.Trim().ToUpperInvariant());

            string? userId = null, passwordHash = null, nomComplet = null;
            int role = 0;
            bool lockoutEnabled = false;
            DateTimeOffset? lockoutEnd = null;
            int accessFailed = 0;

            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync())
                    return Ok(new { succes = false, message = "Email ou mot de passe incorrect." });

                userId = reader.GetString(0);
                passwordHash = reader.GetString(2);
                nomComplet = reader.IsDBNull(3) ? dto.Email : reader.GetString(3);
                role = reader.GetInt32(4);
                lockoutEnabled = reader.GetBoolean(5);
                lockoutEnd = reader.IsDBNull(6) ? null : reader.GetDateTimeOffset(6);
                accessFailed = reader.GetInt32(7);
            }

            // VÃ©rifier verrouillage
            if (lockoutEnabled && lockoutEnd.HasValue && lockoutEnd > DateTimeOffset.UtcNow)
                return Ok(new { succes = false, message = "Compte verrouillÃ©. RÃ©essayez dans quelques minutes." });

            // VÃ©rifier mot de passe
            if (!VerifyPassword(dto.MotDePasse, passwordHash!))
            {
                // IncrÃ©menter Ã©chec
                await using var fail = new SqlCommand(
                    "UPDATE AspNetUsers SET AccessFailedCount=AccessFailedCount+1 WHERE Id=@id", conn);
                fail.Parameters.AddWithValue("@id", userId);
                await fail.ExecuteNonQueryAsync();
                return Ok(new { succes = false, message = "Email ou mot de passe incorrect." });
            }

            // RÃ©initialiser compteur
            await using var reset = new SqlCommand(
                "UPDATE AspNetUsers SET AccessFailedCount=0 WHERE Id=@id", conn);
            reset.Parameters.AddWithValue("@id", userId);
            await reset.ExecuteNonQueryAsync();

            // GÃ©nÃ©rer JWT avec claim tenant
            var roleStr = role switch { 1 => "Admin", 2 => "Superviseur", _ => "Utilisateur" };
            var (token, expiration) = GenererJwt(userId!, dto.Email, nomComplet!, roleStr, tenantCode);

            // RÃ©cupÃ©rer infos du tenant depuis la base Master
            string? baseDeDonnees = null;
            DateTime? dateCreation = null;
            try
            {
                await using var masterConn = new SqlConnection(MasterConn);
                await masterConn.OpenAsync();
                await using var infoCmd = new SqlCommand(
                    "SELECT BaseDeDonnees, DateCreation FROM Tenants WHERE Code=@c",
                    masterConn);
                infoCmd.Parameters.AddWithValue("@c", tenantCode);
                await using var infoR = await infoCmd.ExecuteReaderAsync();
                if (await infoR.ReadAsync())
                {
                    baseDeDonnees = infoR.IsDBNull(0) ? null : infoR.GetString(0);
                    dateCreation  = infoR.IsDBNull(1) ? null : infoR.GetDateTime(1);
                }
            }
            catch { /* non bloquant */ }

            _logger.LogInformation("Connexion tenant {Tenant}: {Email}", tenantCode, dto.Email);

            return Ok(new
            {
                succes = true,
                token,
                expiration,
                userId,
                email = dto.Email,
                nomComplet,
                role = roleStr,
                tenantCode,
                baseDeDonnees,
                dateCreation,
                message = "Connexion rÃ©ussie."
            });
        }
        catch (SqlException ex) when (ex.Number == 4060)
        {
            // Base de donnÃ©es inexistante
            return Ok(new { succes = false, message = $"Code tenant '{tenantCode}' introuvable." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur connexion tenant {Tenant}", tenantCode);
            return Ok(new { succes = false, message = "Erreur de connexion." });
        }
    }

    private (string Token, DateTime Expiration) GenererJwt(
        string userId, string email, string nomComplet, string role, string tenantCode)
    {
        var jwtSettings = _config.GetSection("JwtSettings");
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtSettings["SecretKey"]!));

        var expiration = DateTime.UtcNow.AddHours(
            int.Parse(jwtSettings["ExpirationHeures"] ?? "8"));

        var claims = new[]
        {
            new Claim("sub",    userId),
            new Claim("email",  email),
            new Claim("name",   nomComplet),
            new Claim("role",   role),
            new Claim("tenant", tenantCode),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64)
        };

        var token = new JwtSecurityToken(
            issuer: jwtSettings["Issuer"],
            audience: jwtSettings["Audience"],
            claims: claims,
            expires: expiration,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expiration);
    }

    private static bool VerifyPassword(string password, string hash)
    {
        try
        {
            var decoded = Convert.FromBase64String(hash);
            if (decoded[0] != 0x01) return false; // V3 only

            var iterCount = (int)System.Buffers.Binary.BinaryPrimitives
                .ReadUInt32BigEndian(decoded.AsSpan(5));
            var saltLen = (int)System.Buffers.Binary.BinaryPrimitives
                .ReadUInt32BigEndian(decoded.AsSpan(9));

            var salt = decoded[13..(13 + saltLen)];
            var storedKey = decoded[(13 + saltLen)..];

            var pbkdf2 = new System.Security.Cryptography.Rfc2898DeriveBytes(
                password, salt, iterCount,
                System.Security.Cryptography.HashAlgorithmName.SHA256);
            var actualKey = pbkdf2.GetBytes(storedKey.Length);
            return actualKey.SequenceEqual(storedKey);
        }
        catch { return false; }
    }

    private static string MapRole(int role)
        => Enum.IsDefined(typeof(RoleUtilisateur), role)
            ? ((RoleUtilisateur)role).ToString()
            : RoleUtilisateur.Lecteur.ToString();
}

public class TenantLoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string MotDePasse { get; set; } = string.Empty;
    public string TenantCode { get; set; } = string.Empty;
}

