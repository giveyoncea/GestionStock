namespace GestionStock.Web.Models;

public class ClientDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string RaisonSociale { get; set; } = "";
    public int TypeClient { get; set; }
    public string? Email { get; set; }
    public string? Telephone { get; set; }
    public string? Ville { get; set; }
    public decimal TauxRemise { get; set; }
    public int DelaiPaiementJours { get; set; }
    public decimal PlafondCredit { get; set; }
    public bool EstActif { get; set; }
    public DateTime CreatedAt { get; set; }
    public decimal Encours { get; set; }
}

public class ClientRequest
{
    public string RaisonSociale { get; set; } = "";
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
    public decimal TauxRemise { get; set; } = 0;
    public decimal PlafondCredit { get; set; } = 0;
    public string? Notes { get; set; }
    public bool EstActif { get; set; } = true;
}

public class DocumentVenteDto
{
    public Guid Id { get; set; }
    public string Numero { get; set; } = "";
    public int TypeDocument { get; set; }
    public string TypeLibelle { get; set; } = "";
    public int Statut { get; set; }
    public string StatutLibelle { get; set; } = "";
    public string ClientNom { get; set; } = "";
    public Guid ClientId { get; set; }
    public DateTime DateDocument { get; set; }
    public DateTime? DateEcheance { get; set; }
    public decimal MontantHT { get; set; }
    public decimal MontantTVA { get; set; }
    public decimal MontantTTC { get; set; }
    public decimal MontantRegle { get; set; }
    public decimal Solde { get; set; }
    public decimal FraisLivraison { get; set; }
    public decimal MontantAcompte { get; set; }
    public bool EstVerrouille { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class LigneDocumentDetailDto
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Guid ArticleId { get; set; }
    public string? ArticleCode { get; set; }
    public string Designation { get; set; } = "";
    public decimal Quantite { get; set; }
    public decimal QuantiteLivree { get; set; }
    public decimal PrixUnitaireHT { get; set; }
    public decimal TauxRemise { get; set; }
    public decimal MontantRemise { get; set; }
    public decimal PrixNetHT { get; set; }
    public decimal TauxTVA { get; set; }
    public decimal MontantTVA { get; set; }
    public decimal MontantTTC { get; set; }
    public string? NumeroLot { get; set; }
    public int Ordre { get; set; }
}

public class DocumentVenteDetailDto
{
    public Guid Id { get; set; }
    public string Numero { get; set; } = "";
    public int TypeDocument { get; set; }
    public string TypeLibelle { get; set; } = "";
    public int Statut { get; set; }
    public string StatutLibelle { get; set; } = "";
    public Guid ClientId { get; set; }
    public string ClientNom { get; set; } = "";
    public DateTime DateDocument { get; set; }
    public DateTime? DateEcheance { get; set; }
    public string? AdresseLivraison { get; set; }
    public decimal MontantHT { get; set; }
    public decimal MontantRemise { get; set; }
    public decimal MontantTVA { get; set; }
    public decimal MontantTTC { get; set; }
    public decimal FraisLivraison { get; set; }
    public decimal MontantAcompte { get; set; }
    public decimal MontantRegle { get; set; }
    public bool EstVerrouille { get; set; }
    public string? NotesInternes { get; set; }
    public string? NotesExterne { get; set; }
    public Guid? DocumentParentId { get; set; }
    public List<LigneDocumentDetailDto> Lignes { get; set; } = new();
}

public class DocumentAchatDto
{
    public Guid Id { get; set; }
    public string Numero { get; set; } = "";
    public int TypeDocument { get; set; }
    public string TypeLibelle { get; set; } = "";
    public int Statut { get; set; }
    public string StatutLibelle { get; set; } = "";
    public string FournisseurNom { get; set; } = "";
    public DateTime DateDocument { get; set; }
    public DateTime? DateLivraisonPrevue { get; set; }
    public decimal MontantTTC { get; set; }
    public decimal MontantRegle { get; set; }
    public decimal Solde { get; set; }
    public bool EstVerrouille { get; set; }
}

public class DocumentAchatDetailDto
{
    public Guid Id { get; set; }
    public string Numero { get; set; } = "";
    public int TypeDocument { get; set; }
    public string TypeLibelle { get; set; } = "";
    public int Statut { get; set; }
    public string StatutLibelle { get; set; } = "";
    public Guid FournisseurId { get; set; }
    public string FournisseurNom { get; set; } = "";
    public DateTime DateDocument { get; set; }
    public DateTime? DateLivraisonPrevue { get; set; }
    public decimal MontantHT { get; set; }
    public decimal MontantTVA { get; set; }
    public decimal MontantTTC { get; set; }
    public decimal FraisLivraison { get; set; }
    public decimal MontantRegle { get; set; }
    public bool EstVerrouille { get; set; }
    public string? NotesInternes { get; set; }
    public Guid? DocumentParentId { get; set; }
    public List<LigneDocumentDetailDto> Lignes { get; set; } = new();
}

public class LigneDocRequest
{
    public Guid ArticleId { get; set; }
    public string Designation { get; set; } = "";
    public decimal Quantite { get; set; } = 1;
    public decimal PrixUnitaireHT { get; set; }
    public decimal TauxRemise { get; set; } = 0;
    public decimal TauxTVA { get; set; } = 20;
    public string? NumeroLot { get; set; }
}

public class DocumentVenteRequest
{
    public int TypeDocument { get; set; } = 1;
    public Guid ClientId { get; set; }
    public Guid? RepresentantId { get; set; }
    public Guid? DocumentParentId { get; set; }
    public DateTime? DateDocument { get; set; }
    public DateTime? DateEcheance { get; set; }
    public DateTime? DateLivraisonPrevue { get; set; }
    public string? AdresseLivraison { get; set; }
    public Guid? DepotId { get; set; }
    public decimal FraisLivraison { get; set; } = 0;
    public decimal MontantAcompte { get; set; } = 0;
    public decimal TauxTVA { get; set; } = 20;
    public string? ConditionsPaiement { get; set; }
    public string? NotesInternes { get; set; }
    public string? NotesExterne { get; set; }
    public List<LigneDocRequest> Lignes { get; set; } = new();
}

public class DocumentAchatRequest
{
    public int TypeDocument { get; set; } = 1;
    public Guid FournisseurId { get; set; }
    public Guid? DocumentParentId { get; set; }
    public DateTime? DateDocument { get; set; }
    public DateTime? DateLivraisonPrevue { get; set; }
    public Guid? DepotId { get; set; }
    public decimal FraisLivraison { get; set; } = 0;
    public string? NotesInternes { get; set; }
    public List<LigneDocRequest> Lignes { get; set; } = new();
}

public class ReglementRequest
{
    public decimal Montant { get; set; }
    public int ModeReglement { get; set; } = 1;
    public DateTime? DateReglement { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
}

public class AcompteRequest
{
    public Guid? ClientId { get; set; }
    public Guid? FournisseurId { get; set; }
    public decimal Montant { get; set; }
    public int ModeReglement { get; set; } = 1;
    public DateTime? DateAcompte { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
}

public class CommercialDashboardDto
{
    public int NbClients { get; set; }
    public int DevisOuverts { get; set; }
    public int CommandesEnCours { get; set; }
    public int FacturesNonReglees { get; set; }
    public decimal CaMois { get; set; }
    public decimal EncoursClient { get; set; }
    public int CommandesFourn { get; set; }
}

public class ComptabiliteDto
{
    public string CompteClientDefaut { get; set; } = "411000";
    public string CompteClientEtranger { get; set; } = "411100";
    public string CompteClientDouteux { get; set; } = "416000";
    public string CompteAcompteClient { get; set; } = "419000";
    public string CompteFournisseurDefaut { get; set; } = "401000";
    public string CompteFournisseurEtranger { get; set; } = "401100";
    public string CompteAcompteFournisseur { get; set; } = "409000";
    public string CompteTVACollectee { get; set; } = "445710";
    public string CompteTVADeductible { get; set; } = "445660";
    public string CompteTVASurEncaissements { get; set; } = "445720";
    public string CompteVenteMarchandises { get; set; } = "707000";
    public string CompteVentePrestations { get; set; } = "706000";
    public string CompteAchatMarchandises { get; set; } = "607000";
    public string CompteAchatMatieres { get; set; } = "601000";
    public string CompteFraisPort { get; set; } = "624100";
    public string CompteRemiseAccordee { get; set; } = "709000";
    public string CompteRemiseObtenue { get; set; } = "609000";
    public string JournalVentes { get; set; } = "VT";
    public string JournalAchats { get; set; } = "AC";
    public string JournalBanque { get; set; } = "BQ";
    public string JournalCaisse { get; set; } = "CA";
    public string JournalOD { get; set; } = "OD";
    public string JournalANouveaux { get; set; } = "AN";
    public string JournalVentesLibelle { get; set; } = "Journal des ventes";
    public string JournalAchatsLibelle { get; set; } = "Journal des achats";
    public string JournalBanqueLibelle { get; set; } = "Journal de banque";
    public string JournalCaisseLibelle { get; set; } = "Journal de caisse";
    public string JournalODLibelle { get; set; } = "Opérations diverses";
    public string JournalANouveauxLibelle { get; set; } = "À nouveaux";
    public int RegimeTVA { get; set; } = 1;
    public string FormatExportCompta { get; set; } = "SAGE";
}

public class ReglementDto
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public int TypeDocument { get; set; }
    public int ModeReglement { get; set; }
    public string ModeLibelle { get; set; } = "";
    public decimal Montant { get; set; }
    public DateTime DateReglement { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? NumeroDoc { get; set; }
    public string? TiersNom { get; set; }
}

public class AcompteDto
{
    public Guid Id { get; set; }
    public Guid? ClientId { get; set; }
    public Guid? FournisseurId { get; set; }
    public Guid? DocumentId { get; set; }
    public decimal Montant { get; set; }
    public decimal MontantUtilise { get; set; }
    public decimal MontantDisponible { get; set; }
    public DateTime DateAcompte { get; set; }
    public int ModeReglement { get; set; }
    public string ModeLibelle { get; set; } = "";
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public bool EstUtilise { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? ClientNom { get; set; }
    public string? FournisseurNom { get; set; }
    public string? NumeroDoc { get; set; }
}
