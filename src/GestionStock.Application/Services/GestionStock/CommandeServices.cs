using GestionStock.Application.DTOs;
using GestionStock.Application.Interfaces;
using GestionStock.Domain.Entities;
using GestionStock.Domain.Enums;
using GestionStock.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace GestionStock.Application.Services;

// ─── SERVICE COMMANDES D'ACHAT ────────────────────────────────────────────────
public class CommandeAchatService : ICommandeAchatService
{
    private readonly IUnitOfWork _uow;
    private readonly ILogger<CommandeAchatService> _logger;

    public CommandeAchatService(IUnitOfWork uow, ILogger<CommandeAchatService> logger)
    {
        _uow = uow;
        _logger = logger;
    }

    public async Task<PagedResultDto<CommandeAchatDto>> GetPagedAsync(
        int page, int pageSize, CancellationToken ct)
    {
        var commandes = await _uow.CommandesAchat.GetAllAsync(ct);
        var total = commandes.Count();
        var paged = commandes
            .OrderByDescending(c => c.DateCommande)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(MapToDto);

        return new PagedResultDto<CommandeAchatDto>(paged, total, page, pageSize,
            (int)Math.Ceiling(total / (double)pageSize));
    }

    public async Task<CommandeAchatDto?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var commande = await _uow.CommandesAchat.GetAvecLignesAsync(id, ct);
        return commande is null ? null : MapToDto(commande);
    }

    public async Task<ResultDto> CreerCommandeAsync(CreerCommandeAchatDto dto,
        string userId, CancellationToken ct)
    {
        try
        {
            var fournisseur = await _uow.Fournisseurs.GetByIdAsync(dto.FournisseurId, ct)
                ?? throw new InvalidOperationException("Fournisseur introuvable.");

            var numero = await _uow.CommandesAchat.GenererNumeroCommandeAsync(ct);
            var commande = CommandeAchat.Creer(numero, dto.FournisseurId,
                dto.DateLivraisonPrevue, userId);

            foreach (var ligne in dto.Lignes)
            {
                var article = await _uow.Articles.GetByIdAsync(ligne.ArticleId, ct)
                    ?? throw new InvalidOperationException($"Article {ligne.ArticleId} introuvable.");
                commande.AjouterLigne(article.Id, article.Designation,
                    ligne.Quantite, ligne.PrixUnitaire, article.Unite);
            }

            await _uow.CommandesAchat.AddAsync(commande, ct);

            await _uow.AuditTrails.AddAsync(AuditTrail.Creer(
                userId, userId, "Création commande achat",
                "CommandeAchat", commande.Id.ToString(),
                $"N°: {numero}, Fournisseur: {fournisseur.RaisonSociale}"), ct);

            await _uow.SaveChangesAsync(ct);
            _logger.LogInformation("Commande {Numero} créée par {UserId}", numero, userId);
            return ResultDto.Ok($"Commande {numero} créée.", commande.Id);
        }
        catch (Exception ex)
        {
            return ResultDto.Erreur(ex.Message);
        }
    }

    public async Task<ResultDto> ValiderCommandeAsync(Guid id, string userId, CancellationToken ct)
    {
        var commande = await _uow.CommandesAchat.GetAvecLignesAsync(id, ct);
        if (commande is null) return ResultDto.Erreur("Commande introuvable.");

        commande.Valider(userId);

        // Mettre à jour les quantités en commande pour chaque article
        foreach (var ligne in commande.Lignes)
        {
            var stocks = await _uow.Stocks.GetByArticleAsync(ligne.ArticleId, ct);
            foreach (var stock in stocks)
            {
                stock.AjouterEnCommande(ligne.QuantiteCommandee, userId);
                _uow.Stocks.Update(stock);
            }
        }

        _uow.CommandesAchat.Update(commande);
        await _uow.SaveChangesAsync(ct);
        return ResultDto.Ok($"Commande {commande.Numero} validée.");
    }

    public async Task<ResultDto> ReceptionnerCommandeAsync(ReceptionCommandeDto dto,
        string userId, CancellationToken ct)
    {
        await _uow.BeginTransactionAsync(ct);
        try
        {
            var commande = await _uow.CommandesAchat.GetAvecLignesAsync(dto.CommandeId, ct)
                ?? throw new InvalidOperationException("Commande introuvable.");

            foreach (var reception in dto.Lignes)
            {
                var ligne = commande.Lignes.FirstOrDefault(l => l.Id == reception.LigneId)
                    ?? throw new InvalidOperationException("Ligne introuvable.");

                ligne.EnregistrerReception(reception.QuantiteRecue);

                // Entrée en stock
                var stock = await _uow.Stocks.GetByArticleEmplacementAsync(
                    ligne.ArticleId, dto.EmplacementReceptionId, ct);
                if (stock is null)
                {
                    stock = StockArticle.Creer(ligne.ArticleId, dto.EmplacementReceptionId, userId);
                    await _uow.Stocks.AddAsync(stock, ct);
                }

                stock.ConfirmerReception(reception.QuantiteRecue, ligne.QuantiteCommandee, userId);
                _uow.Stocks.Update(stock);

                // Mouvement d'entrée
                var article = await _uow.Articles.GetByIdAsync(ligne.ArticleId, ct);
                var mouvement = MouvementStock.Creer(
                    ligne.ArticleId, dto.EmplacementReceptionId,
                    TypeMouvement.Entree, reception.QuantiteRecue, ligne.PrixUnitaire,
                    commande.Numero, userId);
                await _uow.Mouvements.AddAsync(mouvement, ct);
            }

            commande.EnregistrerReception(DateTime.UtcNow, userId);
            _uow.CommandesAchat.Update(commande);

            await _uow.AuditTrails.AddAsync(AuditTrail.Creer(
                userId, userId, "Réception commande",
                "CommandeAchat", commande.Id.ToString(),
                $"N°: {commande.Numero}"), ct);

            await _uow.SaveChangesAsync(ct);
            await _uow.CommitTransactionAsync(ct);
            return ResultDto.Ok($"Réception de la commande {commande.Numero} enregistrée.");
        }
        catch (Exception ex)
        {
            await _uow.RollbackTransactionAsync(ct);
            return ResultDto.Erreur(ex.Message);
        }
    }

    public async Task<ResultDto> AnnulerCommandeAsync(Guid id, string motif,
        string userId, CancellationToken ct)
    {
        var commande = await _uow.CommandesAchat.GetByIdAsync(id, ct);
        if (commande is null) return ResultDto.Erreur("Commande introuvable.");
        commande.Annuler(motif, userId);
        _uow.CommandesAchat.Update(commande);
        await _uow.SaveChangesAsync(ct);
        return ResultDto.Ok("Commande annulée.");
    }

    public async Task<IEnumerable<CommandeAchatDto>> GetCommandesEnAttenteAsync(CancellationToken ct)
    {
        var commandes = await _uow.CommandesAchat.GetCommandesEnAttenteAsync(ct);
        return commandes.Select(MapToDto);
    }

    private static CommandeAchatDto MapToDto(CommandeAchat c) => new(
        c.Id, c.Numero, c.FournisseurId,
        c.Fournisseur?.RaisonSociale ?? string.Empty,
        c.DateCommande, c.DateLivraisonPrevue, c.DateLivraisonReelle,
        c.Statut, c.Statut.ToString(),
        c.MontantHT, c.TVA, c.MontantTTC,
        c.Commentaire,
        c.Lignes.Select(l => new LigneCommandeAchatDto(
            l.Id, l.ArticleId,
            l.Article?.Code ?? string.Empty,
            l.Designation, l.QuantiteCommandee, l.QuantiteRecue,
            l.PrixUnitaire, l.Unite, l.MontantLigne)).ToList());
}

// ─── SERVICE DASHBOARD ────────────────────────────────────────────────────────
public class DashboardService : IDashboardService
{
    private readonly IUnitOfWork _uow;
    private readonly IArticleService _articleService;
    private readonly ICommandeAchatService _commandeService;

    public DashboardService(IUnitOfWork uow, IArticleService articleService,
        ICommandeAchatService commandeService)
    {
        _uow = uow;
        _articleService = articleService;
        _commandeService = commandeService;
    }

    public async Task<DashboardDto> GetDashboardAsync(CancellationToken ct)
    {
        var articles = await _uow.Articles.GetAllAsync(ct);
        var totalArticles = articles.Count();

        var alertes = new List<AlerteStockDto>();
        int enAlerte = 0, enRupture = 0;
        decimal valeurTotale = 0;

        foreach (var article in articles)
        {
            var qte = await _uow.Stocks.GetQuantiteTotaleAsync(article.Id, ct);
            valeurTotale += qte * article.PrixAchat;

            if (article.EstEnRuptureStock(qte))
            {
                enRupture++;
                alertes.Add(new AlerteStockDto(article.Id, article.Code,
                    article.Designation, qte, article.SeuilAlerte,
                    article.StockMinimum, "RUPTURE"));
            }
            else if (article.EstEnAlerteStock(qte))
            {
                enAlerte++;
                alertes.Add(new AlerteStockDto(article.Id, article.Code,
                    article.Designation, qte, article.SeuilAlerte,
                    article.StockMinimum, "ALERTE"));
            }
        }

        var commandesEnAttente = await _commandeService.GetCommandesEnAttenteAsync(ct);
        var derniersMouvements = await _uow.Mouvements.GetHistoriqueAsync(
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, ct);

        return new DashboardDto(
            totalArticles, enAlerte, enRupture,
            commandesEnAttente.Count(), 0, valeurTotale,
            alertes.Take(10).ToList(),
            derniersMouvements.Take(10).Select(m => new MouvementStockDto(
                m.Id, m.Article?.Code ?? "", m.Article?.Designation ?? "",
                m.EmplacementSource?.Code ?? "", null,
                m.TypeMouvement, m.TypeMouvement.ToString(),
                m.Quantite, m.ValeurUnitaire, m.ValeurTotale,
                m.Reference, m.Motif, null, null, m.DateMouvement, m.CreatedBy)).ToList(),
            commandesEnAttente.Take(5).ToList());
    }
}
