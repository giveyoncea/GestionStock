using System.Text.Json;
using GestionStock.Application.DTOs;
using GestionStock.Application.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace GestionStock.Application.Services;

public class CommercialOfflineSyncService : ICommercialOfflineSyncService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ICommercialConnectionStringProvider _connectionStringProvider;
    private readonly ICommercialClientService _clientService;
    private readonly ICommercialVenteCommandService _venteCommandService;
    private readonly ICommercialAchatCommandService _achatCommandService;
    private readonly ILogger<CommercialOfflineSyncService> _logger;

    public CommercialOfflineSyncService(
        ICommercialConnectionStringProvider connectionStringProvider,
        ICommercialClientService clientService,
        ICommercialVenteCommandService venteCommandService,
        ICommercialAchatCommandService achatCommandService,
        ILogger<CommercialOfflineSyncService> logger)
    {
        _connectionStringProvider = connectionStringProvider;
        _clientService = clientService;
        _venteCommandService = venteCommandService;
        _achatCommandService = achatCommandService;
        _logger = logger;
    }

    public async Task<CommercialOfflineBootstrapDto> GetBootstrapAsync(DateTime? sinceUtc = null, CancellationToken ct = default)
    {
        var dto = new CommercialOfflineBootstrapDto
        {
            ServerUtc = DateTime.UtcNow,
            SchemaVersion = 1
        };

        try
        {
            await using var conn = new SqlConnection(_connectionStringProvider.GetCurrentConnectionString());
            await conn.OpenAsync(ct);

            if (await TableExistsAsync(conn, "Parametres", ct))
                dto.Parametres = await LoadParametresAsync(conn, ct);

            if (await TableExistsAsync(conn, "Articles", ct))
                dto.Articles = await LoadArticlesAsync(conn, ct);

            if (await TableExistsAsync(conn, "Depots", ct))
                dto.Depots = await LoadDepotsAsync(conn, ct);

            if (await TableExistsAsync(conn, "Clients", ct))
                dto.Clients = await LoadClientsAsync(conn, ct);

            if (await TableExistsAsync(conn, "Fournisseurs", ct))
                dto.Fournisseurs = await LoadFournisseursAsync(conn, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du bootstrap offline commercial");
        }

        return dto;
    }

    public async Task<CommercialOfflinePushResponseDto> PushAsync(CommercialOfflinePushRequestDto request, CancellationToken ct = default)
    {
        var response = new CommercialOfflinePushResponseDto
        {
            ServerUtc = DateTime.UtcNow
        };

        foreach (var operation in request.Operations)
        {
            response.Results.Add(await ProcessOperationAsync(operation, request.UserId, ct));
        }

        return response;
    }

    private async Task<CommercialOfflineSyncOperationResultDto> ProcessOperationAsync(
        CommercialOfflineSyncOperationDto operation,
        string? userId,
        CancellationToken ct)
    {
        try
        {
            var entityType = operation.EntityType?.Trim().ToLowerInvariant() ?? string.Empty;
            var operationType = operation.OperationType?.Trim().ToLowerInvariant() ?? string.Empty;

            return (entityType, operationType) switch
            {
                ("client", "create") or ("clients", "create") => await CreateClientAsync(operation, userId, ct),
                ("client", "update") or ("clients", "update") => await UpdateClientAsync(operation, userId, ct),
                ("vente", "create") or ("ventes", "create") => await CreateVenteAsync(operation, userId, ct),
                ("achat", "create") or ("achats", "create") => await CreateAchatAsync(operation, userId, ct),
                _ => Unsupported(operation, "Cette entite offline n'est pas encore prise en charge par le push serveur.")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur push offline pour {EntityType}/{OperationType}", operation.EntityType, operation.OperationType);
            return Rejected(operation, ex.Message);
        }
    }

    private async Task<CommercialOfflineSyncOperationResultDto> CreateClientAsync(
        CommercialOfflineSyncOperationDto operation,
        string? userId,
        CancellationToken ct)
    {
        var payload = DeserializeClientPayload(operation.PayloadJson);
        if (payload is null)
            return Rejected(operation, "Payload client invalide.");

        var request = MapClientRequest(payload);
        var result = await _clientService.CreerClientAsync(request, NormalizeUser(userId), ct);

        return result.Result.Succes && result.Id.HasValue
            ? Applied(operation, result.Result.Message ?? "Client synchronise.", result.Id.Value.ToString(), result.Code)
            : Rejected(operation, result.Result.Message ?? "Creation du client impossible.");
    }

    private async Task<CommercialOfflineSyncOperationResultDto> UpdateClientAsync(
        CommercialOfflineSyncOperationDto operation,
        string? userId,
        CancellationToken ct)
    {
        var payload = DeserializeClientPayload(operation.PayloadJson);
        if (payload is null)
            return Rejected(operation, "Payload client invalide.");

        var serverId = payload.ServerId;
        if (!serverId.HasValue && Guid.TryParse(operation.EntityLocalId, out var parsedId))
            serverId = parsedId;

        if (!serverId.HasValue)
            return Rejected(operation, "Aucun identifiant serveur client n'a ete fourni pour la mise a jour.");

        var serverUpdatedAtUtc = await GetClientUpdatedAtUtcAsync(serverId.Value, ct);
        if (serverUpdatedAtUtc is null)
            return Rejected(operation, "Client introuvable sur le serveur.");

        if (payload.LastKnownServerUpdatedAtUtc.HasValue && serverUpdatedAtUtc.Value > payload.LastKnownServerUpdatedAtUtc.Value.ToUniversalTime())
        {
            return Conflict(
                operation,
                $"Le client a ete modifie sur le serveur le {serverUpdatedAtUtc.Value:yyyy-MM-dd HH:mm:ss} UTC. Synchronisation manuelle requise.",
                serverId.Value.ToString());
        }

        var request = MapClientRequest(payload);
        var result = await _clientService.ModifierClientAsync(serverId.Value, request, ct);

        return result.Succes
            ? Applied(operation, result.Message ?? "Client mis a jour.", serverId.Value.ToString(), null)
            : Rejected(operation, result.Message ?? "Mise a jour du client impossible.");
    }

    private async Task<CommercialOfflineSyncOperationResultDto> CreateVenteAsync(
        CommercialOfflineSyncOperationDto operation,
        string? userId,
        CancellationToken ct)
    {
        var payload = DeserializeVentePayload(operation.PayloadJson);
        if (payload is null)
            return Rejected(operation, "Payload vente invalide.");

        if (payload.ClientId == Guid.Empty)
            return Rejected(operation, "Le client est obligatoire pour synchroniser une vente offline.");

        if (payload.Lignes.Count == 0)
            return Rejected(operation, "La vente offline doit contenir au moins une ligne.");

        var request = new CommercialVenteRequestDto(
            payload.TypeDocument,
            payload.ClientId,
            payload.RepresentantId,
            payload.DocumentParentId,
            payload.DateDocument,
            payload.DateEcheance,
            payload.DateLivraisonPrevue,
            payload.AdresseLivraison,
            payload.DepotId,
            payload.FraisLivraison,
            payload.MontantAcompte,
            payload.TauxTVA,
            payload.ConditionsPaiement,
            payload.NotesInternes,
            payload.NotesExterne,
            payload.Lignes.Select(MapVenteLigneRequest).ToList());

        var result = await _venteCommandService.CreerVenteAsync(request, NormalizeUser(userId), ct);

        return result.Result.Succes && result.Id.HasValue
            ? Applied(operation, result.Result.Message ?? "Vente synchronisee.", result.Id.Value.ToString(), result.Numero)
            : Rejected(operation, result.Result.Message ?? "Creation de la vente impossible.");
    }

    private async Task<CommercialOfflineSyncOperationResultDto> CreateAchatAsync(
        CommercialOfflineSyncOperationDto operation,
        string? userId,
        CancellationToken ct)
    {
        var payload = DeserializeAchatPayload(operation.PayloadJson);
        if (payload is null)
            return Rejected(operation, "Payload achat invalide.");

        if (payload.FournisseurId == Guid.Empty)
            return Rejected(operation, "Le fournisseur est obligatoire pour synchroniser un achat offline.");

        if (payload.Lignes.Count == 0)
            return Rejected(operation, "L'achat offline doit contenir au moins une ligne.");

        var request = new CommercialAchatRequestDto(
            payload.TypeDocument,
            payload.FournisseurId,
            payload.DocumentParentId,
            payload.DateDocument,
            payload.DateLivraisonPrevue,
            payload.DepotId,
            payload.FraisLivraison,
            payload.NotesInternes,
            payload.Lignes.Select(MapAchatLigneRequest).ToList());

        var result = await _achatCommandService.CreerAchatAsync(request, NormalizeUser(userId), ct);

        return result.Result.Succes && result.Id.HasValue
            ? Applied(operation, result.Result.Message ?? "Achat synchronise.", result.Id.Value.ToString(), result.Numero)
            : Rejected(operation, result.Result.Message ?? "Creation de l'achat impossible.");
    }

    private async Task<DateTime?> GetClientUpdatedAtUtcAsync(Guid serverId, CancellationToken ct)
    {
        await using var conn = new SqlConnection(_connectionStringProvider.GetCurrentConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(@"
            SELECT COALESCE(UpdatedAt, CreatedAt)
            FROM Clients
            WHERE Id=@id", conn);
        cmd.Parameters.AddWithValue("@id", serverId);

        var value = await cmd.ExecuteScalarAsync(ct);
        if (value is null || value is DBNull)
            return null;

        return Convert.ToDateTime(value).ToUniversalTime();
    }

    private static CommercialVenteLigneRequestDto MapVenteLigneRequest(CommercialOfflineVenteLignePayloadDto ligne)
    {
        return new CommercialVenteLigneRequestDto(
            ligne.ArticleId,
            ligne.Designation,
            ligne.Quantite,
            ligne.PrixUnitaireHT,
            ligne.TauxRemise,
            ligne.TauxTVA,
            ligne.NumeroLot);
    }

    private static CommercialAchatLigneRequestDto MapAchatLigneRequest(CommercialOfflineAchatLignePayloadDto ligne)
    {
        return new CommercialAchatLigneRequestDto(
            ligne.ArticleId,
            ligne.Designation,
            ligne.Quantite,
            ligne.PrixUnitaireHT,
            ligne.TauxRemise,
            ligne.TauxTVA,
            ligne.NumeroLot);
    }

    private static CommercialClientRequestDto MapClientRequest(CommercialOfflineClientPayloadDto payload)
    {
        return new CommercialClientRequestDto(
            payload.RaisonSociale,
            payload.TypeClient,
            payload.Email,
            payload.Telephone,
            payload.Adresse,
            payload.CodePostal,
            payload.Ville,
            payload.Pays,
            payload.NumeroTVA,
            payload.Siret,
            payload.DelaiPaiementJours,
            payload.TauxRemise,
            payload.PlafondCredit,
            payload.Notes,
            payload.EstActif);
    }

    private static CommercialOfflineClientPayloadDto? DeserializeClientPayload(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return null;

        return JsonSerializer.Deserialize<CommercialOfflineClientPayloadDto>(payloadJson, JsonOptions);
    }

    private static CommercialOfflineVentePayloadDto? DeserializeVentePayload(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return null;

        return JsonSerializer.Deserialize<CommercialOfflineVentePayloadDto>(payloadJson, JsonOptions);
    }

    private static CommercialOfflineAchatPayloadDto? DeserializeAchatPayload(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return null;

        return JsonSerializer.Deserialize<CommercialOfflineAchatPayloadDto>(payloadJson, JsonOptions);
    }

    private static string NormalizeUser(string? userId)
    {
        return string.IsNullOrWhiteSpace(userId) ? "offline-sync" : userId.Trim();
    }

    private static CommercialOfflineSyncOperationResultDto Applied(
        CommercialOfflineSyncOperationDto operation,
        string message,
        string? serverId,
        string? serverNumero)
    {
        return new CommercialOfflineSyncOperationResultDto(
            operation.EntityType,
            operation.EntityLocalId,
            operation.OperationType,
            true,
            "Applied",
            message,
            serverId,
            serverNumero);
    }

    private static CommercialOfflineSyncOperationResultDto Rejected(CommercialOfflineSyncOperationDto operation, string message)
    {
        return new CommercialOfflineSyncOperationResultDto(
            operation.EntityType,
            operation.EntityLocalId,
            operation.OperationType,
            false,
            "Rejected",
            message,
            null,
            null);
    }

    private static CommercialOfflineSyncOperationResultDto Conflict(CommercialOfflineSyncOperationDto operation, string message, string? serverId)
    {
        return new CommercialOfflineSyncOperationResultDto(
            operation.EntityType,
            operation.EntityLocalId,
            operation.OperationType,
            false,
            "Conflict",
            message,
            serverId,
            null);
    }

    private static CommercialOfflineSyncOperationResultDto Unsupported(CommercialOfflineSyncOperationDto operation, string message)
    {
        return new CommercialOfflineSyncOperationResultDto(
            operation.EntityType,
            operation.EntityLocalId,
            operation.OperationType,
            false,
            "NotImplemented",
            message,
            null,
            null);
    }

    private static async Task<bool> TableExistsAsync(SqlConnection conn, string tableName, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID(@tableName) IS NULL THEN 0 ELSE 1 END", conn);
        cmd.Parameters.AddWithValue("@tableName", tableName);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct) ?? 0) == 1;
    }

    private static async Task<ParametresDto?> LoadParametresAsync(SqlConnection conn, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(@"
            SELECT TOP 1 RaisonSociale, Devise, SymboleDevise, NombreDecimalesMontant,
                   NombreDecimalesQuantite, FormatImpressionDocuments, FormatImpressionRecus,
                   GabaritInterface, LogoEntreprise
            FROM Parametres
            WHERE Id = 1", conn);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return new ParametresDto
        {
            RaisonSociale = reader["RaisonSociale"]?.ToString() ?? string.Empty,
            Devise = reader["Devise"]?.ToString() ?? "EUR",
            SymboleDevise = reader["SymboleDevise"]?.ToString() ?? "EUR",
            NombreDecimalesMontant = reader["NombreDecimalesMontant"] is DBNull ? 2 : Convert.ToInt32(reader["NombreDecimalesMontant"]),
            NombreDecimalesQuantite = reader["NombreDecimalesQuantite"] is DBNull ? 3 : Convert.ToInt32(reader["NombreDecimalesQuantite"]),
            FormatImpressionDocuments = reader["FormatImpressionDocuments"]?.ToString() ?? "STANDARD",
            FormatImpressionRecus = reader["FormatImpressionRecus"]?.ToString() ?? "STANDARD",
            GabaritInterface = reader["GabaritInterface"]?.ToString() ?? "STANDARD",
            LogoEntreprise = reader["LogoEntreprise"]?.ToString() ?? string.Empty
        };
    }

    private static async Task<List<OfflineArticleSnapshotDto>> LoadArticlesAsync(SqlConnection conn, CancellationToken ct)
    {
        var list = new List<OfflineArticleSnapshotDto>();
        await using var cmd = new SqlCommand(@"
            SELECT Id, Code, Designation, ISNULL(Description,''), ISNULL(CodeBarres,''),
                   ISNULL(Unite,'PCS'), ISNULL(PrixAchat,0), ISNULL(PrixVente,0),
                   ISNULL(StockMinimum,0), CASE WHEN ISNULL(Statut,1) = 1 THEN CAST(1 as bit) ELSE CAST(0 as bit) END
            FROM Articles
            WHERE ISNULL(Statut,1) = 1
            ORDER BY Code", conn);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new OfflineArticleSnapshotDto(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetDecimal(6),
                reader.GetDecimal(7),
                reader.GetDecimal(8),
                reader.GetBoolean(9),
                null));
        }

        return list;
    }

    private static async Task<List<OfflineDepotSnapshotDto>> LoadDepotsAsync(SqlConnection conn, CancellationToken ct)
    {
        var list = new List<OfflineDepotSnapshotDto>();
        await using var cmd = new SqlCommand(@"
            SELECT Id, Code, Libelle, ISNULL(Adresse,''), EstActif
            FROM Depots
            WHERE EstActif = 1
            ORDER BY EstPrincipal DESC, Libelle ASC", conn);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new OfflineDepotSnapshotDto(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetBoolean(4),
                null));
        }

        return list;
    }

    private static async Task<List<OfflineClientSnapshotDto>> LoadClientsAsync(SqlConnection conn, CancellationToken ct)
    {
        var list = new List<OfflineClientSnapshotDto>();
        await using var cmd = new SqlCommand(@"
            SELECT Id, Code, RaisonSociale, ISNULL(Telephone,''), ISNULL(Email,''),
                   ISNULL(Adresse,''), ISNULL(Ville,''), ISNULL(Pays,'France'),
                   ISNULL(PlafondCredit,0), EstActif, COALESCE(UpdatedAt, CreatedAt)
            FROM Clients
            WHERE EstActif = 1
            ORDER BY RaisonSociale", conn);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new OfflineClientSnapshotDto(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetDecimal(8),
                reader.GetBoolean(9),
                reader.IsDBNull(10) ? null : reader.GetDateTime(10).ToUniversalTime()));
        }

        return list;
    }

    private static async Task<List<OfflineFournisseurSnapshotDto>> LoadFournisseursAsync(SqlConnection conn, CancellationToken ct)
    {
        var list = new List<OfflineFournisseurSnapshotDto>();
        await using var cmd = new SqlCommand(@"
            SELECT Id, Code, RaisonSociale, ISNULL(Telephone,''), ISNULL(Email,''),
                   ISNULL(Adresse,''), ISNULL(Ville,''), ISNULL(Pays,'France'),
                   CASE WHEN ISNULL(Statut,1) <> 3 THEN CAST(1 as bit) ELSE CAST(0 as bit) END,
                   COALESCE(UpdatedAt, CreatedAt)
            FROM Fournisseurs
            WHERE ISNULL(Statut,1) <> 3
            ORDER BY RaisonSociale", conn);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new OfflineFournisseurSnapshotDto(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetBoolean(8),
                reader.IsDBNull(9) ? null : reader.GetDateTime(9).ToUniversalTime()));
        }

        return list;
    }
}
