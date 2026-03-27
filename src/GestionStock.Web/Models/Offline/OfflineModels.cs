namespace GestionStock.Web.Models;

public static class OfflineSyncStates
{
    public const string Pending = "Pending";
    public const string Sent = "Sent";
    public const string Applied = "Applied";
    public const string Rejected = "Rejected";
    public const string Conflict = "Conflict";
}

public static class OfflineDocumentStatuses
{
    public const string SaisiHorsLigne = "Saisi hors ligne";
    public const string EnAttenteDeSynchro = "En attente de synchro";
    public const string Synchronise = "Synchronise";
    public const string EnConflit = "En conflit";
    public const string Erreur = "Erreur";
}

public class SyncMetadataLocal
{
    public int Id { get; set; }
    public string TenantCode { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public DateTime? LastFullSyncUtc { get; set; }
    public DateTime? LastDeltaSyncUtc { get; set; }
    public string? AppVersion { get; set; }
    public int SchemaVersion { get; set; } = 1;
}

public class ParametresLocal
{
    public string Id { get; set; } = string.Empty;
    public string? RaisonSociale { get; set; }
    public string Devise { get; set; } = "EUR";
    public string SymboleDevise { get; set; } = "EUR";
    public int NombreDecimalesMontant { get; set; } = 2;
    public int NombreDecimalesQuantite { get; set; } = 3;
    public string? FormatImprimeDocuments { get; set; }
    public string? FormatImprimeRecus { get; set; }
    public string? GabaritInterface { get; set; }
    public string? LogoBase64 { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

public class DepotLocal
{
    public string Id { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string Nom { get; set; } = string.Empty;
    public string? Adresse { get; set; }
    public bool Actif { get; set; } = true;
    public DateTime? UpdatedAtUtc { get; set; }
}

public class ArticleLocal
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Designation { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? CodeBarres { get; set; }
    public string? Unite { get; set; }
    public decimal PrixAchat { get; set; }
    public decimal PrixVente { get; set; }
    public decimal StockMinimum { get; set; }
    public bool Actif { get; set; } = true;
    public DateTime? UpdatedAtUtc { get; set; }
}

public class StockResumeLocal
{
    public int Id { get; set; }
    public string ArticleId { get; set; } = string.Empty;
    public string DepotId { get; set; } = string.Empty;
    public decimal QuantiteDisponible { get; set; }
    public decimal QuantiteReservee { get; set; }
    public decimal QuantiteTheorique { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

public abstract class TiersLocalBase
{
    public string Id { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string Nom { get; set; } = string.Empty;
    public string? Telephone { get; set; }
    public string? Email { get; set; }
    public string? Adresse { get; set; }
    public string? Ville { get; set; }
    public string? Pays { get; set; }
    public bool Actif { get; set; } = true;
    public DateTime? UpdatedAtUtc { get; set; }
}

public class ClientLocal : TiersLocalBase
{
    public decimal PlafondCredit { get; set; }
}

public class FournisseurLocal : TiersLocalBase
{
}

public abstract class DocumentLocalBase
{
    public string LocalId { get; set; } = Guid.NewGuid().ToString("N");
    public string? ServerId { get; set; }
    public string? NumeroLocal { get; set; }
    public string? NumeroServeur { get; set; }
    public int TypeDocument { get; set; }
    public string StatutLocal { get; set; } = OfflineDocumentStatuses.SaisiHorsLigne;
    public string? StatutServeur { get; set; }
    public string? DepotId { get; set; }
    public DateTime DateDocument { get; set; } = DateTime.UtcNow;
    public DateTime? DateEcheance { get; set; }
    public decimal SousTotalHt { get; set; }
    public decimal TotalTva { get; set; }
    public decimal TotalTtc { get; set; }
    public decimal Solde { get; set; }
    public string? Commentaire { get; set; }
    public bool IsDirty { get; set; } = true;
    public bool IsDeleted { get; set; }
    public string SyncState { get; set; } = OfflineSyncStates.Pending;
    public DateTime? LastSyncUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class DocumentVenteLocal : DocumentLocalBase
{
    public string? ClientId { get; set; }
}

public class LigneVenteLocal
{
    public string LocalId { get; set; } = Guid.NewGuid().ToString("N");
    public string DocumentLocalId { get; set; } = string.Empty;
    public string ArticleId { get; set; } = string.Empty;
    public decimal Quantite { get; set; }
    public decimal PrixUnitaireHt { get; set; }
    public decimal TauxTva { get; set; }
    public decimal Remise { get; set; }
    public decimal TotalLigneTtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class DocumentAchatLocal : DocumentLocalBase
{
    public string? FournisseurId { get; set; }
}

public class LigneAchatLocal
{
    public string LocalId { get; set; } = Guid.NewGuid().ToString("N");
    public string DocumentLocalId { get; set; } = string.Empty;
    public string ArticleId { get; set; } = string.Empty;
    public decimal Quantite { get; set; }
    public decimal PrixUnitaireHt { get; set; }
    public decimal TauxTva { get; set; }
    public decimal Remise { get; set; }
    public decimal TotalLigneTtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class ReglementLocal
{
    public string LocalId { get; set; } = Guid.NewGuid().ToString("N");
    public string? ServerId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string? DocumentServerId { get; set; }
    public string? DocumentLocalId { get; set; }
    public string TiersType { get; set; } = string.Empty;
    public string? TiersId { get; set; }
    public DateTime DateReglement { get; set; } = DateTime.UtcNow;
    public decimal Montant { get; set; }
    public string? ModeReglement { get; set; }
    public string? Reference { get; set; }
    public string? Commentaire { get; set; }
    public string StatutLocal { get; set; } = OfflineDocumentStatuses.SaisiHorsLigne;
    public string SyncState { get; set; } = OfflineSyncStates.Pending;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class AcompteLocal
{
    public string LocalId { get; set; } = Guid.NewGuid().ToString("N");
    public string? ServerId { get; set; }
    public string TiersType { get; set; } = string.Empty;
    public string? TiersId { get; set; }
    public DateTime DateAcompte { get; set; } = DateTime.UtcNow;
    public decimal Montant { get; set; }
    public string? ModeReglement { get; set; }
    public string? Reference { get; set; }
    public string? Commentaire { get; set; }
    public string StatutLocal { get; set; } = OfflineDocumentStatuses.SaisiHorsLigne;
    public string SyncState { get; set; } = OfflineSyncStates.Pending;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class MouvementLocal
{
    public string LocalId { get; set; } = Guid.NewGuid().ToString("N");
    public string? ServerId { get; set; }
    public string ArticleId { get; set; } = string.Empty;
    public string DepotId { get; set; } = string.Empty;
    public string TypeMouvement { get; set; } = string.Empty;
    public decimal Quantite { get; set; }
    public DateTime DateMouvement { get; set; } = DateTime.UtcNow;
    public string? ReferenceDocument { get; set; }
    public string? Commentaire { get; set; }
    public string SyncState { get; set; } = OfflineSyncStates.Pending;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class SyncQueueItemLocal
{
    public int Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string EntityLocalId { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public string Status { get; set; } = OfflineSyncStates.Pending;
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastAttemptUtc { get; set; }
}

public class SyncConflictLocal
{
    public int Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string EntityLocalId { get; set; } = string.Empty;
    public string ConflictType { get; set; } = string.Empty;
    public string? LocalPayloadJson { get; set; }
    public string? ServerPayloadJson { get; set; }
    public string ResolutionStatus { get; set; } = "Open";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAtUtc { get; set; }
}
