using GestionStock.Application.DTOs;

namespace GestionStock.Application.Interfaces;

public interface IArticleService
{
    Task<PagedResultDto<ArticleDto>> GetPagedAsync(int page, int pageSize,
        string? search = null, string? categorie = null, CancellationToken ct = default);
    Task<ArticleDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ArticleDto?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<ArticleDto?> GetByCodeBarresAsync(string codeBarres, CancellationToken ct = default);
    Task<ResultDto> CreerAsync(CreerArticleDto dto, string userId, CancellationToken ct = default);
    Task<ResultDto> ModifierAsync(ModifierArticleDto dto, string userId, CancellationToken ct = default);
    Task<ResultDto> DesactiverAsync(Guid id, string userId, CancellationToken ct = default);
    Task<IEnumerable<ArticleDto>> GetEnAlerteAsync(CancellationToken ct = default);
    Task<IEnumerable<string>> GetCategoriesAsync(CancellationToken ct = default);
}

public interface IStockService
{
    Task<IEnumerable<StockResumeDto>> GetStocksResumeAsync(CancellationToken ct = default);
    Task<IEnumerable<StockArticleDto>> GetDetailsByArticleAsync(Guid articleId, CancellationToken ct = default);
    Task<ResultDto> EntreeStockAsync(EntreeStockDto dto, string userId, CancellationToken ct = default);
    Task<ResultDto> SortieStockAsync(SortieStockDto dto, string userId, CancellationToken ct = default);
    Task<ResultDto> TransfertStockAsync(TransfertStockDto dto, string userId, CancellationToken ct = default);
    Task<ResultDto> AjustementStockAsync(Guid articleId, Guid emplacementId, int quantiteReelle, string? motif, string userId, CancellationToken ct = default);
    Task<IEnumerable<MouvementStockDto>> GetHistoriqueMouvementsAsync(Guid? articleId,
        DateTime? du, DateTime? au, CancellationToken ct = default);
}

public interface ICommandeAchatService
{
    Task<PagedResultDto<CommandeAchatDto>> GetPagedAsync(int page, int pageSize,
        CancellationToken ct = default);
    Task<CommandeAchatDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ResultDto> CreerCommandeAsync(CreerCommandeAchatDto dto,
        string userId, CancellationToken ct = default);
    Task<ResultDto> ValiderCommandeAsync(Guid id, string userId, CancellationToken ct = default);
    Task<ResultDto> ReceptionnerCommandeAsync(ReceptionCommandeDto dto,
        string userId, CancellationToken ct = default);
    Task<ResultDto> AnnulerCommandeAsync(Guid id, string motif,
        string userId, CancellationToken ct = default);
    Task<IEnumerable<CommandeAchatDto>> GetCommandesEnAttenteAsync(CancellationToken ct = default);
}

public interface IFournisseurService
{
    Task<PagedResultDto<FournisseurDto>> GetPagedAsync(int page, int pageSize,
        string? search = null, CancellationToken ct = default);
    Task<FournisseurDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ResultDto> CreerAsync(CreerFournisseurDto dto, string userId, CancellationToken ct = default);
    Task<ResultDto> ModifierAsync(Guid id, CreerFournisseurDto dto,
        string userId, CancellationToken ct = default);
}

public interface IDashboardService
{
    Task<DashboardDto> GetDashboardAsync(CancellationToken ct = default);
}

public interface IInventaireService
{
    Task<IEnumerable<InventaireDto>> GetAllAsync(CancellationToken ct = default);
    Task<InventaireDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ResultDto> CreerInventaireAsync(string reference, string type,
        string? zone, string userId, CancellationToken ct = default);
    Task<ResultDto> ValiderInventaireAsync(Guid id, string userId, CancellationToken ct = default);
}
