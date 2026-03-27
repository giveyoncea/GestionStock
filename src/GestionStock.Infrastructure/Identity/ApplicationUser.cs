using GestionStock.Domain.Enums;
using Microsoft.AspNetCore.Identity;

namespace GestionStock.Infrastructure.Identity;

/// <summary>
/// Utilisateur ASP.NET Core Identity étendu avec les informations métier.
/// Conforme NF-SEC-02 : gestion des rôles et des droits d'accès.
/// </summary>
public class ApplicationUser : IdentityUser
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public RoleUtilisateur Role { get; set; } = RoleUtilisateur.Lecteur;
    public string? EntrepotAssocie { get; set; }
    public bool TwoFactorEnabled2 { get; set; } = false; // Override base TwoFactorEnabled
    public bool EstActif { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DerniereConnexion { get; set; }

    /// <summary>
    /// Code du tenant auquel appartient cet utilisateur.
    /// Null = super-admin (lit DefaultConnection).
    /// Valorisé = routage automatique vers GestionStock_{TenantCode}.
    /// </summary>
    public string? TenantCode { get; set; }

    public string NomComplet => $"{FirstName} {LastName}".Trim();
}
