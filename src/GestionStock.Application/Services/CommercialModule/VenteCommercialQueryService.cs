using GestionStock.Application.DTOs;
using GestionStock.Application.Interfaces;
using Microsoft.Data.SqlClient;

namespace GestionStock.Application.Services;

public class VenteCommercialQueryService : ICommercialVenteQueryService
{
    private static readonly Dictionary<int, (string Label, string Prefix)> TypesVente = new()
    {
        { 1, ("Devis", "DEV") },
        { 2, ("Commande", "CMD") },
        { 3, ("Bon de livraison", "BL") },
        { 4, ("Facture", "FAC") },
        { 5, ("Avoir", "AVO") }
    };

    private readonly ICommercialConnectionStringProvider _connectionStringProvider;

    public VenteCommercialQueryService(ICommercialConnectionStringProvider connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    public async Task<IReadOnlyList<CommercialVenteListItemDto>> GetVentesAsync(
        int? type = null,
        int? statut = null,
        Guid? clientId = null,
        string? q = null,
        CancellationToken ct = default)
    {
        var list = new List<CommercialVenteListItemDto>();

        await using var conn = new SqlConnection(_connectionStringProvider.GetCurrentConnectionString());
        await conn.OpenAsync(ct);
        await EnsureCommercialTablesAsync(conn, ct);

        var where = "WHERE 1=1";
        if (type.HasValue) where += " AND d.TypeDocument=@type";
        if (statut.HasValue) where += " AND d.Statut=@statut";
        if (clientId.HasValue) where += " AND d.ClientId=@client";
        if (!string.IsNullOrWhiteSpace(q)) where += " AND (d.Numero LIKE @q OR c.RaisonSociale LIKE @q)";

        await using var cmd = new SqlCommand($@"
            SELECT d.Id, d.Numero, d.TypeDocument, d.Statut, d.DateDocument,
                   d.DateEcheance, c.RaisonSociale, c.Id AS ClientId,
                   d.MontantHT, d.MontantTVA, d.MontantTTC,
                   d.MontantRegle, d.EstVerrouille, d.CreatedAt,
                   d.FraisLivraison, d.MontantAcompte
            FROM DocumentsVente d
            JOIN Clients c ON c.Id=d.ClientId
            {where}
            ORDER BY d.DateDocument DESC", conn);

        if (type.HasValue) cmd.Parameters.AddWithValue("@type", type.Value);
        if (statut.HasValue) cmd.Parameters.AddWithValue("@statut", statut.Value);
        if (clientId.HasValue) cmd.Parameters.AddWithValue("@client", clientId.Value);
        if (!string.IsNullOrWhiteSpace(q)) cmd.Parameters.AddWithValue("@q", $"%{q}%");

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var typeDocument = reader.GetInt32(2);
            var montantTtc = reader.GetDecimal(10);
            var montantRegle = reader.GetDecimal(11);
            var estVerrouille = reader.GetBoolean(12);
            var statutNormalise = NormalizeVenteStatus(typeDocument, reader.GetInt32(3), montantTtc, montantRegle, estVerrouille);

            list.Add(new CommercialVenteListItemDto(
                reader.GetGuid(0),
                reader.GetString(1),
                typeDocument,
                TypesVente.GetValueOrDefault(typeDocument, ("?", "")).Item1,
                statutNormalise,
                GetStatutVente(statutNormalise),
                reader.GetDateTime(4),
                reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                reader.GetString(6),
                reader.GetGuid(7),
                reader.GetDecimal(8),
                reader.GetDecimal(9),
                montantTtc,
                montantRegle,
                montantTtc - montantRegle,
                estVerrouille,
                reader.GetDateTime(13),
                reader.GetDecimal(14),
                reader.GetDecimal(15)));
        }

        return list;
    }

    public async Task<CommercialVenteDetailDto?> GetVenteAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionStringProvider.GetCurrentConnectionString());
        await conn.OpenAsync(ct);
        await EnsureCommercialTablesAsync(conn, ct);

        CommercialVenteDocumentDto? document = null;

        await using (var cmd = new SqlCommand(@"
            SELECT d.*, c.RaisonSociale, c.Email AS ClientEmail, c.Telephone AS ClientTel
            FROM DocumentsVente d
            JOIN Clients c ON c.Id=d.ClientId
            WHERE d.Id=@id", conn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                var typeDocument = reader.GetInt32(reader.GetOrdinal("TypeDocument"));
                var montantTtc = reader.GetDecimal(reader.GetOrdinal("MontantTTC"));
                var montantRegle = reader.GetDecimal(reader.GetOrdinal("MontantRegle"));
                var estVerrouille = reader.GetBoolean(reader.GetOrdinal("EstVerrouille"));
                var statutNormalise = NormalizeVenteStatus(
                    typeDocument,
                    reader.GetInt32(reader.GetOrdinal("Statut")),
                    montantTtc,
                    montantRegle,
                    estVerrouille);

                document = new CommercialVenteDocumentDto(
                    reader.GetGuid(reader.GetOrdinal("Id")),
                    reader.GetString(reader.GetOrdinal("Numero")),
                    typeDocument,
                    TypesVente.GetValueOrDefault(typeDocument, ("?", "")).Item1,
                    statutNormalise,
                    GetStatutVente(statutNormalise),
                    reader.GetGuid(reader.GetOrdinal("ClientId")),
                    reader.GetString(reader.GetOrdinal("RaisonSociale")),
                    reader.GetDateTime(reader.GetOrdinal("DateDocument")),
                    reader.IsDBNull(reader.GetOrdinal("DateEcheance")) ? null : reader.GetDateTime(reader.GetOrdinal("DateEcheance")),
                    reader.IsDBNull(reader.GetOrdinal("AdresseLivraison")) ? null : reader.GetString(reader.GetOrdinal("AdresseLivraison")),
                    reader.GetDecimal(reader.GetOrdinal("MontantHT")),
                    reader.GetDecimal(reader.GetOrdinal("MontantRemise")),
                    reader.GetDecimal(reader.GetOrdinal("MontantTVA")),
                    montantTtc,
                    reader.GetDecimal(reader.GetOrdinal("FraisLivraison")),
                    reader.GetDecimal(reader.GetOrdinal("MontantAcompte")),
                    montantRegle,
                    estVerrouille,
                    reader.IsDBNull(reader.GetOrdinal("NotesInternes")) ? null : reader.GetString(reader.GetOrdinal("NotesInternes")),
                    reader.IsDBNull(reader.GetOrdinal("NotesExterne")) ? null : reader.GetString(reader.GetOrdinal("NotesExterne")),
                    reader.IsDBNull(reader.GetOrdinal("DocumentParentId")) ? null : reader.GetGuid(reader.GetOrdinal("DocumentParentId")));
            }
        }

        if (document is null)
            return null;

        var lignes = new List<CommercialVenteLigneDto>();
        await using (var cmd = new SqlCommand(@"
            SELECT l.*, a.Code AS ArticleCode
            FROM LignesDocumentVente l
            LEFT JOIN Articles a ON a.Id=l.ArticleId
            WHERE l.DocumentId=@id
            ORDER BY l.Ordre", conn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                lignes.Add(new CommercialVenteLigneDto(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetGuid(2),
                    reader.GetString(3),
                    reader.GetDecimal(4),
                    reader.GetDecimal(5),
                    reader.GetDecimal(6),
                    reader.GetDecimal(7),
                    reader.GetDecimal(8),
                    reader.GetDecimal(9),
                    reader.GetDecimal(10),
                    reader.GetDecimal(11),
                    reader.GetDecimal(12),
                    reader.IsDBNull(13) ? null : reader.GetString(13),
                    reader.GetInt32(14),
                    reader.IsDBNull(16) ? null : reader.GetString(16)));
            }
        }

        return new CommercialVenteDetailDto(document, lignes);
    }

    private static async Task EnsureCommercialTablesAsync(SqlConnection conn, CancellationToken ct)
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
                UpdatedAt datetime2 NULL, CreatedBy nvarchar(450) NOT NULL DEFAULT '')",
            @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='LignesDocumentVente' AND xtype='U')
              CREATE TABLE LignesDocumentVente (Id uniqueidentifier NOT NULL PRIMARY KEY,
                DocumentId uniqueidentifier NOT NULL,
                ArticleId uniqueidentifier NOT NULL,
                Designation nvarchar(200) NOT NULL,
                Quantite decimal(18,4) NOT NULL DEFAULT 1,
                QuantiteLivree decimal(18,4) NOT NULL DEFAULT 0,
                PrixUnitaireHT decimal(18,4) NOT NULL DEFAULT 0,
                TauxRemise decimal(5,2) NOT NULL DEFAULT 0,
                MontantRemise decimal(18,2) NOT NULL DEFAULT 0,
                PrixNetHT decimal(18,4) NOT NULL DEFAULT 0,
                TauxTVA decimal(5,2) NOT NULL DEFAULT 20,
                MontantTVA decimal(18,2) NOT NULL DEFAULT 0,
                MontantTTC decimal(18,2) NOT NULL DEFAULT 0,
                NumeroLot nvarchar(50) NULL,
                Ordre int NOT NULL DEFAULT 0,
                Notes nvarchar(500) NULL)"
        };

        foreach (var script in scripts)
        {
            await using var cmd = new SqlCommand(script, conn);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static string GetStatutVente(int statut) => statut switch
    {
        1 => "Saisi",
        2 => "Validé",
        3 => "En cours de règlement",
        4 => "Réglé",
        5 => "Comptabilisé",
        10 => "Annulé",
        _ => "?"
    };

    private static int NormalizeSimpleStatus(int rawStatus) => rawStatus switch
    {
        10 => 10,
        9 => 4,
        8 => 3,
        7 => 2,
        > 1 => 2,
        _ => 1
    };

    private static int NormalizeVenteStatus(int typeDocument, int rawStatus, decimal montantTtc, decimal montantRegle, bool estVerrouille)
    {
        if (rawStatus == 10) return 10;
        if (rawStatus == 5 && typeDocument >= 4) return 5;
        if (estVerrouille || (montantTtc > 0 && montantRegle >= montantTtc)) return 4;
        if (montantRegle > 0) return 3;
        return NormalizeSimpleStatus(rawStatus);
    }
}
