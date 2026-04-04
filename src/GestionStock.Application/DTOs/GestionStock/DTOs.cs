using GestionStock.Domain.Enums;

namespace GestionStock.Application.DTOs;

// ─── ARTICLE ─────────────────────────────────────────────────────────────────
public record ArticleDto(
    Guid Id,
    string Code,
    string CodeBarres,
    string Designation,
    string Description,
    string Categorie,
    string FamilleArticle,
    string Unite,
    decimal PrixAchat,
    decimal PrixVente,
    int SeuilAlerte,
    int StockMinimum,
    int StockMaximum,
    bool SansSuiviStock,
    bool GestionLot,
    bool GestionNumeroDeSerie,
    bool GestionDLUO,
    StatutArticle Statut,
    int QuantiteTotale,
    bool EstEnAlerte,
    bool EstEnRupture,
    string? FournisseurNom,
    DateTime CreatedAt
);

public record CreerArticleDto(
    string Code,
    string CodeBarres,
    string Designation,
    string Description,
    string Categorie,
    string FamilleArticle,
    string Unite,
    decimal PrixAchat,
    decimal PrixVente,
    int SeuilAlerte,
    int StockMinimum,
    int StockMaximum,
    bool SansSuiviStock,
    bool GestionLot,
    bool GestionNumeroDeSerie,
    bool GestionDLUO,
    Guid? FournisseurPrincipalId
);

public record ModifierArticleDto(
    Guid Id,
    string Designation,
    string Description,
    string Categorie,
    string FamilleArticle,
    string Unite,
    decimal PrixAchat,
    decimal PrixVente,
    int SeuilAlerte,
    int StockMinimum,
    int StockMaximum,
    bool SansSuiviStock,
    bool GestionLot,
    bool GestionNumeroDeSerie,
    bool GestionDLUO,
    Guid? FournisseurPrincipalId
);

// ─── STOCK ────────────────────────────────────────────────────────────────────
public record StockArticleDto(
    Guid Id,
    Guid ArticleId,
    string ArticleCode,
    string ArticleDesignation,
    Guid EmplacementId,
    string EmplacementCode,
    string EmplacementZone,
    int QuantiteDisponible,
    int QuantiteReservee,
    int QuantiteEnCommande,
    string? NumeroLot,
    DateTime? DatePeremption
);

public record StockResumeDto(
    Guid ArticleId,
    string ArticleCode,
    string ArticleDesignation,
    int QuantiteTotale,
    int QuantiteDisponible,
    int QuantiteReservee,
    bool EstEnAlerte,
    bool EstEnRupture,
    decimal ValeurStock
);

public record MouvementStockDto(
    Guid Id,
    string ArticleCode,
    string ArticleDesignation,
    string EmplacementSource,
    string? EmplacementDestination,
    TypeMouvement TypeMouvement,
    string TypeMouvementLibelle,
    int Quantite,
    decimal ValeurUnitaire,
    decimal ValeurTotale,
    string? Reference,
    string? Motif,
    string? NumeroLot,
    string? NumeroSerie,
    DateTime DateMouvement,
    string CreatedBy
);

public record DocumentStockDto(
    Guid Id,
    string Numero,
    int TypeDocument,
    string TypeLibelle,
    string? Reference,
    DateTime DateDocument,
    int Statut,
    string StatutLibelle,
    int NombreLignes,
    int QuantiteTotale,
    decimal ValeurTotale,
    string CreatedBy,
    DateTime? ValidatedAt,
    string? ValidatedBy,
    string? Motif,
    string ResumeArticles,
    List<LigneDocumentStockDto> Lignes
);

public record LigneDocumentStockDto(
    Guid Id,
    Guid ArticleId,
    string ArticleCode,
    string Designation,
    Guid? EmplacementSourceId,
    string? EmplacementSourceCode,
    Guid? EmplacementDestinationId,
    string? EmplacementDestinationCode,
    int Quantite,
    decimal ValeurUnitaire,
    decimal ValeurTotale,
    string? NumeroLot,
    string? NumeroSerie,
    string? Motif,
    int Ordre
);

public record EntreeStockDto(
    Guid ArticleId,
    Guid EmplacementId,
    int Quantite,
    decimal PrixUnitaire,
    string Reference,
    string? NumeroLot,
    string? NumeroSerie,
    DateTime? DatePeremption,
    string? Motif
);

public class CreerDocumentStockEntreeDto
{
    public DateTime DateDocument { get; set; } = DateTime.Today;
    public string Reference { get; set; } = string.Empty;
    public string? Motif { get; set; }
    public List<CreerLigneDocumentStockEntreeDto> Lignes { get; set; } = new();
}

public class CreerLigneDocumentStockEntreeDto
{
    public Guid ArticleId { get; set; }
    public Guid EmplacementId { get; set; }
    public int Quantite { get; set; }
    public decimal PrixUnitaire { get; set; }
    public string? NumeroLot { get; set; }
    public string? NumeroSerie { get; set; }
    public DateTime? DatePeremption { get; set; }
    public string? Motif { get; set; }
}

public class CreerDocumentStockSortieDto
{
    public DateTime DateDocument { get; set; } = DateTime.Today;
    public string Reference { get; set; } = string.Empty;
    public string? Motif { get; set; }
    public List<CreerLigneDocumentStockSortieDto> Lignes { get; set; } = new();
}

public class CreerLigneDocumentStockSortieDto
{
    public Guid ArticleId { get; set; }
    public Guid EmplacementId { get; set; }
    public int Quantite { get; set; }
    public decimal PrixUnitaire { get; set; }
    public string? NumeroLot { get; set; }
    public string? NumeroSerie { get; set; }
    public string? Motif { get; set; }
}

public record SortieStockDto(
    Guid ArticleId,
    Guid EmplacementId,
    int Quantite,
    string Reference,
    string? NumeroLot,
    string? NumeroSerie,
    string? Motif
);

public record TransfertStockDto(
    Guid ArticleId,
    Guid EmplacementSourceId,
    Guid EmplacementDestinationId,
    int Quantite,
    string? NumeroLot
);

public class CreerDocumentStockTransfertDto
{
    public DateTime DateDocument { get; set; } = DateTime.Today;
    public string Reference { get; set; } = string.Empty;
    public Guid EmplacementSourceId { get; set; }
    public Guid EmplacementDestinationId { get; set; }
    public string? Demandeur { get; set; }
    public string? Motif { get; set; }
    public List<CreerLigneDocumentStockTransfertDto> Lignes { get; set; } = new();
}

public class CreerLigneDocumentStockTransfertDto
{
    public Guid ArticleId { get; set; }
    public int Quantite { get; set; }
    public decimal PrixUnitaire { get; set; }
    public string? NumeroLot { get; set; }
    public string? NumeroSerie { get; set; }
    public string? Motif { get; set; }
}

public record AjustementStockDto(
    Guid ArticleId,
    Guid EmplacementId,
    int QuantiteReelle,
    string? Motif
);

// ─── FOURNISSEUR ─────────────────────────────────────────────────────────────
public record FournisseurDto(
    Guid Id,
    string Code,
    string RaisonSociale,
    string Email,
    string Telephone,
    string Adresse,
    string Ville,
    string CodePostal,
    string Pays,
    int DelaiLivraisonJours,
    decimal TauxRemise,
    StatutFournisseur Statut,
    int NombreCommandes
);

public record CreerFournisseurDto(
    string Code,
    string RaisonSociale,
    string Siret,
    string Email,
    string Telephone,
    string Adresse,
    string Ville,
    string CodePostal,
    string Pays,
    int DelaiLivraisonJours
);

// ─── COMMANDE D'ACHAT ─────────────────────────────────────────────────────────
public record CommandeAchatDto(
    Guid Id,
    string Numero,
    Guid FournisseurId,
    string FournisseurNom,
    DateTime DateCommande,
    DateTime DateLivraisonPrevue,
    DateTime? DateLivraisonReelle,
    StatutCommande Statut,
    string StatutLibelle,
    decimal MontantHT,
    decimal TVA,
    decimal MontantTTC,
    string? Commentaire,
    List<LigneCommandeAchatDto> Lignes
);

public record LigneCommandeAchatDto(
    Guid Id,
    Guid ArticleId,
    string ArticleCode,
    string Designation,
    int QuantiteCommandee,
    int QuantiteRecue,
    decimal PrixUnitaire,
    string Unite,
    decimal MontantLigne
);

public record CreerCommandeAchatDto(
    Guid FournisseurId,
    DateTime DateLivraisonPrevue,
    string? Commentaire,
    List<CreerLigneCommandeDto> Lignes
);

public record CreerLigneCommandeDto(
    Guid ArticleId,
    int Quantite,
    decimal PrixUnitaire
);

public record ReceptionCommandeDto(
    Guid CommandeId,
    List<ReceptionLigneDto> Lignes,
    Guid EmplacementReceptionId,
    string? NumeroLot,
    DateTime? DatePeremption
);

public record ReceptionLigneDto(
    Guid LigneId,
    int QuantiteRecue,
    string? NumeroSerie
);

// ─── EMPLACEMENT ──────────────────────────────────────────────────────────────
public record EmplacementDto(
    Guid Id,
    string Code,
    string Libelle,
    string Zone,
    string Allee,
    string Travee,
    string Niveau,
    string TypeLibelle,
    bool EstActif
);

// ─── LOT ─────────────────────────────────────────────────────────────────────
public record LotDto(
    Guid Id,
    string NumeroLot,
    Guid ArticleId,
    string ArticleCode,
    string ArticleDesignation,
    DateTime DateReception,
    DateTime? DatePeremption,
    int QuantiteInitiale,
    int QuantiteRestante,
    string StatutLibelle,
    bool EstPerime,
    bool EstEnAlertePeremption
);

// ─── INVENTAIRE ───────────────────────────────────────────────────────────────
public record InventaireDto(
    Guid Id,
    string Reference,
    string TypeLibelle,
    string StatutLibelle,
    DateTime DateDebut,
    DateTime? DateFin,
    string? Zone,
    int NombreLignes,
    int NombreEcarts,
    string CreatedBy
);

// ─── DASHBOARD ────────────────────────────────────────────────────────────────
public record DashboardDto(
    int TotalArticles,
    int ArticlesEnAlerte,
    int ArticlesEnRupture,
    int CommandesEnAttente,
    int LotsSurveiller,
    decimal ValeurTotaleStock,
    List<AlerteStockDto> DernieresAlertes,
    List<MouvementStockDto> DerniersMouvements,
    List<CommandeAchatDto> CommandesUrgentes
);

public record AlerteStockDto(
    Guid ArticleId,
    string ArticleCode,
    string ArticleDesignation,
    int QuantiteActuelle,
    int SeuilAlerte,
    int StockMinimum,
    string TypeAlerte   // "ALERTE" | "RUPTURE"
);

// ─── COMMUNS ─────────────────────────────────────────────────────────────────
public record PagedResultDto<T>(
    IEnumerable<T> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages
);

public record ResultDto(bool Succes, string? Message = null, object? Data = null)
{
    public static ResultDto Ok(string? message = null, object? data = null)
        => new(true, message, data);
    public static ResultDto Erreur(string message)
        => new(false, message);
}

// ─── PARAMÈTRES ENTREPRISE ────────────────────────────────────────────────────
public class ParametresDto
{
    public string RaisonSociale { get; set; } = string.Empty;
    public string Siret { get; set; } = string.Empty;
    public string NumTVA { get; set; } = string.Empty;
    public string Telephone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string SiteWeb { get; set; } = string.Empty;
    public string FormeJuridique { get; set; } = string.Empty;
    public string Adresse { get; set; } = string.Empty;
    public string CodePostal { get; set; } = string.Empty;
    public string Ville { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Pays { get; set; } = "France";
    public string EntrepotNom { get; set; } = "Entrepot central";
    public string EntrepotCode { get; set; } = "CENTRAL";
    public string EntrepotAdresse { get; set; } = string.Empty;
    public decimal EntrepotSurface { get; set; }
    public int EntrepotCapacite { get; set; }
    public string MethodeValorisation { get; set; } = "FEFO";
    public string GabaritInterface { get; set; } = "STANDARD";
    public string LogoEntreprise { get; set; } = string.Empty;
    public string FormatImpressionDocuments { get; set; } = "STANDARD";
    public string FormatImpressionRecus { get; set; } = "STANDARD";
    public string FormatPapierDocuments { get; set; } = "A4";
    public string ImprimanteDocumentsDefaut { get; set; } = string.Empty;
    public string FormatPapierRecus { get; set; } = "A5";
    public string ImprimanteRecusDefaut { get; set; } = string.Empty;
    public string Devise { get; set; } = "EUR";
    public string SymboleDevise { get; set; } = "EUR";
    public int NombreDecimalesMontant { get; set; } = 2;
    public int NombreDecimalesQuantite { get; set; } = 3;
    public decimal TauxTVA { get; set; } = 20m;
    public int DelaiAlerteDLUO { get; set; } = 30;
    public bool GestionLotDefaut { get; set; }
    public bool AutoriserStockNegatif { get; set; }
    public bool AlerteMailActif { get; set; }
    public bool InventaireAnnuelObligatoire { get; set; } = true;
    public string EmailAlerte { get; set; } = string.Empty;
    public string PrefixeCA { get; set; } = "CA";
    public string PrefixeArt { get; set; } = "ART";
    public string PrefixeLot { get; set; } = "LOT";
    public string PrefixeInv { get; set; } = "INV";
    public string Banque { get; set; } = string.Empty;
    public string Iban { get; set; } = string.Empty;
    public string Bic { get; set; } = string.Empty;
    public int DelaiPaiement { get; set; } = 30;
    public DateTime? UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}
