using System.Linq.Expressions;
using GestionStock.Domain.Entities;

namespace GestionStock.Domain.Interfaces;

// ─── REPOSITORY GÉNÉRIQUE ────────────────────────────────────────────────────
public interface IRepository<T> where T : BaseEntity
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IEnumerable<T>> GetAllAsync(CancellationToken ct = default);
    Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);
    Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);
    Task AddAsync(T entity, CancellationToken ct = default);
    void Update(T entity);
    void Remove(T entity);
    Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default);
}

// ─── REPOSITORIES SPÉCIFIQUES ─────────────────────────────────────────────────
public interface IArticleRepository : IRepository<Article>
{
    Task<Article?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<Article?> GetByCodeBarresAsync(string codeBarres, CancellationToken ct = default);
    Task<IEnumerable<Article>> GetEnAlerteStockAsync(CancellationToken ct = default);
    Task<IEnumerable<Article>> GetByCategorieAsync(string categorie, CancellationToken ct = default);
    Task<IEnumerable<Article>> SearchAsync(string terme, CancellationToken ct = default);
    Task<bool> CodeExisteDejaAsync(string code, Guid? excludeId = null, CancellationToken ct = default);
}

public interface IStockRepository : IRepository<StockArticle>
{
    Task<StockArticle?> GetByArticleEmplacementAsync(Guid articleId, Guid emplacementId, CancellationToken ct = default);
    Task<IEnumerable<StockArticle>> GetByArticleAsync(Guid articleId, CancellationToken ct = default);
    Task<IEnumerable<StockArticle>> GetByEmplacementAsync(Guid emplacementId, CancellationToken ct = default);
    Task<int> GetQuantiteTotaleAsync(Guid articleId, CancellationToken ct = default);
    Task<IEnumerable<(Guid ArticleId, int QuantiteTotale)>> GetStocksEnAlerteAsync(CancellationToken ct = default);
}

public interface ICommandeAchatRepository : IRepository<CommandeAchat>
{
    Task<CommandeAchat?> GetByNumeroAsync(string numero, CancellationToken ct = default);
    Task<CommandeAchat?> GetAvecLignesAsync(Guid id, CancellationToken ct = default);
    Task<IEnumerable<CommandeAchat>> GetByFournisseurAsync(Guid fournisseurId, CancellationToken ct = default);
    Task<IEnumerable<CommandeAchat>> GetCommandesEnAttenteAsync(CancellationToken ct = default);
    Task<string> GenererNumeroCommandeAsync(CancellationToken ct = default);
}

public interface IMouvementRepository : IRepository<MouvementStock>
{
    Task<IEnumerable<MouvementStock>> GetByArticleAsync(Guid articleId, DateTime? du = null, DateTime? au = null, CancellationToken ct = default);
    Task<IEnumerable<MouvementStock>> GetByEmplacementAsync(Guid emplacementId, CancellationToken ct = default);
    Task<IEnumerable<MouvementStock>> GetHistoriqueAsync(DateTime du, DateTime au, CancellationToken ct = default);
}

public interface IFournisseurRepository : IRepository<Fournisseur>
{
    Task<Fournisseur?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<IEnumerable<Fournisseur>> SearchAsync(string terme, CancellationToken ct = default);
}

public interface IAuditTrailRepository
{
    Task AddAsync(AuditTrail audit, CancellationToken ct = default);
    Task<IEnumerable<AuditTrail>> GetByEntiteAsync(string entite, string entiteId, CancellationToken ct = default);
    Task<IEnumerable<AuditTrail>> GetByUserAsync(string userId, DateTime? du = null, DateTime? au = null, CancellationToken ct = default);
}

// ─── UNIT OF WORK ─────────────────────────────────────────────────────────────
public interface IUnitOfWork : IDisposable
{
    IArticleRepository Articles { get; }
    IStockRepository Stocks { get; }
    ICommandeAchatRepository CommandesAchat { get; }
    IMouvementRepository Mouvements { get; }
    IFournisseurRepository Fournisseurs { get; }
    IAuditTrailRepository AuditTrails { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task BeginTransactionAsync(CancellationToken ct = default);
    Task CommitTransactionAsync(CancellationToken ct = default);
    Task RollbackTransactionAsync(CancellationToken ct = default);
}
