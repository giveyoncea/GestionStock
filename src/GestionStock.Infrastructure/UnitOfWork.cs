using GestionStock.Domain.Interfaces;
using GestionStock.Infrastructure.Data;
using GestionStock.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore.Storage;

namespace GestionStock.Infrastructure;

/// <summary>
/// Implémentation du pattern Unit of Work.
/// Garantit l'atomicité des transactions multi-agrégats.
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;
    private IDbContextTransaction? _transaction;

    private IArticleRepository? _articles;
    private IStockRepository? _stocks;
    private ICommandeAchatRepository? _commandesAchat;
    private IMouvementRepository? _mouvements;
    private IFournisseurRepository? _fournisseurs;
    private IAuditTrailRepository? _auditTrails;

    public UnitOfWork(AppDbContext context) => _context = context;

    public IArticleRepository Articles
        => _articles ??= new ArticleRepository(_context);

    public IStockRepository Stocks
        => _stocks ??= new StockRepository(_context);

    public ICommandeAchatRepository CommandesAchat
        => _commandesAchat ??= new CommandeAchatRepository(_context);

    public IMouvementRepository Mouvements
        => _mouvements ??= new MouvementRepository(_context);

    public IFournisseurRepository Fournisseurs
        => _fournisseurs ??= new FournisseurRepository(_context);

    public IAuditTrailRepository AuditTrails
        => _auditTrails ??= new AuditTrailRepository(_context);

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);

    public async Task BeginTransactionAsync(CancellationToken ct = default)
        => _transaction = await _context.Database.BeginTransactionAsync(ct);

    public async Task CommitTransactionAsync(CancellationToken ct = default)
    {
        if (_transaction is not null)
        {
            await _transaction.CommitAsync(ct);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken ct = default)
    {
        if (_transaction is not null)
        {
            await _transaction.RollbackAsync(ct);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public void Dispose()
    {
        _transaction?.Dispose();
        _context.Dispose();
    }
}
