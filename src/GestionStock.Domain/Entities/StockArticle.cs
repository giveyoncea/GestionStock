namespace GestionStock.Domain.Entities;

/// <summary>
/// Stock d'un article dans un emplacement précis de l'entrepôt.
/// Modèle de stock multi-emplacements conforme au CdC (WMS).
/// </summary>
public class StockArticle : BaseEntity
{
    public Guid ArticleId { get; private set; }
    public Article Article { get; private set; } = null!;

    public Guid EmplacementId { get; private set; }
    public Emplacement Emplacement { get; private set; } = null!;

    public int QuantiteDisponible { get; private set; }
    public int QuantiteReservee { get; private set; }
    public int QuantiteEnCommande { get; private set; }

    public int QuantiteStock => QuantiteDisponible + QuantiteReservee;

    // Traçabilité lot
    public Guid? LotId { get; private set; }
    public Lot? Lot { get; private set; }

    private StockArticle() { }

    public static StockArticle Creer(Guid articleId, Guid emplacementId, string createdBy)
    {
        return new StockArticle
        {
            ArticleId = articleId,
            EmplacementId = emplacementId,
            QuantiteDisponible = 0,
            QuantiteReservee = 0,
            QuantiteEnCommande = 0,
            CreatedBy = createdBy
        };
    }

    public void AppliquerEntree(int quantite, string modifiedBy)
    {
        if (quantite <= 0)
            throw new ArgumentException("La quantité d'entrée doit être positive.", nameof(quantite));
        QuantiteDisponible += quantite;
        SetUpdated(modifiedBy);
    }

    public void AppliquerSortie(int quantite, string modifiedBy)
    {
        if (quantite <= 0)
            throw new ArgumentException("La quantité de sortie doit être positive.", nameof(quantite));
        if (quantite > QuantiteDisponible)
            throw new InvalidOperationException(
                $"Stock insuffisant. Disponible : {QuantiteDisponible}, Demandé : {quantite}");
        QuantiteDisponible -= quantite;
        SetUpdated(modifiedBy);
    }

    public void Reserver(int quantite, string modifiedBy)
    {
        if (quantite > QuantiteDisponible)
            throw new InvalidOperationException("Impossible de réserver plus que le stock disponible.");
        QuantiteDisponible -= quantite;
        QuantiteReservee += quantite;
        SetUpdated(modifiedBy);
    }

    public void LiberReservation(int quantite, string modifiedBy)
    {
        QuantiteReservee -= quantite;
        QuantiteDisponible += quantite;
        SetUpdated(modifiedBy);
    }

    public void AjouterEnCommande(int quantite, string modifiedBy)
    {
        QuantiteEnCommande += quantite;
        SetUpdated(modifiedBy);
    }

    public void ConfirmerReception(int quantiteRecue, int quantiteCommandee, string modifiedBy)
    {
        QuantiteDisponible += quantiteRecue;
        QuantiteEnCommande = Math.Max(0, QuantiteEnCommande - quantiteCommandee);
        SetUpdated(modifiedBy);
    }
}
