using GestionStock.Application.DTOs;
using GestionStock.Application.Interfaces;
using GestionStock.Domain.Entities;
using GestionStock.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace GestionStock.Application.Services;

public class ArticleService : IArticleService
{
    private readonly IUnitOfWork _uow;
    private readonly ILogger<ArticleService> _logger;

    public ArticleService(IUnitOfWork uow, ILogger<ArticleService> logger)
    {
        _uow = uow;
        _logger = logger;
    }

    public async Task<PagedResultDto<ArticleDto>> GetPagedAsync(int page, int pageSize,
        string? search, string? categorie, CancellationToken ct)
    {
        IEnumerable<Article> articles;

        if (!string.IsNullOrWhiteSpace(search))
            articles = await _uow.Articles.SearchAsync(search, ct);
        else if (!string.IsNullOrWhiteSpace(categorie))
            articles = await _uow.Articles.GetByCategorieAsync(categorie, ct);
        else
            articles = await _uow.Articles.GetAllAsync(ct);

        var totalCount = articles.Count();
        var paged = articles
            .Skip((page - 1) * pageSize)
            .Take(pageSize);

        var dtos = new List<ArticleDto>();
        foreach (var a in paged)
        {
            var qte = await _uow.Stocks.GetQuantiteTotaleAsync(a.Id, ct);
            dtos.Add(MapToDto(a, qte));
        }

        return new PagedResultDto<ArticleDto>(
            dtos, totalCount, page, pageSize,
            (int)Math.Ceiling(totalCount / (double)pageSize));
    }

    public async Task<ArticleDto?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var article = await _uow.Articles.GetByIdAsync(id, ct);
        if (article is null) return null;
        var qte = await _uow.Stocks.GetQuantiteTotaleAsync(id, ct);
        return MapToDto(article, qte);
    }

    public async Task<ArticleDto?> GetByCodeAsync(string code, CancellationToken ct)
    {
        var article = await _uow.Articles.GetByCodeAsync(code, ct);
        if (article is null) return null;
        var qte = await _uow.Stocks.GetQuantiteTotaleAsync(article.Id, ct);
        return MapToDto(article, qte);
    }

    public async Task<ArticleDto?> GetByCodeBarresAsync(string codeBarres, CancellationToken ct)
    {
        var article = await _uow.Articles.GetByCodeBarresAsync(codeBarres, ct);
        if (article is null) return null;
        var qte = await _uow.Stocks.GetQuantiteTotaleAsync(article.Id, ct);
        return MapToDto(article, qte);
    }

    public async Task<ResultDto> CreerAsync(CreerArticleDto dto, string userId, CancellationToken ct)
    {
        try
        {
            if (await _uow.Articles.CodeExisteDejaAsync(dto.Code, null, ct))
                return ResultDto.Erreur($"Le code article '{dto.Code}' existe déjà.");

            var article = Article.Creer(
                dto.Code, dto.CodeBarres, dto.Designation,
                dto.Categorie, dto.Unite, dto.PrixAchat,
                dto.SeuilAlerte, dto.StockMinimum, dto.StockMaximum, userId);

            article.Modifier(dto.Designation, dto.Description, dto.Categorie,
                dto.FamilleArticle, dto.Unite,
                dto.PrixAchat, dto.PrixVente, dto.SeuilAlerte,
                dto.StockMinimum, dto.StockMaximum, dto.GestionLot, dto.GestionDLUO, userId);

            await _uow.Articles.AddAsync(article, ct);

            // Audit trail
            await _uow.AuditTrails.AddAsync(AuditTrail.Creer(
                userId, userId, "Création article",
                "Article", article.Id.ToString(),
                $"Code: {article.Code}, Désignation: {article.Designation}"), ct);

            await _uow.SaveChangesAsync(ct);

            _logger.LogInformation("Article {Code} créé par {UserId}", article.Code, userId);
            return ResultDto.Ok($"Article '{article.Code}' créé avec succès.", article.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la création de l'article {Code}", dto.Code);
            return ResultDto.Erreur($"Erreur lors de la création : {ex.Message}");
        }
    }

    public async Task<ResultDto> ModifierAsync(ModifierArticleDto dto, string userId, CancellationToken ct)
    {
        var article = await _uow.Articles.GetByIdAsync(dto.Id, ct);
        if (article is null) return ResultDto.Erreur("Article introuvable.");

        article.Modifier(dto.Designation, dto.Description, dto.Categorie,
            dto.FamilleArticle, dto.Unite,
            dto.PrixAchat, dto.PrixVente, dto.SeuilAlerte,
            dto.StockMinimum, dto.StockMaximum, dto.GestionLot, dto.GestionDLUO, userId);

        _uow.Articles.Update(article);

        await _uow.AuditTrails.AddAsync(AuditTrail.Creer(
            userId, userId, "Modification article",
            "Article", article.Id.ToString()), ct);

        await _uow.SaveChangesAsync(ct);
        return ResultDto.Ok("Article modifié avec succès.");
    }

    public async Task<ResultDto> DesactiverAsync(Guid id, string userId, CancellationToken ct)
    {
        var article = await _uow.Articles.GetByIdAsync(id, ct);
        if (article is null) return ResultDto.Erreur("Article introuvable.");

        article.Desactiver(userId);
        _uow.Articles.Update(article);
        await _uow.SaveChangesAsync(ct);
        return ResultDto.Ok("Article désactivé.");
    }

    public async Task<IEnumerable<ArticleDto>> GetEnAlerteAsync(CancellationToken ct)
    {
        var articles = await _uow.Articles.GetEnAlerteStockAsync(ct);
        var result = new List<ArticleDto>();
        foreach (var a in articles)
        {
            var qte = await _uow.Stocks.GetQuantiteTotaleAsync(a.Id, ct);
            if (a.EstEnAlerteStock(qte))
                result.Add(MapToDto(a, qte));
        }
        return result;
    }

    public async Task<IEnumerable<string>> GetCategoriesAsync(CancellationToken ct)
    {
        var articles = await _uow.Articles.GetAllAsync(ct);
        return articles.Select(a => a.Categorie).Distinct().OrderBy(c => c);
    }

    private static ArticleDto MapToDto(Article a, int qte) => new(
        a.Id, a.Code, a.CodeBarres, a.Designation, a.Description,
        a.Categorie, a.FamilleArticle, a.Unite,
        a.PrixAchat, a.PrixVente, a.SeuilAlerte, a.StockMinimum, a.StockMaximum,
        a.GestionLot, a.GestionDLUO, a.Statut, qte,
        a.EstEnAlerteStock(qte), a.EstEnRuptureStock(qte),
        a.FournisseurPrincipal?.RaisonSociale, a.CreatedAt);
}
