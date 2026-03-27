using System.Linq.Expressions;
using GestionStock.Domain.Entities;
using GestionStock.Domain.Enums;
using GestionStock.Domain.Interfaces;
using GestionStock.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GestionStock.Infrastructure.Repositories;

// ─── REPOSITORY GÉNÉRIQUE ────────────────────────────────────────────────────
public class Repository<T> : IRepository<T> where T : BaseEntity
{
    protected readonly AppDbContext _context;
    protected readonly DbSet<T> _dbSet;

    public Repository(AppDbContext context)
    {
        _context = context;
        _dbSet = context.Set<T>();
    }

    public async Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _dbSet.FindAsync(new object[] { id }, ct);

    public async Task<IEnumerable<T>> GetAllAsync(CancellationToken ct = default)
        => await _dbSet.AsNoTracking().ToListAsync(ct);

    public async Task<IEnumerable<T>> FindAsync(
        Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        => await _dbSet.AsNoTracking().Where(predicate).ToListAsync(ct);

    public async Task<bool> ExistsAsync(
        Expression<Func<T, bool>> predicate, CancellationToken ct = default)
        => await _dbSet.AnyAsync(predicate, ct);

    public async Task AddAsync(T entity, CancellationToken ct = default)
        => await _dbSet.AddAsync(entity, ct);

    public void Update(T entity) => _dbSet.Update(entity);

    public void Remove(T entity) => _dbSet.Remove(entity);

    public async Task<int> CountAsync(
        Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default)
        => predicate is null
            ? await _dbSet.CountAsync(ct)
            : await _dbSet.CountAsync(predicate, ct);
}

// ─── ARTICLE REPOSITORY ───────────────────────────────────────────────────────
public class ArticleRepository : Repository<Article>, IArticleRepository
{
    public ArticleRepository(AppDbContext context) : base(context) { }

    public async Task<Article?> GetByCodeAsync(string code, CancellationToken ct = default)
        => await _dbSet
            .Include(a => a.FournisseurPrincipal)
            .FirstOrDefaultAsync(a => a.Code == code.ToUpper(), ct);

    public async Task<Article?> GetByCodeBarresAsync(string codeBarres, CancellationToken ct = default)
        => await _dbSet.FirstOrDefaultAsync(a => a.CodeBarres == codeBarres, ct);

    public async Task<IEnumerable<Article>> GetEnAlerteStockAsync(CancellationToken ct = default)
        => await _dbSet
            .Where(a => a.Statut == StatutArticle.Actif)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task<IEnumerable<Article>> GetByCategorieAsync(
        string categorie, CancellationToken ct = default)
        => await _dbSet
            .Where(a => a.Categorie == categorie)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task<IEnumerable<Article>> SearchAsync(string terme, CancellationToken ct = default)
    {
        var t = terme.ToLower();
        return await _dbSet
            .Where(a => a.Code.ToLower().Contains(t)
                     || a.Designation.ToLower().Contains(t)
                     || a.CodeBarres.Contains(terme)
                     || a.Categorie.ToLower().Contains(t))
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<bool> CodeExisteDejaAsync(
        string code, Guid? excludeId = null, CancellationToken ct = default)
    {
        var query = _dbSet.Where(a => a.Code == code.ToUpper());
        if (excludeId.HasValue) query = query.Where(a => a.Id != excludeId.Value);
        return await query.AnyAsync(ct);
    }
}

// ─── STOCK REPOSITORY ─────────────────────────────────────────────────────────
public class StockRepository : Repository<StockArticle>, IStockRepository
{
    public StockRepository(AppDbContext context) : base(context) { }

    public async Task<StockArticle?> GetByArticleEmplacementAsync(
        Guid articleId, Guid emplacementId, CancellationToken ct = default)
        => await _dbSet
            .FirstOrDefaultAsync(s =>
                s.ArticleId == articleId && s.EmplacementId == emplacementId, ct);

    public async Task<IEnumerable<StockArticle>> GetByArticleAsync(
        Guid articleId, CancellationToken ct = default)
        => await _dbSet
            .Include(s => s.Emplacement)
            .Include(s => s.Lot)
            .Where(s => s.ArticleId == articleId)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task<IEnumerable<StockArticle>> GetByEmplacementAsync(
        Guid emplacementId, CancellationToken ct = default)
        => await _dbSet
            .Include(s => s.Article)
            .Where(s => s.EmplacementId == emplacementId)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task<int> GetQuantiteTotaleAsync(Guid articleId, CancellationToken ct = default)
        => await _dbSet
            .Where(s => s.ArticleId == articleId)
            .SumAsync(s => s.QuantiteDisponible + s.QuantiteReservee, ct);

    public async Task<IEnumerable<(Guid ArticleId, int QuantiteTotale)>> GetStocksEnAlerteAsync(
        CancellationToken ct = default)
    {
        var stocks = await _dbSet
            .GroupBy(s => s.ArticleId)
            .Select(g => new
            {
                ArticleId = g.Key,
                Total = g.Sum(s => s.QuantiteDisponible + s.QuantiteReservee)
            })
            .ToListAsync(ct);

        return stocks.Select(s => (s.ArticleId, s.Total));
    }
}

// ─── COMMANDE ACHAT REPOSITORY ────────────────────────────────────────────────
public class CommandeAchatRepository : Repository<CommandeAchat>, ICommandeAchatRepository
{
    public CommandeAchatRepository(AppDbContext context) : base(context) { }

    public async Task<CommandeAchat?> GetByNumeroAsync(string numero, CancellationToken ct = default)
        => await _dbSet
            .Include(c => c.Fournisseur)
            .FirstOrDefaultAsync(c => c.Numero == numero, ct);

    public async Task<CommandeAchat?> GetAvecLignesAsync(Guid id, CancellationToken ct = default)
        => await _dbSet
            .Include(c => c.Fournisseur)
            .Include(c => c.Lignes)
                .ThenInclude(l => l.Article)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IEnumerable<CommandeAchat>> GetByFournisseurAsync(
        Guid fournisseurId, CancellationToken ct = default)
        => await _dbSet
            .Include(c => c.Fournisseur)
            .Where(c => c.FournisseurId == fournisseurId)
            .OrderByDescending(c => c.DateCommande)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task<IEnumerable<CommandeAchat>> GetCommandesEnAttenteAsync(
        CancellationToken ct = default)
        => await _dbSet
            .Include(c => c.Fournisseur)
            .Include(c => c.Lignes)
            .Where(c => c.Statut == StatutCommande.Confirmee
                     || c.Statut == StatutCommande.EnCours
                     || c.Statut == StatutCommande.Soumise)
            .OrderBy(c => c.DateLivraisonPrevue)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task<string> GenererNumeroCommandeAsync(CancellationToken ct = default)
    {
        var annee = DateTime.Now.Year;
        var mois = DateTime.Now.Month.ToString("D2");
        var count = await _dbSet
            .CountAsync(c => c.DateCommande.Year == annee, ct);
        return $"CA-{annee}{mois}-{(count + 1):D4}";
    }
}

// ─── MOUVEMENT REPOSITORY ─────────────────────────────────────────────────────
public class MouvementRepository : Repository<MouvementStock>, IMouvementRepository
{
    public MouvementRepository(AppDbContext context) : base(context) { }

    public async Task<IEnumerable<MouvementStock>> GetByArticleAsync(
        Guid articleId, DateTime? du = null, DateTime? au = null, CancellationToken ct = default)
    {
        var query = _dbSet
            .Include(m => m.Article)
            .Include(m => m.EmplacementSource)
            .Include(m => m.Lot)
            .Where(m => m.ArticleId == articleId);

        if (du.HasValue) query = query.Where(m => m.DateMouvement >= du.Value);
        if (au.HasValue) query = query.Where(m => m.DateMouvement <= au.Value);

        return await query
            .OrderByDescending(m => m.DateMouvement)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<MouvementStock>> GetByEmplacementAsync(
        Guid emplacementId, CancellationToken ct = default)
        => await _dbSet
            .Include(m => m.Article)
            .Include(m => m.EmplacementSource)
            .Where(m => m.EmplacementSourceId == emplacementId
                     || m.EmplacementDestinationId == emplacementId)
            .OrderByDescending(m => m.DateMouvement)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task<IEnumerable<MouvementStock>> GetHistoriqueAsync(
        DateTime du, DateTime au, CancellationToken ct = default)
        => await _dbSet
            .Include(m => m.Article)
            .Include(m => m.EmplacementSource)
            .Where(m => m.DateMouvement >= du && m.DateMouvement <= au)
            .OrderByDescending(m => m.DateMouvement)
            .AsNoTracking()
            .ToListAsync(ct);
}

// ─── FOURNISSEUR REPOSITORY ───────────────────────────────────────────────────
public class FournisseurRepository : Repository<Fournisseur>, IFournisseurRepository
{
    public FournisseurRepository(AppDbContext context) : base(context) { }

    public async Task<Fournisseur?> GetByCodeAsync(string code, CancellationToken ct = default)
        => await _dbSet.FirstOrDefaultAsync(f => f.Code == code.ToUpper(), ct);

    public async Task<IEnumerable<Fournisseur>> SearchAsync(string terme, CancellationToken ct = default)
    {
        var t = terme.ToLower();
        return await _dbSet
            .Where(f => f.RaisonSociale.ToLower().Contains(t)
                     || f.Code.ToLower().Contains(t)
                     || f.Email.ToLower().Contains(t))
            .AsNoTracking()
            .ToListAsync(ct);
    }
}

// ─── AUDIT TRAIL REPOSITORY ───────────────────────────────────────────────────
public class AuditTrailRepository : IAuditTrailRepository
{
    private readonly AppDbContext _context;

    public AuditTrailRepository(AppDbContext context) => _context = context;

    public async Task AddAsync(AuditTrail audit, CancellationToken ct = default)
        => await _context.AuditTrails.AddAsync(audit, ct);

    public async Task<IEnumerable<AuditTrail>> GetByEntiteAsync(
        string entite, string entiteId, CancellationToken ct = default)
        => await _context.AuditTrails
            .Where(a => a.Entite == entite && a.EntiteId == entiteId)
            .OrderByDescending(a => a.Timestamp)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task<IEnumerable<AuditTrail>> GetByUserAsync(
        string userId, DateTime? du = null, DateTime? au = null, CancellationToken ct = default)
    {
        var query = _context.AuditTrails.Where(a => a.UserId == userId);
        if (du.HasValue) query = query.Where(a => a.Timestamp >= du.Value);
        if (au.HasValue) query = query.Where(a => a.Timestamp <= au.Value);
        return await query.OrderByDescending(a => a.Timestamp).AsNoTracking().ToListAsync(ct);
    }
}
