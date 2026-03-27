using FluentValidation;
using GestionStock.Application.DTOs;
using GestionStock.Application.Interfaces;
using GestionStock.Infrastructure.Services;
using GestionStock.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using System.Security.Claims;

namespace GestionStock.API.Controllers;

// ─── BASE CONTROLLER ─────────────────────────────────────────────────────────
[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
public abstract class BaseController : ControllerBase
{
    protected string UserId => User.FindFirstValue("sub") ?? "system";
    protected string UserEmail => User.FindFirstValue(ClaimTypes.Email) ?? string.Empty;

    protected IActionResult HandleResult(ResultDto result)
        => result.Succes ? Ok(result) : BadRequest(result);
}

// ─── AUTH CONTROLLER ─────────────────────────────────────────────────────────
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
[Tags("Authentification")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    public AuthController(IAuthService authService) => _authService = authService;

    [HttpPost("login")]
    [SwaggerOperation(Summary = "Connexion utilisateur")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var result = await _authService.LoginAsync(request.Email, request.MotDePasse);
        if (!result.Succes) return Unauthorized(new { message = result.Message });
        return Ok(result);
    }

    [HttpGet("diagnostic")]
    public async Task<IActionResult> Diagnostic(
        [FromServices] UserManager<ApplicationUser> userManager)
    {
        try
        {
            var user = await userManager.FindByEmailAsync("admin@gestionstock.com");
            if (user is null) return Ok(new { existe = false, message = "Compte admin introuvable." });
            var roles = await userManager.GetRolesAsync(user);
            return Ok(new { existe = true, email = user.Email, estActif = user.EstActif, roles });
        }
        catch (Exception)
        {
            return StatusCode(500, new { succes = false, message = "Une erreur interne est survenue.",
                code = 500, timestamp = DateTime.UtcNow });
        }
    }

    [HttpPost("setup")]
    public async Task<IActionResult> Setup(
        [FromServices] UserManager<ApplicationUser> userManager,
        [FromServices] RoleManager<IdentityRole> roleManager,
        [FromServices] IConfiguration config)
    {
        var log = new List<string>();
        string[] roles = ["Admin", "Magasinier", "Acheteur", "Superviseur", "Lecteur"];
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var r = await roleManager.CreateAsync(new IdentityRole(role));
                log.Add(r.Succeeded ? $"Role cree: {role}" : $"Role echoue: {role}");
            }
            else log.Add($"Role existant: {role}");
        }
        var email    = config["DefaultAdmin:Email"]    ?? "admin@gestionstock.com";
        var password = config["DefaultAdmin:Password"] ?? "Admin@2024!Stock";
        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(existing);
            await userManager.ResetPasswordAsync(existing, token, password);
            existing.EmailConfirmed = true; existing.EstActif = true;
            await userManager.UpdateAsync(existing);
            log.Add($"Admin reinitialise : {email}");
        }
        else
        {
            var user = new ApplicationUser
            {
                UserName = email, Email = email,
                FirstName = "Administrateur", LastName = "Systeme",
                Role = GestionStock.Domain.Enums.RoleUtilisateur.Admin,
                EmailConfirmed = true, EstActif = true
            };
            var result = await userManager.CreateAsync(user, password);
            if (result.Succeeded) { await userManager.AddToRoleAsync(user, "Admin"); log.Add($"Admin cree : {email}"); }
            else log.Add($"Creation echouee : {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }
        return Ok(new { email, password, log });
    }

    public record LoginRequest(string Email, string MotDePasse);
}
