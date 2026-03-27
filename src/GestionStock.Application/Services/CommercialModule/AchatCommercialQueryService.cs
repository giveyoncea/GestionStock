using GestionStock.Application.DTOs;
using GestionStock.Application.Interfaces;
using Microsoft.Data.SqlClient;

namespace GestionStock.Application.Services;

public class AchatCommercialQueryService : ICommercialAchatQueryService
{
    private static readonly Dictionary<int, (string Label, string Prefix)> TypesAchat = new()
    {
        { 1, ("Demande d'achat", "DA") },
        { 2, ("Commande fournisseur", "CF") },
        { 3, ("Bon de reception", "BR") },
        { 4, ("Facture fournisseur", "FF") },
        { 5, ("Avoir fournisseur", "AF") }
    };

    private readonly ICommercialConnectionStringProvider _connectionStringProvider;

    public AchatCommercialQueryService(ICommercialConnectionStringProvider connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    public async Task<IReadOnlyList<CommercialAchatListItemDto>> GetAchatsAsync(
        int? type = null,
        Guid? fournisseurId = null,
        CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionStringProvider.GetCurrentConnectionString());
        await conn.OpenAsync(ct);
        await EnsureCommercialTablesAsync(conn, ct);

        var list = new List<CommercialAchatListItemDto>();
        var where = "WHERE 1=1";
        if (type.HasValue) where += " AND d.TypeDocument=@type";
        if (fournisseurId.HasValue) where += " AND d.FournisseurId=@fid";

        await using var cmd = new SqlCommand($@"
            SELECT d.Id, d.Numero, d.TypeDocument, d.Statut, d.DateDocument,
                   d.DateLivraisonPrevue, f.RaisonSociale, d.MontantTTC,
                   d.MontantRegle, d.EstVerrouille
            FROM DocumentsAchatComm d
            JOIN Fournisseurs f ON f.Id=d.FournisseurId
            {where}
            ORDER BY d.DateDocument DESC", conn);

        if (type.HasValue) cmd.Parameters.AddWithValue("@type", type.Value);
        if (fournisseurId.HasValue) cmd.Parameters.AddWithValue("@fid", fournisseurId.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var typeDocument = reader.GetInt32(2);
            var montantTtc = reader.GetDecimal(7);
            var montantRegle = reader.GetDecimal(8);
            var estVerrouille = reader.GetBoolean(9);
            var statut = NormalizeAchatStatus(typeDocument, reader.GetInt32(3), montantTtc, montantRegle, estVerrouille);

            list.Add(new CommercialAchatListItemDto(
                reader.GetGuid(0),
                reader.GetString(1),
                typeDocument,
                TypesAchat.GetValueOrDefault(typeDocument, ("?", "")).Item1,
                statut,
                GetStatutAchat(statut),
                reader.GetDateTime(4),
                reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                reader.GetString(6),
                montantTtc,
                montantRegle,
                montantTtc - montantRegle,
                estVerrouille));
        }

        return list;
    }

    public async Task<CommercialAchatDetailDto?> GetAchatAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionStringProvider.GetCurrentConnectionString());
        await conn.OpenAsync(ct);
        await EnsureCommercialTablesAsync(conn, ct);

        CommercialAchatDocumentDto? document = null;

        await using (var cmd = new SqlCommand(@"
            SELECT d.*, f.RaisonSociale
            FROM DocumentsAchatComm d
            JOIN Fournisseurs f ON f.Id=d.FournisseurId
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
                var statut = NormalizeAchatStatus(typeDocument, reader.GetInt32(reader.GetOrdinal("Statut")), montantTtc, montantRegle, estVerrouille);

                document = new CommercialAchatDocumentDto(
                    reader.GetGuid(reader.GetOrdinal("Id")),
                    reader.GetString(reader.GetOrdinal("Numero")),
                    typeDocument,
                    TypesAchat.GetValueOrDefault(typeDocument, ("?", "")).Item1,
                    statut,
                    GetStatutAchat(statut),
                    reader.GetGuid(reader.GetOrdinal("FournisseurId")),
                    reader.GetString(reader.GetOrdinal("RaisonSociale")),
                    reader.GetDateTime(reader.GetOrdinal("DateDocument")),
                    reader.IsDBNull(reader.GetOrdinal("DateLivraisonPrevue")) ? null : reader.GetDateTime(reader.GetOrdinal("DateLivraisonPrevue")),
                    reader.GetDecimal(reader.GetOrdinal("MontantHT")),
                    reader.GetDecimal(reader.GetOrdinal("MontantTVA")),
                    montantTtc,
                    reader.GetDecimal(reader.GetOrdinal("FraisLivraison")),
                    montantRegle,
                    estVerrouille,
                    reader.IsDBNull(reader.GetOrdinal("NotesInternes")) ? null : reader.GetString(reader.GetOrdinal("NotesInternes")),
                    reader.IsDBNull(reader.GetOrdinal("DocumentParentId")) ? null : reader.GetGuid(reader.GetOrdinal("DocumentParentId")));
            }
        }

        if (document is null)
            return null;

        var lignes = new List<CommercialAchatLigneDto>();
        await using (var cmd = new SqlCommand(@"
            SELECT l.*, a.Code AS ArticleCode
            FROM LignesDocumentAchat l
            LEFT JOIN Articles a ON a.Id=l.ArticleId
            WHERE l.DocumentId=@id
            ORDER BY l.Ordre", conn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                lignes.Add(new CommercialAchatLigneDto(
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
                    reader.IsDBNull(12) ? null : reader.GetString(12),
                    reader.GetInt32(13),
                    reader.IsDBNull(15) ? null : reader.GetString(15)));
            }
        }

        return new CommercialAchatDetailDto(document, lignes);
    }

    private static async Task EnsureCommercialTablesAsync(SqlConnection conn, CancellationToken ct)
    {
        var scripts = new[]
        {
            @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='DocumentsAchatComm' AND xtype='U')
              CREATE TABLE DocumentsAchatComm (Id uniqueidentifier NOT NULL PRIMARY KEY,
                Numero nvarchar(30) NOT NULL, TypeDocument int NOT NULL,
                Statut int NOT NULL DEFAULT 1,
                FournisseurId uniqueidentifier NOT NULL,
                DocumentParentId uniqueidentifier NULL,
                DateDocument datetime2 NOT NULL DEFAULT GETUTCDATE(),
                DateLivraisonPrevue datetime2 NULL,
                DateReceptionReelle datetime2 NULL,
                DepotId uniqueidentifier NULL,
                MontantHT decimal(18,2) NOT NULL DEFAULT 0,
                MontantRemise decimal(18,2) NOT NULL DEFAULT 0,
                MontantTVA decimal(18,2) NOT NULL DEFAULT 0,
                MontantTTC decimal(18,2) NOT NULL DEFAULT 0,
                FraisLivraison decimal(18,2) NOT NULL DEFAULT 0,
                MontantRegle decimal(18,2) NOT NULL DEFAULT 0,
                NotesInternes nvarchar(1000) NULL,
                EstVerrouille bit NOT NULL DEFAULT 0,
                CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
                UpdatedAt datetime2 NULL, CreatedBy nvarchar(450) NOT NULL DEFAULT '')",
            @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='LignesDocumentAchat' AND xtype='U')
              CREATE TABLE LignesDocumentAchat (Id uniqueidentifier NOT NULL PRIMARY KEY,
                DocumentId uniqueidentifier NOT NULL,
                ArticleId uniqueidentifier NOT NULL,
                Designation nvarchar(200) NOT NULL,
                Quantite decimal(18,4) NOT NULL DEFAULT 1,
                QuantiteRecue decimal(18,4) NOT NULL DEFAULT 0,
                PrixUnitaireHT decimal(18,4) NOT NULL DEFAULT 0,
                TauxRemise decimal(5,2) NOT NULL DEFAULT 0,
                MontantRemise decimal(18,2) NOT NULL DEFAULT 0,
                PrixNetHT decimal(18,4) NOT NULL DEFAULT 0,
                TauxTVA decimal(5,2) NOT NULL DEFAULT 20,
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

    private static string GetStatutAchat(int s) => s switch
    {
        1 => "Saisi",
        2 => "Valide",
        3 => "En cours de reglement",
        4 => "Regle",
        5 => "Comptabilise",
        10 => "Annule",
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

    private static int NormalizeAchatStatus(int typeDocument, int rawStatus, decimal montantTtc, decimal montantRegle, bool estVerrouille)
    {
        if (rawStatus == 10 || rawStatus == 9) return 10;
        if (rawStatus == 5 && typeDocument >= 4) return 5;
        if (estVerrouille || (montantTtc > 0 && montantRegle >= montantTtc)) return 4;
        if (montantRegle > 0) return 3;
        return NormalizeSimpleStatus(rawStatus);
    }
}
