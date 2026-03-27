namespace GestionStock.Web.Models;

public record LoginRequest(string Email, string MotDePasse);

public record AuthResult(
    bool Succes, string? Token, string? RefreshToken,
    DateTime? Expiration, string? UserId, string? Email,
    string? NomComplet, string? Role, string? Message);

public record ResultDto(bool Succes, string? Message, object? Data);

public record PagedResult<T>(
    IEnumerable<T> Items, int TotalCount,
    int Page, int PageSize, int TotalPages);

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

public class InscriptionResultat
{
    public bool Succes { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? TenantCode { get; set; }
    public string? LoginEmail { get; set; }
    public string? AppUrl { get; set; }
    public bool EmailEnvoye { get; set; }
}

public class TenantDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string RaisonSociale { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string AdminNom { get; set; } = string.Empty;
    public string BaseDeDonnees { get; set; } = string.Empty;
    public bool EstActif { get; set; }
    public DateTime DateCreation { get; set; }
}

public class TenantDetailDto
{
    public TenantDto? Tenant { get; set; }
    public TenantStatsDto? Stats { get; set; }
}

public class TenantStatsDto
{
    public int Articles { get; set; }
    public int Fournisseurs { get; set; }
    public decimal ValeurStock { get; set; }
    public int TotalMouvements { get; set; }
    public int Utilisateurs { get; set; }
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

public class UtilisateurDto
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string NomComplet { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public int Role { get; set; }
    public string RoleLibelle { get; set; } = string.Empty;
    public bool EstActif { get; set; }
    public bool EstVerrouille { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DerniereConnexion { get; set; }
    public string? EntrepotAssocie { get; set; }
    public string[] Permissions { get; set; } = Array.Empty<string>();
    public bool HasCustomPermissions { get; set; }
}

public class RoleDto
{
    public int Id { get; set; }
    public string Libelle { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string[] Permissions { get; set; } = Array.Empty<string>();
    public bool IsCustom { get; set; }
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

public class ResetMdpRequest
{
    public string NouveauMotDePasse { get; set; } = "";
}

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

public class PermissionsDetailDto
{
    public int? RoleId { get; set; }
    public string? UserId { get; set; }
    public int? Role { get; set; }
    public string[] PermissionsRole { get; set; } = Array.Empty<string>();
    public string[]? PermissionsUtilisateur { get; set; }
    public string[] PermissionsEffectives { get; set; } = Array.Empty<string>();
    public string[] Tous { get; set; } = Array.Empty<string>();
    public string[] Permissions { get; set; } = Array.Empty<string>();
}

public class RoleCompletDto
{
    public int Id { get; set; }
    public string Libelle { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string[] Permissions { get; set; } = Array.Empty<string>();
    public string? Couleur { get; set; }
    public bool EstActif { get; set; }
    public bool EstSysteme { get; set; }
    public DateTime CreatedAt { get; set; }
    public int NbUtilisateurs { get; set; }
}

public class RoleRequest
{
    public string Libelle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string[]? Permissions { get; set; }
    public string? Couleur { get; set; }
}
