using GestionStock.Domain.Entities;
using GestionStock.Domain.Enums;
using GestionStock.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GestionStock.Infrastructure.Data;

/// <summary>
/// Contexte principal EF Core – SQL Server.
/// Hérite d'IdentityDbContext pour la gestion des utilisateurs (NF-SEC-02).
/// </summary>
public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // ─── DbSets ───────────────────────────────────────────────────────────────
    public DbSet<Article> Articles => Set<Article>();
    public DbSet<StockArticle> Stocks => Set<StockArticle>();
    public DbSet<Emplacement> Emplacements => Set<Emplacement>();
    public DbSet<Fournisseur> Fournisseurs => Set<Fournisseur>();
    public DbSet<Lot> Lots => Set<Lot>();
    public DbSet<MouvementStock> MouvementsStock => Set<MouvementStock>();
    public DbSet<CommandeAchat> CommandesAchat => Set<CommandeAchat>();
    public DbSet<LigneCommandeAchat> LignesCommandeAchat => Set<LigneCommandeAchat>();
    public DbSet<AuditTrail> AuditTrails => Set<AuditTrail>();
    public DbSet<ParametresEntreprise> Parametres => Set<ParametresEntreprise>();
    public DbSet<Depot> Depots => Set<Depot>();
    public DbSet<FamilleArticle> FamillesArticles => Set<FamilleArticle>();
    public DbSet<Inventaire> Inventaires => Set<Inventaire>();
    public DbSet<LigneInventaire> LignesInventaire => Set<LigneInventaire>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ─── ARTICLE ──────────────────────────────────────────────────────────
        builder.Entity<Article>(e =>
        {
            e.ToTable("Articles");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.Property(x => x.CodeBarres).HasMaxLength(50);
            e.Property(x => x.Designation).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.Categorie).HasMaxLength(100);
            e.Property(x => x.FamilleArticle).HasMaxLength(100);
            e.Property(x => x.Unite).HasMaxLength(20);
            e.Property(x => x.PrixAchat).HasPrecision(18, 4);
            e.Property(x => x.PrixVente).HasPrecision(18, 4);
            e.Property(x => x.ValeurStockMoyen).HasPrecision(18, 4);
            e.Property(x => x.Statut).HasConversion<int>();
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.CodeBarres);
            e.HasIndex(x => x.Categorie);

            e.HasOne(x => x.FournisseurPrincipal)
             .WithMany()
             .HasForeignKey(x => x.FournisseurPrincipalId)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.SetNull);
        });

        // ─── STOCK ARTICLE ────────────────────────────────────────────────────
        builder.Entity<StockArticle>(e =>
        {
            e.ToTable("Stocks");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Article).WithMany(a => a.StocksParEmplacement)
             .HasForeignKey(x => x.ArticleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Emplacement).WithMany()
             .HasForeignKey(x => x.EmplacementId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Lot).WithMany()
             .HasForeignKey(x => x.LotId).IsRequired(false);
            e.HasIndex(x => new { x.ArticleId, x.EmplacementId });
        });

        // ─── EMPLACEMENT ──────────────────────────────────────────────────────
        builder.Entity<Emplacement>(e =>
        {
            e.ToTable("Emplacements");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(30).IsRequired();
            e.Property(x => x.Libelle).HasMaxLength(100);
            e.Property(x => x.Zone).HasMaxLength(50);
            e.Property(x => x.Type).HasConversion<int>();
            e.HasIndex(x => x.Code).IsUnique();
        });

        // ─── FOURNISSEUR ──────────────────────────────────────────────────────
        builder.Entity<Fournisseur>(e =>
        {
            e.ToTable("Fournisseurs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(10).IsRequired();
            e.Property(x => x.RaisonSociale).HasMaxLength(200).IsRequired();
            e.Property(x => x.Email).HasMaxLength(150);
            e.Property(x => x.Telephone).HasMaxLength(30);
            e.Property(x => x.Adresse).HasMaxLength(300);
            e.Property(x => x.Statut).HasConversion<int>();
            e.HasIndex(x => x.Code).IsUnique();
        });

        // ─── LOT ─────────────────────────────────────────────────────────────
        builder.Entity<Lot>(e =>
        {
            e.ToTable("Lots");
            e.HasKey(x => x.Id);
            e.Property(x => x.NumeroLot).HasMaxLength(50).IsRequired();
            e.Property(x => x.Statut).HasConversion<int>();
            e.HasOne(x => x.Article).WithMany(a => a.Lots)
             .HasForeignKey(x => x.ArticleId);
            e.HasIndex(x => new { x.ArticleId, x.NumeroLot }).IsUnique();
        });

        // ─── MOUVEMENT STOCK ──────────────────────────────────────────────────
        builder.Entity<MouvementStock>(e =>
        {
            e.ToTable("MouvementsStock");
            e.HasKey(x => x.Id);
            e.Property(x => x.TypeMouvement).HasConversion<int>();
            e.Property(x => x.ValeurUnitaire).HasPrecision(18, 4);
            e.Property(x => x.Reference).HasMaxLength(100);
            e.Property(x => x.Motif).HasMaxLength(500);
            e.HasOne(x => x.Article).WithMany()
             .HasForeignKey(x => x.ArticleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.EmplacementSource).WithMany()
             .HasForeignKey(x => x.EmplacementSourceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.EmplacementDestination).WithMany()
             .HasForeignKey(x => x.EmplacementDestinationId)
             .IsRequired(false).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.ArticleId);
            e.HasIndex(x => x.DateMouvement);
        });

        // ─── COMMANDE ACHAT ───────────────────────────────────────────────────
        builder.Entity<CommandeAchat>(e =>
        {
            e.ToTable("CommandesAchat");
            e.HasKey(x => x.Id);
            e.Property(x => x.Numero).HasMaxLength(30).IsRequired();
            e.Property(x => x.Statut).HasConversion<int>();
            e.Property(x => x.Commentaire).HasMaxLength(1000);
            e.HasOne(x => x.Fournisseur).WithMany(f => f.Commandes)
             .HasForeignKey(x => x.FournisseurId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lignes).WithOne()
             .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.Numero).IsUnique();
        });

        // ─── LIGNE COMMANDE ACHAT ─────────────────────────────────────────────
        builder.Entity<LigneCommandeAchat>(e =>
        {
            e.ToTable("LignesCommandeAchat");
            e.HasKey(x => x.Id);
            e.Property(x => x.Designation).HasMaxLength(200);
            e.Property(x => x.Unite).HasMaxLength(20);
            e.Property(x => x.PrixUnitaire).HasPrecision(18, 4);
        });

        // ─── AUDIT TRAIL ──────────────────────────────────────────────────────
        builder.Entity<AuditTrail>(e =>
        {
            e.ToTable("AuditTrails");
            e.HasKey(x => x.Id);
            e.Property(x => x.UserId).HasMaxLength(450);
            e.Property(x => x.UserEmail).HasMaxLength(256);
            e.Property(x => x.Action).HasMaxLength(200);
            e.Property(x => x.Entite).HasMaxLength(100);
            e.Property(x => x.EntiteId).HasMaxLength(450);
            e.Property(x => x.Details).HasColumnType("nvarchar(max)");
            e.Property(x => x.AdresseIp).HasMaxLength(45);
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => new { x.Entite, x.EntiteId });
            e.HasIndex(x => x.Timestamp);
        });

        // ─── INVENTAIRE ───────────────────────────────────────────────────────
        builder.Entity<Inventaire>(e =>
        {
            e.ToTable("Inventaires");
            e.HasKey(x => x.Id);
            e.Property(x => x.Reference).HasMaxLength(30).IsRequired();
            e.Property(x => x.Type).HasConversion<int>();
            e.Property(x => x.Statut).HasConversion<int>();
            e.Property(x => x.Zone).HasMaxLength(50);
            e.HasMany(x => x.Lignes).WithOne().OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<LigneInventaire>(e =>
        {
            e.ToTable("LignesInventaire");
            e.HasKey(x => x.Id);
        });

        builder.Entity<ParametresEntreprise>(e =>
        {
            e.ToTable("Parametres");
            e.HasKey(x => x.Id);
            // Id fixe = 1 (singleton), ne pas générer automatiquement
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.RaisonSociale).HasMaxLength(200);
            e.Property(x => x.Siret).HasMaxLength(20);
            e.Property(x => x.Email).HasMaxLength(150);
            e.Property(x => x.TauxTVA).HasPrecision(5, 2);
            e.Property(x => x.EntrepotSurface).HasPrecision(10, 2);
        });

        builder.Entity<Depot>(e =>
        {
            e.ToTable("Depots");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.Property(x => x.Libelle).HasMaxLength(200).IsRequired();
            e.Property(x => x.Adresse).HasMaxLength(300);
            e.Property(x => x.Ville).HasMaxLength(100);
            e.Property(x => x.CodePostal).HasMaxLength(10);
            e.Property(x => x.Pays).HasMaxLength(100);
            e.Property(x => x.SurfaceM2).HasPrecision(10, 2);
        });

        builder.Entity<FamilleArticle>(e =>
        {
            e.ToTable("FamillesArticles");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.Property(x => x.Libelle).HasMaxLength(200).IsRequired();
            e.Property(x => x.Couleur).HasMaxLength(10);
            e.HasOne(x => x.Parent).WithMany(x => x.SousFamilles)
             .HasForeignKey(x => x.ParentId).IsRequired(false)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ─── DONNÉES DE RÉFÉRENCE ─────────────────────────────────────────────
    }
}
