using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GestionStock.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace GestionStock.Infrastructure.Services;

public interface IAuthService
{
    Task<AuthResultDto> LoginAsync(string email, string motDePasse);
    Task<AuthResultDto> InscriptionAsync(InscriptionDto dto);
}

public record AuthResultDto(
    bool Succes,
    string? Token,
    string? RefreshToken,
    DateTime? Expiration,
    string? UserId,
    string? Email,
    string? NomComplet,
    string? Role,
    string? Message = null
);

public record InscriptionDto(
    string Prenom,
    string Nom,
    string Email,
    string MotDePasse,
    string Role,
    string? EntrepotAssocie
);

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _config;

    public AuthService(UserManager<ApplicationUser> userManager, IConfiguration config)
    {
        _userManager = userManager;
        _config = config;
    }

    public async Task<AuthResultDto> LoginAsync(string email, string motDePasse)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user is null)
            return Echec("Email ou mot de passe incorrect.");

        if (!user.EstActif)
            return Echec("Compte désactivé. Contactez l'administrateur.");

        if (await _userManager.IsLockedOutAsync(user))
            return Echec("Compte verrouillé temporairement. Réessayez dans quelques minutes.");

        var passwordOk = await _userManager.CheckPasswordAsync(user, motDePasse);
        if (!passwordOk)
        {
            await _userManager.AccessFailedAsync(user);
            return Echec("Email ou mot de passe incorrect.");
        }

        await _userManager.ResetAccessFailedCountAsync(user);
        user.DerniereConnexion = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        var (token, expiration) = GenererJwt(user);

        return new AuthResultDto(
            true, token, Guid.NewGuid().ToString(),
            expiration, user.Id, user.Email,
            user.NomComplet, user.Role.ToString());
    }

    public async Task<AuthResultDto> InscriptionAsync(InscriptionDto dto)
    {
        var existant = await _userManager.FindByEmailAsync(dto.Email);
        if (existant is not null)
            return Echec("Cet email est déjà utilisé.");

        if (!Enum.TryParse<Domain.Enums.RoleUtilisateur>(dto.Role, out var role))
            return Echec("Rôle invalide.");

        var user = new ApplicationUser
        {
            UserName        = dto.Email,
            Email           = dto.Email,
            FirstName       = dto.Prenom,
            LastName        = dto.Nom,
            Role            = role,
            EntrepotAssocie = dto.EntrepotAssocie,
            EmailConfirmed  = true
        };

        var result = await _userManager.CreateAsync(user, dto.MotDePasse);
        if (!result.Succeeded)
            return Echec(string.Join(", ", result.Errors.Select(e => e.Description)));

        await _userManager.AddToRoleAsync(user, dto.Role);

        return new AuthResultDto(true, null, null, null,
            user.Id, user.Email, user.NomComplet, user.Role.ToString(),
            "Compte créé avec succès.");
    }

    private (string Token, DateTime Expiration) GenererJwt(ApplicationUser user)
    {
        var jwtSettings = _config.GetSection("JwtSettings");
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtSettings["SecretKey"]
                ?? throw new InvalidOperationException("JWT SecretKey manquante.")));

        var expiration = DateTime.UtcNow.AddHours(
            int.Parse(jwtSettings["ExpirationHeures"] ?? "8"));

        var claims = BuildClaims(user);

        var token = new JwtSecurityToken(
            issuer:             jwtSettings["Issuer"],
            audience:           jwtSettings["Audience"],
            claims:             claims,
            expires:            expiration,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expiration);
    }

    private static IEnumerable<Claim> BuildClaims(ApplicationUser user)
    {
        var claims = new List<Claim>
        {
            new("sub",      user.Id),
            new("email",    user.Email ?? string.Empty),
            new("name",     user.NomComplet),
            new("role",     user.Role.ToString()),
            new("entrepot", user.EntrepotAssocie ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64)
        };

        // Claim tenant pour le routage automatique vers la bonne base de données
        if (!string.IsNullOrWhiteSpace(user.TenantCode))
            claims.Add(new Claim("tenant", user.TenantCode.ToUpperInvariant()));

        return claims;
    }

    private static AuthResultDto Echec(string message) =>
        new(false, null, null, null, null, null, null, null, message);
}
