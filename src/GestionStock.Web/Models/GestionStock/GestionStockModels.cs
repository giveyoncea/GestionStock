namespace GestionStock.Web.Models;

public record DashboardDto(
    int TotalArticles, int ArticlesEnAlerte, int ArticlesEnRupture,
    int CommandesEnAttente, int LotsSurveiller, decimal ValeurTotaleStock,
    List<AlerteStockDto> DernieresAlertes,
    List<MouvementStockDto> DerniersMouvements,
    List<CommandeAchatDto> CommandesUrgentes);

public record AlerteStockDto(
    Guid ArticleId, string ArticleCode, string ArticleDesignation,
    int QuantiteActuelle, int SeuilAlerte, int StockMinimum, string TypeAlerte);

public record ArticleDto(
    Guid Id, string Code, string CodeBarres, string Designation,
    string Description, string Categorie, string FamilleArticle, string Unite,
    decimal PrixAchat, decimal PrixVente,
    int SeuilAlerte, int StockMinimum, int StockMaximum,
    bool GestionLot, bool GestionDLUO, int Statut,
    int QuantiteTotale, bool EstEnAlerte, bool EstEnRupture,
    string? FournisseurNom, DateTime CreatedAt);

public record ModifierArticleDto(
    Guid Id, string Designation, string Description,
    string Categorie, string FamilleArticle, string Unite,
    decimal PrixAchat, decimal PrixVente,
    int SeuilAlerte, int StockMinimum, int StockMaximum,
    bool GestionLot, bool GestionDLUO, Guid? FournisseurPrincipalId);

public record CreerArticleDto(
    string Code, string CodeBarres, string Designation, string Description,
    string Categorie, string FamilleArticle, string Unite,
    decimal PrixAchat, decimal PrixVente,
    int SeuilAlerte, int StockMinimum, int StockMaximum,
    bool GestionLot, bool GestionDLUO, Guid? FournisseurPrincipalId);

public record StockResumeDto(
    Guid ArticleId, string ArticleCode, string ArticleDesignation,
    int QuantiteTotale, int QuantiteDisponible, int QuantiteReservee,
    bool EstEnAlerte, bool EstEnRupture, decimal ValeurStock);

public record MouvementStockDto(
    Guid Id, string ArticleCode, string ArticleDesignation,
    string EmplacementSource, string? EmplacementDestination,
    int TypeMouvement, string TypeMouvementLibelle,
    int Quantite, decimal ValeurUnitaire, decimal ValeurTotale,
    string? Reference, string? Motif, string? NumeroLot,
    DateTime DateMouvement, string CreatedBy);

public record EntreeStockDto(
    Guid ArticleId, Guid EmplacementId, int Quantite,
    decimal PrixUnitaire, string Reference,
    string? NumeroLot, DateTime? DatePeremption, string? Motif);

public record SortieStockDto(
    Guid ArticleId, Guid EmplacementId, int Quantite,
    string Reference, string? NumeroLot, string? Motif);

public record FournisseurDto(
    Guid Id, string Code, string RaisonSociale, string Email,
    string Telephone, string Adresse, string Ville, string CodePostal,
    string Pays, int DelaiLivraisonJours, decimal TauxRemise,
    int Statut, int NombreCommandes);

public record CreerFournisseurDto(
    string Code, string RaisonSociale, string Siret,
    string Email, string Telephone, string Adresse,
    string Ville, string CodePostal, string Pays, int DelaiLivraisonJours);

public record CommandeAchatDto(
    Guid Id, string Numero, Guid FournisseurId, string FournisseurNom,
    DateTime DateCommande, DateTime DateLivraisonPrevue,
    DateTime? DateLivraisonReelle, int Statut, string StatutLibelle,
    decimal MontantHT, decimal TVA, decimal MontantTTC,
    string? Commentaire, List<LigneCommandeDto> Lignes);

public record LigneCommandeDto(
    Guid Id, Guid ArticleId, string ArticleCode, string Designation,
    int QuantiteCommandee, int QuantiteRecue,
    decimal PrixUnitaire, string Unite, decimal MontantLigne);

public record CreerCommandeDto(
    Guid FournisseurId, DateTime DateLivraisonPrevue,
    string? Commentaire, List<CreerLigneDto> Lignes);

public record CreerLigneDto(Guid ArticleId, int Quantite, decimal PrixUnitaire);

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
    public string EntrepotNom { get; set; } = "Entrepôt central";
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

public class DepotDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Libelle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Adresse { get; set; } = string.Empty;
    public string CodePostal { get; set; } = string.Empty;
    public string Ville { get; set; } = string.Empty;
    public string Pays { get; set; } = "France";
    public string? Responsable { get; set; }
    public string? Telephone { get; set; }
    public decimal SurfaceM2 { get; set; }
    public int CapacitePalettes { get; set; }
    public bool EstPrincipal { get; set; }
    public bool EstActif { get; set; }
    public int TypeDepot { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class FamilleArticleDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Libelle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? ParentId { get; set; }
    public string? ParentLibelle { get; set; }
    public string? Couleur { get; set; }
    public int Ordre { get; set; }
    public bool EstActif { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DepotRequest
{
    public string Code { get; set; } = string.Empty;
    public string Libelle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Adresse { get; set; } = string.Empty;
    public string CodePostal { get; set; } = string.Empty;
    public string Ville { get; set; } = string.Empty;
    public string Pays { get; set; } = "France";
    public string? Responsable { get; set; }
    public string? Telephone { get; set; }
    public decimal SurfaceM2 { get; set; }
    public int CapacitePalettes { get; set; }
    public bool EstPrincipal { get; set; }
    public int TypeDepot { get; set; }
}

public class FamilleRequest
{
    public string Code { get; set; } = string.Empty;
    public string Libelle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? ParentId { get; set; }
    public string? Couleur { get; set; }
    public int Ordre { get; set; }
}

public class EmplacementDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Zone { get; set; } = string.Empty;
}

public class TransfertRequest
{
    public Guid ArticleId { get; set; }
    public Guid EmplacementSourceId { get; set; }
    public Guid EmplacementDestinationId { get; set; }
    public int Quantite { get; set; }
    public string? NumeroLot { get; set; }
}

public class AjustementRequest
{
    public Guid ArticleId { get; set; }
    public Guid EmplacementId { get; set; }
    public int QuantiteReelle { get; set; }
    public string? Motif { get; set; }
}

public class LigneInventaireSaisie
{
    public Guid ArticleId { get; set; }
    public string ArticleCode { get; set; } = string.Empty;
    public string ArticleDesignation { get; set; } = string.Empty;
    public Guid EmplacementId { get; set; }
    public string EmplacementCode { get; set; } = string.Empty;
    public int QuantiteTheorique { get; set; }
    public int QuantiteComptee { get; set; }
    public int Ecart => QuantiteComptee - QuantiteTheorique;
    public bool Modifiee { get; set; }
}

public class CategorieDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Libelle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Couleur { get; set; }
    public int Ordre { get; set; }
    public bool EstActif { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CategorieRequest
{
    public string Code { get; set; } = string.Empty;
    public string Libelle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Couleur { get; set; }
    public int Ordre { get; set; }
}

public class LotTracabiliteDto
{
    public Guid Id { get; set; }
    public string NumeroLot { get; set; } = string.Empty;
    public Guid ArticleId { get; set; }
    public string ArticleCode { get; set; } = string.Empty;
    public string ArticleDesignation { get; set; } = string.Empty;
    public DateTime DateReception { get; set; }
    public DateTime DateFabrication { get; set; }
    public DateTime? DatePeremption { get; set; }
    public int QuantiteInitiale { get; set; }
    public int QuantiteRestante { get; set; }
    public int Statut { get; set; }
    public string StatutLibelle { get; set; } = string.Empty;
    public string? NumeroSerie { get; set; }
    public string? CertifiqueQualite { get; set; }
    public bool EstPerime { get; set; }
    public bool EnAlertePeremption { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AlertePeremptionDto
{
    public Guid Id { get; set; }
    public string NumeroLot { get; set; } = string.Empty;
    public DateTime DatePeremption { get; set; }
    public int QuantiteRestante { get; set; }
    public int Statut { get; set; }
    public string ArticleCode { get; set; } = string.Empty;
    public string ArticleDesignation { get; set; } = string.Empty;
    public int JoursRestants { get; set; }
    public bool Critique { get; set; }
}

public class LotRequest
{
    public Guid ArticleId { get; set; }
    public string NumeroLot { get; set; } = string.Empty;
    public DateTime DateReception { get; set; } = DateTime.Today;
    public DateTime? DateFabrication { get; set; }
    public DateTime? DatePeremption { get; set; }
    public int Quantite { get; set; } = 1;
    public string? NumeroSerie { get; set; }
    public string? CertifiqueQualite { get; set; }
}

public class MouvementTracabiliteDto
{
    public Guid Id { get; set; }
    public DateTime DateMouvement { get; set; }
    public int TypeMouvement { get; set; }
    public string TypeLibelle { get; set; } = string.Empty;
    public int Quantite { get; set; }
    public decimal ValeurTotale { get; set; }
    public string? Reference { get; set; }
    public string? Motif { get; set; }
    public string? EmplacementSource { get; set; }
    public string? EmplacementDestination { get; set; }
    public string? NumeroLot { get; set; }
    public string? CreatedBy { get; set; }
}
