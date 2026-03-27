using GestionStock.Application.DTOs;
using GestionStock.Application.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace GestionStock.Application.Services;

public class ClientCommercialService : ICommercialClientService
{
    private readonly ICommercialConnectionStringProvider _connectionStringProvider;
    private readonly ILogger<ClientCommercialService> _logger;

    public ClientCommercialService(
        ICommercialConnectionStringProvider connectionStringProvider,
        ILogger<ClientCommercialService> logger)
    {
        _connectionStringProvider = connectionStringProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CommercialClientDto>> GetClientsAsync(
        string? q = null,
        bool actifSeulement = true,
        CancellationToken ct = default)
    {
        var list = new List<CommercialClientDto>();

        await using var conn = new SqlConnection(_connectionStringProvider.GetCurrentConnectionString());
        await conn.OpenAsync(ct);
        await EnsureClientTablesAsync(conn, ct);

        var where = actifSeulement ? "WHERE c.EstActif=1" : "WHERE 1=1";
        if (!string.IsNullOrWhiteSpace(q))
            where += " AND (c.RaisonSociale LIKE @q OR c.Code LIKE @q OR c.Email LIKE @q)";

        await using var cmd = new SqlCommand($@"
            SELECT c.Id, c.Code, c.RaisonSociale, c.TypeClient, c.Email, c.Telephone,
                   c.Ville, c.TauxRemise, c.DelaiPaiementJours, c.PlafondCredit,
                   c.EstActif, c.CreatedAt,
                   ISNULL((SELECT SUM(MontantTTC-MontantRegle) FROM DocumentsVente
                            WHERE ClientId=c.Id AND Statut NOT IN (9,10,11)),0) AS Encours
            FROM Clients c {where}
            ORDER BY c.RaisonSociale", conn);

        if (!string.IsNullOrWhiteSpace(q))
            cmd.Parameters.AddWithValue("@q", $"%{q}%");

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new CommercialClientDto(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetDecimal(7),
                reader.GetInt32(8),
                reader.GetDecimal(9),
                reader.GetBoolean(10),
                reader.GetDateTime(11),
                reader.GetDecimal(12)));
        }

        return list;
    }

    public async Task<(ResultDto Result, Guid? Id, string? Code)> CreerClientAsync(
        CommercialClientRequestDto dto,
        string userId,
        CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqlConnection(_connectionStringProvider.GetCurrentConnectionString());
            await conn.OpenAsync(ct);
            await EnsureClientTablesAsync(conn, ct);

            var id = Guid.NewGuid();
            var count = 0;
            await using (var chk = new SqlCommand("SELECT COUNT(1) FROM Clients", conn))
                count = Convert.ToInt32(await chk.ExecuteScalarAsync(ct) ?? 0);

            var code = $"CLI{count + 1:D5}";

            await using var cmd = new SqlCommand(@"
                INSERT INTO Clients (Id,Code,RaisonSociale,TypeClient,Email,Telephone,
                    Adresse,CodePostal,Ville,Pays,NumeroTVA,Siret,
                    DelaiPaiementJours,TauxRemise,PlafondCredit,Notes,EstActif,CreatedAt,CreatedBy)
                VALUES (@id,@code,@rs,@type,@email,@tel,@adr,@cp,@ville,@pays,@tva,@siret,
                    @delai,@remise,@plafond,@notes,1,GETUTCDATE(),@user)", conn);

            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@code", code);
            cmd.Parameters.AddWithValue("@rs", dto.RaisonSociale);
            cmd.Parameters.AddWithValue("@type", dto.TypeClient);
            cmd.Parameters.AddWithValue("@email", (object?)dto.Email ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@tel", (object?)dto.Telephone ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@adr", (object?)dto.Adresse ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@cp", (object?)dto.CodePostal ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ville", (object?)dto.Ville ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@pays", dto.Pays ?? "France");
            cmd.Parameters.AddWithValue("@tva", (object?)dto.NumeroTVA ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@siret", (object?)dto.Siret ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@delai", dto.DelaiPaiementJours);
            cmd.Parameters.AddWithValue("@remise", dto.TauxRemise);
            cmd.Parameters.AddWithValue("@plafond", dto.PlafondCredit);
            cmd.Parameters.AddWithValue("@notes", (object?)dto.Notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@user", userId);

            await cmd.ExecuteNonQueryAsync(ct);
            return (ResultDto.Ok("Client créé."), id, code);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la création d'un client commercial");
            return (ResultDto.Erreur(ex.Message), null, null);
        }
    }

    public async Task<ResultDto> ModifierClientAsync(
        Guid id,
        CommercialClientRequestDto dto,
        CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqlConnection(_connectionStringProvider.GetCurrentConnectionString());
            await conn.OpenAsync(ct);
            await EnsureClientTablesAsync(conn, ct);

            await using var cmd = new SqlCommand(@"
                UPDATE Clients SET RaisonSociale=@rs, TypeClient=@type, Email=@email,
                    Telephone=@tel, Adresse=@adr, CodePostal=@cp, Ville=@ville, Pays=@pays,
                    NumeroTVA=@tva, Siret=@siret, DelaiPaiementJours=@delai,
                    TauxRemise=@remise, PlafondCredit=@plafond, Notes=@notes,
                    EstActif=@actif, UpdatedAt=GETUTCDATE()
                WHERE Id=@id", conn);

            cmd.Parameters.AddWithValue("@rs", dto.RaisonSociale);
            cmd.Parameters.AddWithValue("@type", dto.TypeClient);
            cmd.Parameters.AddWithValue("@email", (object?)dto.Email ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@tel", (object?)dto.Telephone ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@adr", (object?)dto.Adresse ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@cp", (object?)dto.CodePostal ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ville", (object?)dto.Ville ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@pays", dto.Pays ?? "France");
            cmd.Parameters.AddWithValue("@tva", (object?)dto.NumeroTVA ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@siret", (object?)dto.Siret ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@delai", dto.DelaiPaiementJours);
            cmd.Parameters.AddWithValue("@remise", dto.TauxRemise);
            cmd.Parameters.AddWithValue("@plafond", dto.PlafondCredit);
            cmd.Parameters.AddWithValue("@notes", (object?)dto.Notes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@actif", dto.EstActif);
            cmd.Parameters.AddWithValue("@id", id);

            var rows = await cmd.ExecuteNonQueryAsync(ct);
            return rows > 0
                ? ResultDto.Ok("Client modifié.")
                : ResultDto.Erreur("Client introuvable.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la modification du client {ClientId}", id);
            return ResultDto.Erreur(ex.Message);
        }
    }

    private static async Task EnsureClientTablesAsync(SqlConnection conn, CancellationToken ct)
    {
        var scripts = new[]
        {
            @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Clients' AND xtype='U')
              CREATE TABLE Clients (Id uniqueidentifier NOT NULL PRIMARY KEY,
                Code nvarchar(20) NOT NULL, RaisonSociale nvarchar(200) NOT NULL,
                TypeClient int NOT NULL DEFAULT 1,
                Email nvarchar(256) NULL, Telephone nvarchar(30) NULL,
                Adresse nvarchar(300) NULL, CodePostal nvarchar(10) NULL,
                Ville nvarchar(100) NULL, Pays nvarchar(100) NOT NULL DEFAULT 'France',
                NumeroTVA nvarchar(30) NULL, Siret nvarchar(20) NULL,
                RepresentantId uniqueidentifier NULL,
                DelaiPaiementJours int NOT NULL DEFAULT 30,
                TauxRemise decimal(5,2) NOT NULL DEFAULT 0,
                PlafondCredit decimal(18,2) NOT NULL DEFAULT 0,
                LimiteDepassement bit NOT NULL DEFAULT 0,
                Notes nvarchar(1000) NULL,
                EstActif bit NOT NULL DEFAULT 1,
                CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
                UpdatedAt datetime2 NULL, CreatedBy nvarchar(450) NOT NULL DEFAULT '')",
            @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='DocumentsVente' AND xtype='U')
              CREATE TABLE DocumentsVente (Id uniqueidentifier NOT NULL PRIMARY KEY,
                Numero nvarchar(30) NOT NULL, TypeDocument int NOT NULL,
                Statut int NOT NULL DEFAULT 1,
                ClientId uniqueidentifier NOT NULL,
                RepresentantId uniqueidentifier NULL,
                DocumentParentId uniqueidentifier NULL,
                DateDocument datetime2 NOT NULL DEFAULT GETUTCDATE(),
                DateEcheance datetime2 NULL,
                DateLivraisonPrevue datetime2 NULL,
                AdresseLivraison nvarchar(500) NULL,
                DepotId uniqueidentifier NULL,
                MontantHT decimal(18,2) NOT NULL DEFAULT 0,
                MontantRemise decimal(18,2) NOT NULL DEFAULT 0,
                MontantTVA decimal(18,2) NOT NULL DEFAULT 0,
                MontantTTC decimal(18,2) NOT NULL DEFAULT 0,
                FraisLivraison decimal(18,2) NOT NULL DEFAULT 0,
                MontantAcompte decimal(18,2) NOT NULL DEFAULT 0,
                MontantRegle decimal(18,2) NOT NULL DEFAULT 0,
                TauxTVA decimal(5,2) NOT NULL DEFAULT 20,
                ConditionsPaiement nvarchar(200) NULL,
                NotesInternes nvarchar(1000) NULL,
                NotesExterne nvarchar(1000) NULL,
                EstVerrouille bit NOT NULL DEFAULT 0,
                CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
                UpdatedAt datetime2 NULL, CreatedBy nvarchar(450) NOT NULL DEFAULT '')"
        };

        foreach (var script in scripts)
        {
            await using var cmd = new SqlCommand(script, conn);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
