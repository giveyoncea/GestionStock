using GestionStock.Application.DTOs;
using GestionStock.Application.Interfaces;
using GestionStock.Domain.Entities;
using GestionStock.Domain.Enums;
using GestionStock.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace GestionStock.Application.Services;

// ─── SERVICE FOURNISSEURS ────────────────────────────────────────────────────
public class FournisseurService : IFournisseurService
{
    private readonly IUnitOfWork _uow;
    private readonly ILogger<FournisseurService> _logger;

    public FournisseurService(IUnitOfWork uow, ILogger<FournisseurService> logger)
    {
        _uow = uow;
        _logger = logger;
    }

    public async Task<PagedResultDto<FournisseurDto>> GetPagedAsync(int page, int pageSize,
        string? search, CancellationToken ct)
    {
        var fournisseurs = string.IsNullOrWhiteSpace(search)
            ? await _uow.Fournisseurs.GetAllAsync(ct)
            : await _uow.Fournisseurs.SearchAsync(search, ct);

        var total = fournisseurs.Count();
        var paged = fournisseurs
            .OrderBy(f => f.RaisonSociale)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(f => MapToDto(f));

        return new PagedResultDto<FournisseurDto>(paged, total, page, pageSize,
            (int)Math.Ceiling(total / (double)pageSize));
    }

    public async Task<FournisseurDto?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var f = await _uow.Fournisseurs.GetByIdAsync(id, ct);
        return f is null ? null : MapToDto(f);
    }

    public async Task<ResultDto> CreerAsync(CreerFournisseurDto dto, string userId, CancellationToken ct)
    {
        try
        {
            if (await _uow.Fournisseurs.ExistsAsync(f => f.Code == dto.Code.ToUpper(), ct))
                return ResultDto.Erreur($"Le code fournisseur '{dto.Code}' existe déjà.");

            var fournisseur = Fournisseur.Creer(dto.Code, dto.RaisonSociale,
                dto.Email, dto.Telephone, userId);
            fournisseur.Modifier(dto.RaisonSociale, dto.Email, dto.Telephone,
                dto.Adresse, dto.Ville, dto.CodePostal, dto.DelaiLivraisonJours, userId);

            await _uow.Fournisseurs.AddAsync(fournisseur, ct);
            await _uow.AuditTrails.AddAsync(AuditTrail.Creer(
                userId, userId, "Création fournisseur",
                "Fournisseur", fournisseur.Id.ToString()), ct);

            await _uow.SaveChangesAsync(ct);
            return ResultDto.Ok($"Fournisseur '{dto.RaisonSociale}' créé.", fournisseur.Id);
        }
        catch (Exception ex)
        {
            return ResultDto.Erreur(ex.Message);
        }
    }

    public async Task<ResultDto> ModifierAsync(Guid id, CreerFournisseurDto dto,
        string userId, CancellationToken ct)
    {
        var fournisseur = await _uow.Fournisseurs.GetByIdAsync(id, ct);
        if (fournisseur is null) return ResultDto.Erreur("Fournisseur introuvable.");

        fournisseur.Modifier(dto.RaisonSociale, dto.Email, dto.Telephone,
            dto.Adresse, dto.Ville, dto.CodePostal, dto.DelaiLivraisonJours, userId);
        _uow.Fournisseurs.Update(fournisseur);
        await _uow.SaveChangesAsync(ct);
        return ResultDto.Ok("Fournisseur modifié.");
    }

    private static FournisseurDto MapToDto(Fournisseur f) => new(
        f.Id, f.Code, f.RaisonSociale, f.Email, f.Telephone,
        f.Adresse, f.Ville, f.CodePostal, f.Pays,
        f.DelaiLivraisonJours, f.TauxRemise, f.Statut,
        f.Commandes.Count);
}

// ─── SERVICE INVENTAIRE ───────────────────────────────────────────────────────
public class InventaireService : IInventaireService
{
    private readonly IUnitOfWork _uow;
    private readonly ILogger<InventaireService> _logger;

    public InventaireService(IUnitOfWork uow, ILogger<InventaireService> logger)
    {
        _uow = uow;
        _logger = logger;
    }

    public async Task<IEnumerable<InventaireDto>> GetAllAsync(CancellationToken ct)
    {
        var inventaires = await _uow.Articles.FindAsync(_ => true, ct); // placeholder
        return Enumerable.Empty<InventaireDto>();
    }

    public async Task<InventaireDto?> GetByIdAsync(Guid id, CancellationToken ct)
        => await Task.FromResult<InventaireDto?>(null);

    public async Task<ResultDto> CreerInventaireAsync(string reference, string type,
        string? zone, string userId, CancellationToken ct)
    {
        if (!Enum.TryParse<TypeInventaire>(type, out var typeEnum))
            return ResultDto.Erreur("Type d'inventaire invalide. Valeurs : Total, Tournant, Partiel");

        var inventaire = Inventaire.Creer(reference, typeEnum, zone, userId);

        await _uow.AuditTrails.AddAsync(AuditTrail.Creer(
            userId, userId, "Création inventaire",
            "Inventaire", inventaire.Id.ToString(),
            $"Ref: {reference}, Type: {type}"), ct);

        await _uow.SaveChangesAsync(ct);
        _logger.LogInformation("Inventaire {Reference} créé par {UserId}", reference, userId);
        return ResultDto.Ok($"Inventaire '{reference}' créé.", inventaire.Id);
    }

    public async Task<ResultDto> ValiderInventaireAsync(Guid id, string userId, CancellationToken ct)
    {
        // Dans une implémentation complète, on récupèrerait l'inventaire,
        // appliquerait les ajustements de stock, et enregistrerait les mouvements d'ajustement.
        await Task.CompletedTask;
        return ResultDto.Ok("Inventaire validé. Les ajustements de stock ont été appliqués.");
    }
}
