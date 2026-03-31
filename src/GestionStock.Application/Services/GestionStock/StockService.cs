using GestionStock.Application.DTOs;
using GestionStock.Application.Interfaces;
using GestionStock.Domain.Entities;
using GestionStock.Domain.Enums;
using GestionStock.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace GestionStock.Application.Services;

public class StockService : IStockService
{
    private readonly IUnitOfWork _uow;
    private readonly ILogger<StockService> _logger;

    public StockService(IUnitOfWork uow, ILogger<StockService> logger)
    {
        _uow = uow;
        _logger = logger;
    }

    public async Task<IEnumerable<StockResumeDto>> GetStocksResumeAsync(CancellationToken ct)
    {
        var articles = await _uow.Articles.GetAllAsync(ct);
        var result = new List<StockResumeDto>();

        foreach (var article in articles.Where(a => a.Statut == StatutArticle.Actif))
        {
            var stocks = await _uow.Stocks.GetByArticleAsync(article.Id, ct);
            var qteTotal = stocks.Sum(s => s.QuantiteDisponible + s.QuantiteReservee);
            var qteDispo = stocks.Sum(s => s.QuantiteDisponible);
            var qteResa = stocks.Sum(s => s.QuantiteReservee);
            var valeur = qteDispo * article.PrixAchat;

            result.Add(new StockResumeDto(
                article.Id, article.Code, article.Designation,
                qteTotal, qteDispo, qteResa,
                article.EstEnAlerteStock(qteTotal),
                article.EstEnRuptureStock(qteTotal),
                valeur));
        }
        return result;
    }

    public async Task<IEnumerable<StockArticleDto>> GetDetailsByArticleAsync(
        Guid articleId, CancellationToken ct)
    {
        var stocks = await _uow.Stocks.GetByArticleAsync(articleId, ct);
        return stocks.Select(s => new StockArticleDto(
            s.Id, s.ArticleId,
            s.Article?.Code ?? string.Empty,
            s.Article?.Designation ?? string.Empty,
            s.EmplacementId,
            s.Emplacement?.Code ?? string.Empty,
            s.Emplacement?.Zone ?? string.Empty,
            s.QuantiteDisponible, s.QuantiteReservee, s.QuantiteEnCommande,
            s.Lot?.NumeroLot, s.Lot?.DatePeremption));
    }

    public async Task<ResultDto> EntreeStockAsync(EntreeStockDto dto, string userId, CancellationToken ct)
    {
        await _uow.BeginTransactionAsync(ct);
        try
        {
            var article = await _uow.Articles.GetByIdAsync(dto.ArticleId, ct)
                ?? throw new InvalidOperationException("Article introuvable.");

            // Créer ou récupérer l'entrée de stock pour cet emplacement
            var stock = await _uow.Stocks.GetByArticleEmplacementAsync(
                dto.ArticleId, dto.EmplacementId, ct);

            if (stock is null)
            {
                stock = StockArticle.Creer(dto.ArticleId, dto.EmplacementId, userId);
                await _uow.Stocks.AddAsync(stock, ct);
            }

            stock.AppliquerEntree(dto.Quantite, userId);
            _uow.Stocks.Update(stock);

            // Enregistrer le mouvement
            var mouvement = MouvementStock.Creer(
                dto.ArticleId, dto.EmplacementId,
                TypeMouvement.Entree, dto.Quantite, dto.PrixUnitaire,
                dto.Reference, userId);
            await _uow.Mouvements.AddAsync(mouvement, ct);

            // Audit
            await _uow.AuditTrails.AddAsync(AuditTrail.Creer(
                userId, userId, "Entrée stock",
                "Stock", stock.Id.ToString(),
                $"Article: {article.Code}, Qté: {dto.Quantite}, Ref: {dto.Reference}"), ct);

            await _uow.SaveChangesAsync(ct);
            await _uow.CommitTransactionAsync(ct);

            _logger.LogInformation("Entrée stock article {Code} x{Qty}", article.Code, dto.Quantite);
            return ResultDto.Ok($"Entrée de {dto.Quantite} unité(s) enregistrée.");
        }
        catch (Exception ex)
        {
            await _uow.RollbackTransactionAsync(ct);
            _logger.LogError(ex, "Erreur entrée stock article {Id}", dto.ArticleId);
            return ResultDto.Erreur(ex.Message);
        }
    }

    public async Task<ResultDto> SortieStockAsync(SortieStockDto dto, string userId, CancellationToken ct)
    {
        await _uow.BeginTransactionAsync(ct);
        try
        {
            var article = await _uow.Articles.GetByIdAsync(dto.ArticleId, ct)
                ?? throw new InvalidOperationException("Article introuvable.");

            var stock = await _uow.Stocks.GetByArticleEmplacementAsync(
                dto.ArticleId, dto.EmplacementId, ct)
                ?? throw new InvalidOperationException("Aucun stock trouvé pour cet emplacement.");

            stock.AppliquerSortie(dto.Quantite, userId);
            _uow.Stocks.Update(stock);

            var mouvement = MouvementStock.Creer(
                dto.ArticleId, dto.EmplacementId,
                TypeMouvement.Sortie, dto.Quantite, article.PrixAchat,
                dto.Reference, userId);
            await _uow.Mouvements.AddAsync(mouvement, ct);

            await _uow.AuditTrails.AddAsync(AuditTrail.Creer(
                userId, userId, "Sortie stock",
                "Stock", stock.Id.ToString(),
                $"Article: {article.Code}, Qté: -{dto.Quantite}"), ct);

            await _uow.SaveChangesAsync(ct);
            await _uow.CommitTransactionAsync(ct);

            return ResultDto.Ok($"Sortie de {dto.Quantite} unité(s) enregistrée.");
        }
        catch (Exception ex)
        {
            await _uow.RollbackTransactionAsync(ct);
            return ResultDto.Erreur(ex.Message);
        }
    }

    public async Task<ResultDto> TransfertStockAsync(TransfertStockDto dto, string userId, CancellationToken ct)
    {
        await _uow.BeginTransactionAsync(ct);
        try
        {
            var stockSource = await _uow.Stocks.GetByArticleEmplacementAsync(
                dto.ArticleId, dto.EmplacementSourceId, ct)
                ?? throw new InvalidOperationException("Stock source introuvable.");

            stockSource.AppliquerSortie(dto.Quantite, userId);
            _uow.Stocks.Update(stockSource);

            var stockDest = await _uow.Stocks.GetByArticleEmplacementAsync(
                dto.ArticleId, dto.EmplacementDestinationId, ct);
            if (stockDest is null)
            {
                stockDest = StockArticle.Creer(dto.ArticleId, dto.EmplacementDestinationId, userId);
                await _uow.Stocks.AddAsync(stockDest, ct);
            }
            stockDest.AppliquerEntree(dto.Quantite, userId);
            _uow.Stocks.Update(stockDest);

            var article = await _uow.Articles.GetByIdAsync(dto.ArticleId, ct);
            var mouvement = MouvementStock.Creer(
                dto.ArticleId, dto.EmplacementSourceId,
                TypeMouvement.Transfert, dto.Quantite, article?.PrixAchat ?? 0,
                "TRANSFERT", userId);
            await _uow.Mouvements.AddAsync(mouvement, ct);

            await _uow.SaveChangesAsync(ct);
            await _uow.CommitTransactionAsync(ct);

            return ResultDto.Ok($"Transfert de {dto.Quantite} unité(s) effectué.");
        }
        catch (Exception ex)
        {
            await _uow.RollbackTransactionAsync(ct);
            return ResultDto.Erreur(ex.Message);
        }
    }

    public async Task<IEnumerable<MouvementStockDto>> GetHistoriqueMouvementsAsync(
        Guid? articleId, DateTime? du, DateTime? au, CancellationToken ct)
    {
        IEnumerable<MouvementStock> mouvements;

        if (articleId.HasValue)
            mouvements = await _uow.Mouvements.GetByArticleAsync(articleId.Value, du, au, ct);
        else
            mouvements = await _uow.Mouvements.GetHistoriqueAsync(
                du ?? DateTime.UtcNow.AddDays(-30), au ?? DateTime.UtcNow, ct);

        return mouvements.Select(m => new MouvementStockDto(
            m.Id,
            m.Article?.Code ?? string.Empty,
            m.Article?.Designation ?? string.Empty,
            m.EmplacementSource?.Code ?? string.Empty,
            m.EmplacementDestination?.Code,
            m.TypeMouvement,
            m.TypeMouvement.ToString(),
            m.Quantite,
            m.ValeurUnitaire,
            m.ValeurTotale,
            m.Reference,
            m.Motif,
            m.Lot?.NumeroLot,
            null,
            m.DateMouvement,
            m.CreatedBy));
    }

    public async Task<ResultDto> AjustementStockAsync(
        Guid articleId, Guid emplacementId, int quantiteReelle,
        string? motif, string userId, CancellationToken ct)
    {
        await _uow.BeginTransactionAsync(ct);
        try
        {
            var stock = await _uow.Stocks.GetByArticleEmplacementAsync(articleId, emplacementId, ct);
            var article = await _uow.Articles.GetByIdAsync(articleId, ct);
            var prix = article?.PrixAchat ?? 0m;

            int quantiteAvant = stock?.QuantiteDisponible ?? 0;
            int ecart = quantiteReelle - quantiteAvant;

            if (stock is null)
            {
                stock = GestionStock.Domain.Entities.StockArticle.Creer(articleId, emplacementId, userId);
                await _uow.Stocks.AddAsync(stock, ct);
            }

            // Ajuste la quantité directement
            if (ecart > 0) stock.AppliquerEntree(ecart, userId);
            else if (ecart < 0) stock.AppliquerSortie(Math.Abs(ecart), userId);
            // ecart == 0 : rien à faire, on trace quand même
            _uow.Stocks.Update(stock);

            // Enregistrer le mouvement d'ajustement
            var mouvement = GestionStock.Domain.Entities.MouvementStock.Creer(
                articleId, emplacementId,
                GestionStock.Domain.Enums.TypeMouvement.Ajustement,
                Math.Abs(ecart), prix,
                motif ?? $"Inventaire: {quantiteAvant}→{quantiteReelle}", userId);
            await _uow.Mouvements.AddAsync(mouvement, ct);

            await _uow.SaveChangesAsync(ct);
            await _uow.CommitTransactionAsync(ct);

            return ResultDto.Ok($"Ajustement enregistré ({(ecart >= 0 ? "+" : "")}{ecart} unité(s)).");
        }
        catch (Exception ex)
        {
            await _uow.RollbackTransactionAsync(ct);
            return ResultDto.Erreur(ex.Message);
        }
    }
}
