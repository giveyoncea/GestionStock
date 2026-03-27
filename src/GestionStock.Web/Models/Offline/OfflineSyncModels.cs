namespace GestionStock.Web.Models;

public class OfflineArticleSnapshotDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Designation { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? CodeBarres { get; set; }
    public string Unite { get; set; } = "PCS";
    public decimal PrixAchat { get; set; }
    public decimal PrixVente { get; set; }
    public decimal StockMinimum { get; set; }
    public bool Actif { get; set; } = true;
    public DateTime? UpdatedAtUtc { get; set; }
}

public class OfflineDepotSnapshotDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Libelle { get; set; } = string.Empty;
    public string? Adresse { get; set; }
    public bool EstActif { get; set; } = true;
    public DateTime? UpdatedAtUtc { get; set; }
}

public class OfflineClientSnapshotDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string RaisonSociale { get; set; } = string.Empty;
    public string? Telephone { get; set; }
    public string? Email { get; set; }
    public string? Adresse { get; set; }
    public string? Ville { get; set; }
    public string? Pays { get; set; }
    public decimal PlafondCredit { get; set; }
    public bool EstActif { get; set; } = true;
    public DateTime? UpdatedAtUtc { get; set; }
}

public class OfflineFournisseurSnapshotDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string RaisonSociale { get; set; } = string.Empty;
    public string? Telephone { get; set; }
    public string? Email { get; set; }
    public string? Adresse { get; set; }
    public string? Ville { get; set; }
    public string? Pays { get; set; }
    public bool EstActif { get; set; } = true;
    public DateTime? UpdatedAtUtc { get; set; }
}

public class OfflineClientPayloadDto
{
    public Guid? ServerId { get; set; }
    public DateTime? LastKnownServerUpdatedAtUtc { get; set; }
    public string RaisonSociale { get; set; } = string.Empty;
    public int TypeClient { get; set; } = 2;
    public string? Email { get; set; }
    public string? Telephone { get; set; }
    public string? Adresse { get; set; }
    public string? CodePostal { get; set; }
    public string? Ville { get; set; }
    public string? Pays { get; set; } = "France";
    public string? NumeroTVA { get; set; }
    public string? Siret { get; set; }
    public int DelaiPaiementJours { get; set; } = 30;
    public decimal TauxRemise { get; set; }
    public decimal PlafondCredit { get; set; }
    public string? Notes { get; set; }
    public bool EstActif { get; set; } = true;
}

public class OfflineVenteLignePayloadDto
{
    public Guid ArticleId { get; set; }
    public string Designation { get; set; } = string.Empty;
    public decimal Quantite { get; set; } = 1;
    public decimal PrixUnitaireHT { get; set; }
    public decimal TauxRemise { get; set; }
    public decimal TauxTVA { get; set; } = 20;
    public string? NumeroLot { get; set; }
}

public class OfflineVentePayloadDto
{
    public Guid? ServerId { get; set; }
    public int TypeDocument { get; set; } = 1;
    public Guid ClientId { get; set; }
    public Guid? RepresentantId { get; set; }
    public Guid? DocumentParentId { get; set; }
    public DateTime? DateDocument { get; set; }
    public DateTime? DateEcheance { get; set; }
    public DateTime? DateLivraisonPrevue { get; set; }
    public string? AdresseLivraison { get; set; }
    public Guid? DepotId { get; set; }
    public decimal FraisLivraison { get; set; }
    public decimal MontantAcompte { get; set; }
    public decimal TauxTVA { get; set; } = 20;
    public string? ConditionsPaiement { get; set; }
    public string? NotesInternes { get; set; }
    public string? NotesExterne { get; set; }
    public List<OfflineVenteLignePayloadDto> Lignes { get; set; } = new();
}

public class OfflineAchatLignePayloadDto
{
    public Guid ArticleId { get; set; }
    public string Designation { get; set; } = string.Empty;
    public decimal Quantite { get; set; } = 1;
    public decimal PrixUnitaireHT { get; set; }
    public decimal TauxRemise { get; set; }
    public decimal TauxTVA { get; set; } = 20;
    public string? NumeroLot { get; set; }
}

public class OfflineAchatPayloadDto
{
    public Guid? ServerId { get; set; }
    public int TypeDocument { get; set; } = 1;
    public Guid FournisseurId { get; set; }
    public Guid? DocumentParentId { get; set; }
    public DateTime? DateDocument { get; set; }
    public DateTime? DateLivraisonPrevue { get; set; }
    public Guid? DepotId { get; set; }
    public decimal FraisLivraison { get; set; }
    public string? NotesInternes { get; set; }
    public List<OfflineAchatLignePayloadDto> Lignes { get; set; } = new();
}

public class OfflineSyncOperationDto
{
    public string EntityType { get; set; } = string.Empty;
    public string EntityLocalId { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class OfflinePushRequestDto
{
    public string? DeviceId { get; set; }
    public string? TenantCode { get; set; }
    public string? UserId { get; set; }
    public List<OfflineSyncOperationDto> Operations { get; set; } = new();
}

public class OfflineSyncOperationResultDto
{
    public string EntityType { get; set; } = string.Empty;
    public string EntityLocalId { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? ServerId { get; set; }
    public string? ServerNumero { get; set; }
}

public class OfflinePushResponseDto
{
    public DateTime ServerUtc { get; set; } = DateTime.UtcNow;
    public List<OfflineSyncOperationResultDto> Results { get; set; } = new();
}

public class OfflineBootstrapResponseDto
{
    public DateTime ServerUtc { get; set; } = DateTime.UtcNow;
    public int SchemaVersion { get; set; } = 1;
    public ParametresDto? Parametres { get; set; }
    public List<OfflineArticleSnapshotDto> Articles { get; set; } = new();
    public List<OfflineDepotSnapshotDto> Depots { get; set; } = new();
    public List<OfflineClientSnapshotDto> Clients { get; set; } = new();
    public List<OfflineFournisseurSnapshotDto> Fournisseurs { get; set; } = new();
}

public class OfflineSyncSummaryDto
{
    public int AppliedCount { get; set; }
    public int ConflictCount { get; set; }
    public int RejectedCount { get; set; }
    public int PendingCount { get; set; }
    public bool HasErrors => ConflictCount > 0 || RejectedCount > 0;
}
