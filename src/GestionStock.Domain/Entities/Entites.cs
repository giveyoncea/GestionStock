using GestionStock.Domain.Enums;

namespace GestionStock.Domain.Entities;

// ─── EMPLACEMENT ─────────────────────────────────────────────────────────────
/// <summary>
/// Zone/emplacement physique dans l'entrepôt (allée, travée, niveau, case).
/// </summary>
public class Emplacement : BaseEntity
{
    public string Code { get; private set; } = string.Empty;
    public string Libelle { get; private set; } = string.Empty;
    public string Zone { get; private set; } = string.Empty;    // ex: ZONE-A, FRIGO, QUAI
    public string Allee { get; private set; } = string.Empty;
    public string Travee { get; private set; } = string.Empty;
    public string Niveau { get; private set; } = string.Empty;
    public TypeEmplacement Type { get; private set; }
    public decimal CapaciteMax { get; private set; }
    public bool EstActif { get; private set; } = true;

    private Emplacement() { }

    public static Emplacement Creer(string code, string libelle, string zone,
        TypeEmplacement type, string createdBy) => new()
    {
        Code = code.ToUpper(),
        Libelle = libelle,
        Zone = zone,
        Type = type,
        CreatedBy = createdBy
    };
}

// ─── FOURNISSEUR ─────────────────────────────────────────────────────────────
/// <summary>
/// Fournisseur référencé dans le système d'approvisionnement.
/// </summary>
public class Fournisseur : BaseEntity
{
    public string Code { get; private set; } = string.Empty;
    public string RaisonSociale { get; private set; } = string.Empty;
    public string Siret { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string Telephone { get; private set; } = string.Empty;
    public string Adresse { get; private set; } = string.Empty;
    public string Ville { get; private set; } = string.Empty;
    public string CodePostal { get; private set; } = string.Empty;
    public string Pays { get; private set; } = "France";
    public int DelaiLivraisonJours { get; private set; }
    public decimal TauxRemise { get; private set; }
    public StatutFournisseur Statut { get; private set; } = StatutFournisseur.Actif;
    public string? Commentaire { get; private set; }

    public IReadOnlyCollection<CommandeAchat> Commandes => _commandes.AsReadOnly();
    private readonly List<CommandeAchat> _commandes = new();

    private Fournisseur() { }

    public static Fournisseur Creer(string code, string raisonSociale,
        string email, string telephone, string createdBy) => new()
    {
        Code = code.ToUpper(),
        RaisonSociale = raisonSociale,
        Email = email,
        Telephone = telephone,
        CreatedBy = createdBy
    };

    public void Modifier(string raisonSociale, string email, string telephone,
        string adresse, string ville, string codePostal, int delaiLivraison, string modifiedBy)
    {
        RaisonSociale = raisonSociale;
        Email = email;
        Telephone = telephone;
        Adresse = adresse;
        Ville = ville;
        CodePostal = codePostal;
        DelaiLivraisonJours = delaiLivraison;
        SetUpdated(modifiedBy);
    }
}

// ─── LOT ─────────────────────────────────────────────────────────────────────
/// <summary>
/// Lot de fabrication / lot de réception pour traçabilité FIFO/FEFO.
/// </summary>
public class Lot : BaseEntity
{
    public string NumeroLot { get; private set; } = string.Empty;
    public Guid ArticleId { get; private set; }
    public Article Article { get; private set; } = null!;

    public DateTime DateFabrication { get; private set; }
    public DateTime? DatePeremption { get; private set; }    // DLC / DLUO
    public DateTime DateReception { get; private set; }

    public int QuantiteInitiale { get; private set; }
    public int QuantiteRestante { get; private set; }
    public StatutLot Statut { get; private set; } = StatutLot.Disponible;

    public Guid? CommandeAchatId { get; private set; }
    public string? NumeroSerie { get; private set; }
    public string? CertifiqueQualite { get; private set; }

    private Lot() { }

    public static Lot Creer(string numeroLot, Guid articleId, DateTime dateReception,
        int quantite, string createdBy) => new()
    {
        NumeroLot = numeroLot,
        ArticleId = articleId,
        DateReception = dateReception,
        DateFabrication = dateReception,
        QuantiteInitiale = quantite,
        QuantiteRestante = quantite,
        CreatedBy = createdBy
    };

    public bool EstPerime() =>
        DatePeremption.HasValue && DatePeremption.Value < DateTime.UtcNow;

    public bool EstEnAlertPeremption(int joursAvance = 30) =>
        DatePeremption.HasValue &&
        DatePeremption.Value < DateTime.UtcNow.AddDays(joursAvance);

    public void ConsommerQuantite(int quantite)
    {
        if (quantite > QuantiteRestante)
            throw new InvalidOperationException("Quantité de lot insuffisante.");
        QuantiteRestante -= quantite;
        if (QuantiteRestante == 0) Statut = StatutLot.Epuise;
    }
}

// ─── MOUVEMENT DE STOCK ───────────────────────────────────────────────────────
/// <summary>
/// Historique complet des mouvements de stock (entrée, sortie, transfert, ajustement).
/// Conforme NF-TRACE-01 : horodatage et traçabilité complète.
/// </summary>
public class MouvementStock : BaseEntity
{
    public Guid ArticleId { get; private set; }
    public Article Article { get; private set; } = null!;

    public Guid EmplacementSourceId { get; private set; }
    public Emplacement EmplacementSource { get; private set; } = null!;

    public Guid? EmplacementDestinationId { get; private set; }
    public Emplacement? EmplacementDestination { get; private set; }

    public TypeMouvement TypeMouvement { get; private set; }
    public int Quantite { get; private set; }
    public decimal ValeurUnitaire { get; private set; }
    public decimal ValeurTotale => Quantite * ValeurUnitaire;

    public Guid? LotId { get; private set; }
    public Lot? Lot { get; private set; }

    public string? Reference { get; private set; }     // N° BL, N° commande, etc.
    public string? Motif { get; private set; }
    public DateTime DateMouvement { get; private set; }

    private MouvementStock() { }

    public static MouvementStock Creer(Guid articleId, Guid emplacementSourceId,
        TypeMouvement type, int quantite, decimal valeurUnitaire,
        string reference, string createdBy) => new()
    {
        ArticleId = articleId,
        EmplacementSourceId = emplacementSourceId,
        TypeMouvement = type,
        Quantite = quantite,
        ValeurUnitaire = valeurUnitaire,
        Reference = reference,
        DateMouvement = DateTime.UtcNow,
        CreatedBy = createdBy
    };
}

// ─── COMMANDE D'ACHAT ─────────────────────────────────────────────────────────
/// <summary>
/// Commande d'approvisionnement fournisseur (Purchase Order).
/// </summary>
public class CommandeAchat : BaseEntity
{
    public string Numero { get; private set; } = string.Empty;
    public Guid FournisseurId { get; private set; }
    public Fournisseur Fournisseur { get; private set; } = null!;

    public DateTime DateCommande { get; private set; }
    public DateTime DateLivraisonPrevue { get; private set; }
    public DateTime? DateLivraisonReelle { get; private set; }

    public StatutCommande Statut { get; private set; } = StatutCommande.Brouillon;
    public string? Commentaire { get; private set; }
    public string? NumeroFacture { get; private set; }

    public decimal MontantHT => _lignes.Sum(l => l.MontantLigne);
    public decimal TVA => MontantHT * 0.20m;
    public decimal MontantTTC => MontantHT + TVA;

    public IReadOnlyCollection<LigneCommandeAchat> Lignes => _lignes.AsReadOnly();
    private readonly List<LigneCommandeAchat> _lignes = new();

    private CommandeAchat() { }

    public static CommandeAchat Creer(string numero, Guid fournisseurId,
        DateTime dateLivraisonPrevue, string createdBy) => new()
    {
        Numero = numero,
        FournisseurId = fournisseurId,
        DateCommande = DateTime.UtcNow,
        DateLivraisonPrevue = dateLivraisonPrevue,
        CreatedBy = createdBy
    };

    public void AjouterLigne(Guid articleId, string designation, int quantite,
        decimal prixUnitaire, string unite)
    {
        if (Statut != StatutCommande.Brouillon)
            throw new InvalidOperationException("Impossible de modifier une commande validée.");

        var ligne = new LigneCommandeAchat(articleId, designation, quantite, prixUnitaire, unite);
        _lignes.Add(ligne);
    }

    public void Valider(string modifiedBy)
    {
        if (!_lignes.Any())
            throw new InvalidOperationException("Impossible de valider une commande sans lignes.");
        Statut = StatutCommande.Confirmee;
        SetUpdated(modifiedBy);
    }

    public void EnregistrerReception(DateTime dateReception, string modifiedBy)
    {
        Statut = StatutCommande.Recue;
        DateLivraisonReelle = dateReception;
        SetUpdated(modifiedBy);
    }

    public void Annuler(string motif, string modifiedBy)
    {
        if (Statut == StatutCommande.Recue)
            throw new InvalidOperationException("Impossible d'annuler une commande déjà reçue.");
        Statut = StatutCommande.Annulee;
        Commentaire = motif;
        SetUpdated(modifiedBy);
    }
}

// ─── LIGNE DE COMMANDE D'ACHAT ────────────────────────────────────────────────
public class LigneCommandeAchat
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ArticleId { get; private set; }
    public Article Article { get; private set; } = null!;
    public string Designation { get; private set; } = string.Empty;
    public int QuantiteCommandee { get; private set; }
    public int QuantiteRecue { get; private set; }
    public decimal PrixUnitaire { get; private set; }
    public string Unite { get; private set; } = string.Empty;
    public decimal MontantLigne => QuantiteCommandee * PrixUnitaire;

    private LigneCommandeAchat() { }

    internal LigneCommandeAchat(Guid articleId, string designation,
        int quantite, decimal prixUnitaire, string unite)
    {
        ArticleId = articleId;
        Designation = designation;
        QuantiteCommandee = quantite;
        PrixUnitaire = prixUnitaire;
        Unite = unite;
    }

    public void EnregistrerReception(int quantiteRecue)
    {
        QuantiteRecue = Math.Min(quantiteRecue, QuantiteCommandee);
    }
}

// ─── AUDIT TRAIL ─────────────────────────────────────────────────────────────
/// <summary>
/// Journal d'audit conforme NF-SEC-03 : traçabilité de toutes les actions critiques.
/// </summary>
public class AuditTrail
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string UserId { get; private set; } = string.Empty;
    public string UserEmail { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public string Entite { get; private set; } = string.Empty;     // Article, Commande, Lot...
    public string EntiteId { get; private set; } = string.Empty;
    public DateTime Timestamp { get; private set; } = DateTime.UtcNow;
    public string? Details { get; private set; }                   // JSON avant/après
    public string? AdresseIp { get; private set; }

    private AuditTrail() { }

    public static AuditTrail Creer(string userId, string userEmail, string action,
        string entite, string entiteId, string? details = null, string? ip = null) => new()
    {
        UserId = userId,
        UserEmail = userEmail,
        Action = action,
        Entite = entite,
        EntiteId = entiteId,
        Details = details,
        AdresseIp = ip
    };
}

// ─── INVENTAIRE ───────────────────────────────────────────────────────────────
/// <summary>
/// Session d'inventaire physique (partiel ou total).
/// </summary>
public class Inventaire : BaseEntity
{
    public string Reference { get; private set; } = string.Empty;
    public TypeInventaire Type { get; private set; }
    public StatutInventaire Statut { get; private set; } = StatutInventaire.EnCours;
    public DateTime DateDebut { get; private set; }
    public DateTime? DateFin { get; private set; }
    public string? Zone { get; private set; }   // Pour inventaire tournant

    public IReadOnlyCollection<LigneInventaire> Lignes => _lignes.AsReadOnly();
    private readonly List<LigneInventaire> _lignes = new();

    private Inventaire() { }

    public static Inventaire Creer(string reference, TypeInventaire type,
        string? zone, string createdBy) => new()
    {
        Reference = reference,
        Type = type,
        Zone = zone,
        DateDebut = DateTime.UtcNow,
        CreatedBy = createdBy
    };

    public void AjouterLigne(Guid articleId, Guid emplacementId,
        int quantiteTheorique, int quantiteComptee)
    {
        _lignes.Add(new LigneInventaire(articleId, emplacementId,
            quantiteTheorique, quantiteComptee));
    }

    public void Valider(string modifiedBy)
    {
        Statut = StatutInventaire.Valide;
        DateFin = DateTime.UtcNow;
        SetUpdated(modifiedBy);
    }
}

public class LigneInventaire
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ArticleId { get; private set; }
    public Guid EmplacementId { get; private set; }
    public int QuantiteTheorique { get; private set; }
    public int QuantiteComptee { get; private set; }
    public int Ecart => QuantiteComptee - QuantiteTheorique;
    public bool EstJuste => Ecart == 0;

    private LigneInventaire() { }
    internal LigneInventaire(Guid articleId, Guid emplacementId,
        int theo, int compte)
    {
        ArticleId = articleId;
        EmplacementId = emplacementId;
        QuantiteTheorique = theo;
        QuantiteComptee = compte;
    }
}

// ─── PARAMÈTRES ENTREPRISE ────────────────────────────────────────────────────
/// <summary>
/// Table singleton – un seul enregistrement contient tous les paramètres.
/// </summary>
public class ParametresEntreprise
{
    public int Id { get; set; } = 1; // Toujours 1

    // Entreprise
    public string RaisonSociale { get; set; } = string.Empty;
    public string Siret { get; set; } = string.Empty;
    public string NumTVA { get; set; } = string.Empty;
    public string Telephone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string SiteWeb { get; set; } = string.Empty;
    public string FormeJuridique { get; set; } = string.Empty;

    // Adresse
    public string Adresse { get; set; } = string.Empty;
    public string CodePostal { get; set; } = string.Empty;
    public string Ville { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Pays { get; set; } = "France";

    // Entrepôt
    public string EntrepotNom { get; set; } = "Entrepôt central";
    public string EntrepotCode { get; set; } = "CENTRAL";
    public string EntrepotAdresse { get; set; } = string.Empty;
    public decimal EntrepotSurface { get; set; }
    public int EntrepotCapacite { get; set; }

    // Stock
    public string MethodeValorisation { get; set; } = "FEFO";
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

    // Numérotation
    public string PrefixeCA { get; set; } = "CA";
    public string PrefixeArt { get; set; } = "ART";
    public string PrefixeLot { get; set; } = "LOT";
    public string PrefixeInv { get; set; } = "INV";

    // Banque
    public string Banque { get; set; } = string.Empty;
    public string Iban { get; set; } = string.Empty;
    public string Bic { get; set; } = string.Empty;
    public int DelaiPaiement { get; set; } = 30;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string UpdatedBy { get; set; } = string.Empty;
}

// ─── DÉPÔT ────────────────────────────────────────────────────────────────────
public class Depot : BaseEntity
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
    public bool EstActif { get; set; } = true;
    public int TypeDepot { get; set; }

    public static Depot Creer(string code, string libelle, string adresse,
        string ville, string codePostal, string createdBy, bool estPrincipal = false)
        => new() { Code = code, Libelle = libelle, Adresse = adresse,
                   Ville = ville, CodePostal = codePostal, EstPrincipal = estPrincipal,
                   EstActif = true, CreatedAt = DateTime.UtcNow, CreatedBy = createdBy };

    public void Modifier(string libelle, string? description, string adresse,
        string codePostal, string ville, string pays, string? responsable,
        string? telephone, decimal surfaceM2, int capacitePalettes, int typeDepot, string updatedBy)
    {
        Libelle = libelle; Description = description; Adresse = adresse;
        CodePostal = codePostal; Ville = ville; Pays = pays;
        Responsable = responsable; Telephone = telephone;
        SurfaceM2 = surfaceM2; CapacitePalettes = capacitePalettes;
        TypeDepot = typeDepot; UpdatedAt = DateTime.UtcNow; UpdatedBy = updatedBy;
    }

    public void DefinirPrincipal(string updatedBy)
    { EstPrincipal = true; UpdatedAt = DateTime.UtcNow; UpdatedBy = updatedBy; }

    public void RetirerPrincipal(string updatedBy)
    { EstPrincipal = false; UpdatedAt = DateTime.UtcNow; UpdatedBy = updatedBy; }

    public void Desactiver(string updatedBy)
    { EstActif = false; EstPrincipal = false; UpdatedAt = DateTime.UtcNow; UpdatedBy = updatedBy; }
}

// ─── FAMILLE ARTICLE ──────────────────────────────────────────────────────────
public class FamilleArticle : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Libelle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? ParentId { get; set; }
    public string? Couleur { get; set; }
    public int Ordre { get; set; }
    public bool EstActif { get; set; } = true;
    public FamilleArticle? Parent { get; set; }
    public ICollection<FamilleArticle> SousFamilles { get; set; } = new List<FamilleArticle>();

    public static FamilleArticle Creer(string code, string libelle,
        string? description, Guid? parentId, string? couleur, int ordre, string createdBy)
        => new() { Code = code, Libelle = libelle, Description = description,
                   ParentId = parentId, Couleur = couleur, Ordre = ordre,
                   EstActif = true, CreatedAt = DateTime.UtcNow, CreatedBy = createdBy };

    public void Modifier(string libelle, string? description, Guid? parentId,
        string? couleur, int ordre, string updatedBy)
    {
        Libelle = libelle; Description = description; ParentId = parentId;
        Couleur = couleur; Ordre = ordre; UpdatedAt = DateTime.UtcNow; UpdatedBy = updatedBy;
    }

    public void Desactiver(string updatedBy)
    { EstActif = false; UpdatedAt = DateTime.UtcNow; UpdatedBy = updatedBy; }
}
