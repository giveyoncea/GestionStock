using GestionStock.Domain.Enums;

namespace GestionStock.Domain.Entities;

/// <summary>
/// Représente un article géré dans le système WMS/SCM.
/// Conforme aux exigences fonctionnelles du CdC (section Gestion des Articles).
/// </summary>
public class Article : BaseEntity
{
    public string Code { get; private set; } = string.Empty;
    public string CodeBarres { get; private set; } = string.Empty;
    public string Designation { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string Categorie { get; private set; } = string.Empty;
    public string FamilleArticle { get; private set; } = string.Empty;
    public string Unite { get; private set; } = "PCS"; // Unité de mesure

    // Prix & valorisation
    public decimal PrixAchat { get; private set; }
    public decimal PrixVente { get; private set; }
    public decimal ValeurStockMoyen { get; private set; }

    // Seuils de stock (NF-GS-03 : alertes automatiques)
    public int SeuilAlerte { get; private set; }
    public int StockMinimum { get; private set; }
    public int StockMaximum { get; private set; }
    public bool SansSuiviStock { get; private set; }

    // Informations de traçabilité
    public bool GestionLot { get; private set; }
    public bool GestionNumeroDeSerie { get; private set; }
    public bool GestionDLUO { get; private set; } // Date Limite d'Utilisation Optimale
    public bool GestionDLC { get; private set; }  // Date Limite de Consommation

    // Dimensions & logistique
    public decimal? Poids { get; private set; }
    public decimal? Volume { get; private set; }
    public string? ImageUrl { get; private set; }

    public StatutArticle Statut { get; private set; } = StatutArticle.Actif;

    // Fournisseur principal
    public Guid? FournisseurPrincipalId { get; private set; }
    public Fournisseur? FournisseurPrincipal { get; private set; }
    public string? ReferencesFournisseur { get; private set; }

    // Collections de navigation
    public IReadOnlyCollection<StockArticle> StocksParEmplacement => _stocks.AsReadOnly();
    private readonly List<StockArticle> _stocks = new();

    public IReadOnlyCollection<Lot> Lots => _lots.AsReadOnly();
    private readonly List<Lot> _lots = new();

    // Constructeur privé pour EF Core
    private Article() { }

    public static Article Creer(
        string code,
        string codeBarres,
        string designation,
        string categorie,
        string unite,
        decimal prixAchat,
        int seuilAlerte,
        int stockMinimum,
        int stockMaximum,
        string createdBy)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Le code article est obligatoire.", nameof(code));
        if (string.IsNullOrWhiteSpace(designation))
            throw new ArgumentException("La désignation est obligatoire.", nameof(designation));
        if (seuilAlerte < 0 || stockMinimum < 0)
            throw new ArgumentException("Les seuils de stock ne peuvent pas être négatifs.");

        var article = new Article
        {
            Code = code.ToUpper().Trim(),
            CodeBarres = codeBarres.Trim(),
            Designation = designation.Trim(),
            Categorie = categorie.Trim(),
            Unite = unite.Trim(),
            PrixAchat = prixAchat,
            SeuilAlerte = seuilAlerte,
            StockMinimum = stockMinimum,
            StockMaximum = stockMaximum,
            CreatedBy = createdBy
        };
        return article;
    }

    public void Modifier(
        string designation,
        string description,
        string categorie,
        string familleArticle,
        string unite,
        decimal prixAchat,
        decimal prixVente,
        int seuilAlerte,
        int stockMinimum,
        int stockMaximum,
        bool sansSuiviStock,
        bool gestionLot,
        bool gestionNumeroDeSerie,
        bool gestionDLUO,
        string modifiedBy)
    {
        Designation = designation;
        Description = description;
        Categorie = categorie;
        FamilleArticle = familleArticle;
        Unite = unite;
        PrixAchat = prixAchat;
        PrixVente = prixVente;
        SansSuiviStock = sansSuiviStock;
        SeuilAlerte = sansSuiviStock ? 0 : seuilAlerte;
        StockMinimum = sansSuiviStock ? 0 : stockMinimum;
        StockMaximum = sansSuiviStock ? 0 : stockMaximum;
        GestionLot = sansSuiviStock ? false : gestionLot;
        GestionNumeroDeSerie = sansSuiviStock ? false : gestionNumeroDeSerie;
        GestionDLUO = sansSuiviStock ? false : gestionDLUO;
        SetUpdated(modifiedBy);
    }

    public void Desactiver(string modifiedBy)
    {
        Statut = StatutArticle.Inactif;
        SetUpdated(modifiedBy);
    }

    public bool EstEnAlerteStock(int quantiteTotale)
        => !SansSuiviStock && quantiteTotale <= SeuilAlerte;

    public bool EstEnRuptureStock(int quantiteTotale)
        => !SansSuiviStock && quantiteTotale <= StockMinimum;
}
