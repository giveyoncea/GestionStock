namespace GestionStock.Application.DTOs;

public record OfflineArticleSnapshotDto(
    Guid Id,
    string Code,
    string Designation,
    string? Description,
    string? CodeBarres,
    string Unite,
    decimal PrixAchat,
    decimal PrixVente,
    decimal StockMinimum,
    bool Actif,
    DateTime? UpdatedAtUtc);

public record OfflineDepotSnapshotDto(
    Guid Id,
    string Code,
    string Libelle,
    string? Adresse,
    bool EstActif,
    DateTime? UpdatedAtUtc);

public record OfflineClientSnapshotDto(
    Guid Id,
    string Code,
    string RaisonSociale,
    string? Telephone,
    string? Email,
    string? Adresse,
    string? Ville,
    string? Pays,
    decimal PlafondCredit,
    bool EstActif,
    DateTime? UpdatedAtUtc);

public record OfflineFournisseurSnapshotDto(
    Guid Id,
    string Code,
    string RaisonSociale,
    string? Telephone,
    string? Email,
    string? Adresse,
    string? Ville,
    string? Pays,
    bool EstActif,
    DateTime? UpdatedAtUtc);

public record CommercialOfflineSyncOperationDto(
    string EntityType,
    string EntityLocalId,
    string OperationType,
    string PayloadJson,
    DateTime CreatedAtUtc);

public class CommercialOfflineClientPayloadDto
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

public class CommercialOfflineVenteLignePayloadDto
{
    public Guid ArticleId { get; set; }
    public string Designation { get; set; } = string.Empty;
    public decimal Quantite { get; set; } = 1;
    public decimal PrixUnitaireHT { get; set; }
    public decimal TauxRemise { get; set; }
    public decimal TauxTVA { get; set; } = 20;
    public string? NumeroLot { get; set; }
}

public class CommercialOfflineVentePayloadDto
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
    public List<CommercialOfflineVenteLignePayloadDto> Lignes { get; set; } = new();
}

public class CommercialOfflineAchatLignePayloadDto
{
    public Guid ArticleId { get; set; }
    public string Designation { get; set; } = string.Empty;
    public decimal Quantite { get; set; } = 1;
    public decimal PrixUnitaireHT { get; set; }
    public decimal TauxRemise { get; set; }
    public decimal TauxTVA { get; set; } = 20;
    public string? NumeroLot { get; set; }
}

public class CommercialOfflineAchatPayloadDto
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
    public List<CommercialOfflineAchatLignePayloadDto> Lignes { get; set; } = new();
}

public class CommercialOfflinePushRequestDto
{
    public string? DeviceId { get; set; }
    public string? TenantCode { get; set; }
    public string? UserId { get; set; }
    public List<CommercialOfflineSyncOperationDto> Operations { get; set; } = new();
}

public record CommercialOfflineSyncOperationResultDto(
    string EntityType,
    string EntityLocalId,
    string OperationType,
    bool Success,
    string Status,
    string? Message,
    string? ServerId,
    string? ServerNumero);

public class CommercialOfflinePushResponseDto
{
    public DateTime ServerUtc { get; set; } = DateTime.UtcNow;
    public List<CommercialOfflineSyncOperationResultDto> Results { get; set; } = new();
}

public class CommercialOfflineBootstrapDto
{
    public DateTime ServerUtc { get; set; } = DateTime.UtcNow;
    public int SchemaVersion { get; set; } = 1;
    public ParametresDto? Parametres { get; set; }
    public List<OfflineArticleSnapshotDto> Articles { get; set; } = new();
    public List<OfflineDepotSnapshotDto> Depots { get; set; } = new();
    public List<OfflineClientSnapshotDto> Clients { get; set; } = new();
    public List<OfflineFournisseurSnapshotDto> Fournisseurs { get; set; } = new();
}
