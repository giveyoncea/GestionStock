using GestionStock.Application.DTOs;

namespace GestionStock.Application.Interfaces;

public interface ICommercialConnectionStringProvider
{
    string GetCurrentConnectionString();
}

public interface ICommercialClientService
{
    Task<IReadOnlyList<CommercialClientDto>> GetClientsAsync(string? q = null, bool actifSeulement = true, CancellationToken ct = default);
    Task<(ResultDto Result, Guid? Id, string? Code)> CreerClientAsync(CommercialClientRequestDto dto, string userId, CancellationToken ct = default);
    Task<ResultDto> ModifierClientAsync(Guid id, CommercialClientRequestDto dto, CancellationToken ct = default);
}

public interface ICommercialVenteQueryService
{
    Task<IReadOnlyList<CommercialVenteListItemDto>> GetVentesAsync(
        int? type = null,
        int? statut = null,
        Guid? clientId = null,
        string? q = null,
        CancellationToken ct = default);

    Task<CommercialVenteDetailDto?> GetVenteAsync(Guid id, CancellationToken ct = default);
}

public interface ICommercialVenteCommandService
{
    Task<(ResultDto Result, Guid? Id, string? Numero)> CreerVenteAsync(
        CommercialVenteRequestDto dto,
        string userId,
        CancellationToken ct = default);

    Task<ResultDto> ModifierVenteAsync(
        Guid id,
        CommercialVenteRequestDto dto,
        CancellationToken ct = default);

    Task<ResultDto> SetStatutVenteAsync(
        Guid id,
        int statut,
        CancellationToken ct = default);

    Task<ResultDto> AnnulerVenteAsync(
        Guid id,
        CancellationToken ct = default);

    Task<(ResultDto Result, decimal? Solde, bool? EstRegle)> AjouterReglementAsync(
        Guid id,
        CommercialReglementRequestDto dto,
        string userId,
        CancellationToken ct = default);

    Task<(ResultDto Result, Guid? Id, string? Numero)> TransformerVenteAsync(
        Guid id,
        int typeDoc,
        string userId,
        CancellationToken ct = default);
}

public interface ICommercialAchatQueryService
{
    Task<IReadOnlyList<CommercialAchatListItemDto>> GetAchatsAsync(
        int? type = null,
        Guid? fournisseurId = null,
        CancellationToken ct = default);

    Task<CommercialAchatDetailDto?> GetAchatAsync(Guid id, CancellationToken ct = default);
}

public interface ICommercialOfflineSyncService
{
    Task<CommercialOfflineBootstrapDto> GetBootstrapAsync(DateTime? sinceUtc = null, CancellationToken ct = default);
    Task<CommercialOfflinePushResponseDto> PushAsync(CommercialOfflinePushRequestDto request, CancellationToken ct = default);
}

public interface ICommercialAchatCommandService
{
    Task<(ResultDto Result, Guid? Id, string? Numero)> CreerAchatAsync(
        CommercialAchatRequestDto dto,
        string userId,
        CancellationToken ct = default);

    Task<(ResultDto Result, Guid? Id, string? Numero)> TransformerAchatAsync(
        Guid id,
        int typeDoc,
        string userId,
        CancellationToken ct = default);
}

