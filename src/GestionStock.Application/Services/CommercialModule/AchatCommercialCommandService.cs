using GestionStock.Application.DTOs;
using GestionStock.Application.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace GestionStock.Application.Services;

public class AchatCommercialCommandService : ICommercialAchatCommandService
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
    private readonly ILogger<AchatCommercialCommandService> _logger;

    public AchatCommercialCommandService(
        ICommercialConnectionStringProvider connectionStringProvider,
        ILogger<AchatCommercialCommandService> logger)
    {
        _connectionStringProvider = connectionStringProvider;
        _logger = logger;
    }

    public async Task<(ResultDto Result, Guid? Id, string? Numero)> CreerAchatAsync(
        CommercialAchatRequestDto dto,
        string userId,
        CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqlConnection(_connectionStringProvider.GetCurrentConnectionString());
            await conn.OpenAsync(ct);
            await EnsureCommercialTablesAsync(conn, ct);
            return await CreateAchatCoreAsync(conn, dto, userId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la creation d'un document d'achat");
            return (ResultDto.Erreur(ex.Message), null, null);
        }
    }

    public async Task<(ResultDto Result, Guid? Id, string? Numero)> TransformerAchatAsync(
        Guid id,
        int typeDoc,
        string userId,
        CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqlConnection(_connectionStringProvider.GetCurrentConnectionString());
            await conn.OpenAsync(ct);
            await EnsureCommercialTablesAsync(conn, ct);

            CommercialAchatRequestDto? nouveauDocument = null;

            await using (var cmd = new SqlCommand(@"
                SELECT TypeDocument, FournisseurId, DateLivraisonPrevue, DepotId, FraisLivraison, NotesInternes
                FROM DocumentsAchatComm
                WHERE Id=@id", conn))
            {
                cmd.Parameters.AddWithValue("@id", id);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                    return (ResultDto.Erreur("Document introuvable."), null, null);

                var typeSource = reader.GetInt32(0);
                if (typeDoc <= typeSource)
                    return (ResultDto.Erreur("La transformation doit etre vers un type superieur."), null, null);

                nouveauDocument = new CommercialAchatRequestDto(
                    typeDoc,
                    reader.GetGuid(1),
                    id,
                    null,
                    reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                    reader.IsDBNull(3) ? null : reader.GetGuid(3),
                    reader.GetDecimal(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    Array.Empty<CommercialAchatLigneRequestDto>());
            }

            var lignes = new List<CommercialAchatLigneRequestDto>();
            await using (var lCmd = new SqlCommand(@"
                SELECT ArticleId, Designation, Quantite, PrixUnitaireHT, TauxRemise, TauxTVA, NumeroLot
                FROM LignesDocumentAchat
                WHERE DocumentId=@id
                ORDER BY Ordre", conn))
            {
                lCmd.Parameters.AddWithValue("@id", id);
                await using var reader = await lCmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    lignes.Add(new CommercialAchatLigneRequestDto(
                        reader.GetGuid(0),
                        reader.GetString(1),
                        reader.GetDecimal(2),
                        reader.GetDecimal(3),
                        reader.GetDecimal(4),
                        reader.GetDecimal(5),
                        reader.IsDBNull(6) ? null : reader.GetString(6)));
                }
            }

            nouveauDocument = nouveauDocument! with { Lignes = lignes };

            var nouveauStatut = typeDoc == 3 ? 5 : typeDoc == 4 ? 7 : typeDoc;
            await using (var upd = new SqlCommand(
                "UPDATE DocumentsAchatComm SET Statut=@s, UpdatedAt=GETUTCDATE() WHERE Id=@id", conn))
            {
                upd.Parameters.AddWithValue("@s", nouveauStatut);
                upd.Parameters.AddWithValue("@id", id);
                await upd.ExecuteNonQueryAsync(ct);
            }

            return await CreateAchatCoreAsync(conn, nouveauDocument, userId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la transformation du document d'achat {DocumentId}", id);
            return (ResultDto.Erreur(ex.Message), null, null);
        }
    }

    private static async Task<(ResultDto Result, Guid? Id, string? Numero)> CreateAchatCoreAsync(
        SqlConnection conn,
        CommercialAchatRequestDto dto,
        string userId,
        CancellationToken ct)
    {
        var (_, prefixe) = TypesAchat.GetValueOrDefault(dto.TypeDocument, ("?", "DOC"));
        var numero = await NextNumeroAsync(conn, $"A{dto.TypeDocument}", prefixe, ct);
        var id = Guid.NewGuid();

        var montantHt = dto.Lignes.Sum(l => l.Quantite * l.PrixUnitaireHT * (1 - l.TauxRemise / 100));
        var montantTva = dto.Lignes.Sum(l => l.Quantite * l.PrixUnitaireHT * (1 - l.TauxRemise / 100) * l.TauxTVA / 100);
        var montantTtc = montantHt + montantTva + dto.FraisLivraison;

        await using (var cmd = new SqlCommand(@"
            INSERT INTO DocumentsAchatComm (Id,Numero,TypeDocument,Statut,FournisseurId,DocumentParentId,
                DateDocument,DateLivraisonPrevue,DepotId,MontantHT,MontantRemise,MontantTVA,MontantTTC,
                FraisLivraison,NotesInternes,CreatedAt,CreatedBy)
            VALUES (@id,@num,@type,1,@four,@parent,@date,@dliv,@depot,@ht,0,@tva,@ttc,@frliv,@notes,GETUTCDATE(),@user)", conn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@num", numero);
            cmd.Parameters.AddWithValue("@type", dto.TypeDocument);
            cmd.Parameters.AddWithValue("@four", dto.FournisseurId);
            cmd.Parameters.AddWithValue("@parent", (object?)dto.DocumentParentId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@date", dto.DateDocument ?? DateTime.Today);
            cmd.Parameters.AddWithValue("@dliv", (object?)dto.DateLivraisonPrevue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@depot", (object?)dto.DepotId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ht", montantHt);
            cmd.Parameters.AddWithValue("@tva", montantTva);
            cmd.Parameters.AddWithValue("@ttc", montantTtc);
            cmd.Parameters.AddWithValue("@frliv", dto.FraisLivraison);
            cmd.Parameters.AddWithValue("@notes", (object?)dto.NotesInternes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@user", userId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        for (var i = 0; i < dto.Lignes.Count; i++)
        {
            var ligne = dto.Lignes[i];
            var prixNet = ligne.PrixUnitaireHT * (1 - ligne.TauxRemise / 100);
            var montantRemise = ligne.Quantite * ligne.PrixUnitaireHT * ligne.TauxRemise / 100;
            var montantTtcLigne = ligne.Quantite * prixNet * (1 + ligne.TauxTVA / 100);

            await using var cmd = new SqlCommand(@"
                INSERT INTO LignesDocumentAchat (Id,DocumentId,ArticleId,Designation,Quantite,QuantiteRecue,
                    PrixUnitaireHT,TauxRemise,MontantRemise,PrixNetHT,TauxTVA,MontantTTC,NumeroLot,Ordre)
                VALUES (NEWID(),@doc,@art,@des,@qte,0,@pu,@trem,@mrem,@pnet,@ttva,@mttc,@lot,@ord)", conn);
            cmd.Parameters.AddWithValue("@doc", id);
            cmd.Parameters.AddWithValue("@art", ligne.ArticleId);
            cmd.Parameters.AddWithValue("@des", ligne.Designation);
            cmd.Parameters.AddWithValue("@qte", ligne.Quantite);
            cmd.Parameters.AddWithValue("@pu", ligne.PrixUnitaireHT);
            cmd.Parameters.AddWithValue("@trem", ligne.TauxRemise);
            cmd.Parameters.AddWithValue("@mrem", montantRemise);
            cmd.Parameters.AddWithValue("@pnet", prixNet);
            cmd.Parameters.AddWithValue("@ttva", ligne.TauxTVA);
            cmd.Parameters.AddWithValue("@mttc", montantTtcLigne);
            cmd.Parameters.AddWithValue("@lot", (object?)ligne.NumeroLot ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ord", i + 1);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        if (dto.TypeDocument == 3 && dto.DepotId.HasValue)
            await ImpacterStockAchatAsync(conn, id, dto.DepotId.Value, ct);

        return (ResultDto.Ok($"Document {numero} cree."), id, numero);
    }

    private static async Task<string> NextNumeroAsync(SqlConnection conn, string type, string prefixe, CancellationToken ct)
    {
        var year = DateTime.Now.Year;
        await using var cmd = new SqlCommand(@"
            MERGE NumeroAutoComm AS t
            USING (VALUES (@type, @pre, @yr)) AS s(TypeDocument,Prefixe,Annee)
            ON t.TypeDocument = s.TypeDocument
            WHEN MATCHED AND t.Annee = @yr THEN UPDATE SET Compteur = Compteur+1
            WHEN MATCHED AND t.Annee != @yr THEN UPDATE SET Annee=@yr, Compteur=1
            WHEN NOT MATCHED THEN INSERT (TypeDocument,Prefixe,Annee,Compteur) VALUES (@type,@pre,@yr,1);
            SELECT Compteur FROM NumeroAutoComm WHERE TypeDocument=@type", conn);

        cmd.Parameters.AddWithValue("@type", type);
        cmd.Parameters.AddWithValue("@pre", prefixe);
        cmd.Parameters.AddWithValue("@yr", year);

        var num = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct) ?? 1);
        return $"{prefixe}-{year}-{num:D6}";
    }

    private static async Task EnsureCommercialTablesAsync(SqlConnection conn, CancellationToken ct)
    {
        var scripts = new[]
        {
            @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='NumeroAutoComm' AND xtype='U')
              CREATE TABLE NumeroAutoComm (TypeDocument nvarchar(20) NOT NULL PRIMARY KEY,
                Prefixe nvarchar(10) NOT NULL,
                Annee int NOT NULL,
                Compteur int NOT NULL DEFAULT 0)",
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

    private static async Task ImpacterStockAchatAsync(SqlConnection conn, Guid docId, Guid depotId, CancellationToken ct)
    {
        Guid? emplacementId = null;
        await using (var emplacementCmd = new SqlCommand(
            "SELECT TOP 1 Id FROM Emplacements WHERE EstActif=1 ORDER BY Code", conn))
        {
            var value = await emplacementCmd.ExecuteScalarAsync(ct);
            if (value != null)
                emplacementId = (Guid)value;
        }

        if (!emplacementId.HasValue)
            return;

        await using var lignesCmd = new SqlCommand(
            "SELECT ArticleId,Quantite,PrixUnitaireHT,NumeroLot FROM LignesDocumentAchat WHERE DocumentId=@id", conn);
        lignesCmd.Parameters.AddWithValue("@id", docId);

        var lignes = new List<(Guid ArticleId, decimal Quantite, decimal PrixUnitaire, string? NumeroLot)>();
        await using (var reader = await lignesCmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                lignes.Add((
                    reader.GetGuid(0),
                    reader.GetDecimal(1),
                    reader.GetDecimal(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3)));
            }
        }

        foreach (var ligne in lignes)
        {
            await using (var mouvementCmd = new SqlCommand(@"
                INSERT INTO MouvementsStock (Id,ArticleId,EmplacementSourceId,TypeMouvement,
                    Quantite,PrixUnitaire,Reference,NumeroLot,CreatedAt,CreatedBy)
                VALUES (NEWID(),@art,@empl,1,@qte,@pu,@ref,@lot,GETUTCDATE(),@user)", conn))
            {
                mouvementCmd.Parameters.AddWithValue("@art", ligne.ArticleId);
                mouvementCmd.Parameters.AddWithValue("@empl", emplacementId.Value);
                mouvementCmd.Parameters.AddWithValue("@qte", (int)ligne.Quantite);
                mouvementCmd.Parameters.AddWithValue("@pu", ligne.PrixUnitaire);
                mouvementCmd.Parameters.AddWithValue("@ref", $"BR-{docId.ToString()[..8]}");
                mouvementCmd.Parameters.AddWithValue("@lot", (object?)ligne.NumeroLot ?? DBNull.Value);
                mouvementCmd.Parameters.AddWithValue("@user", "commercial");
                await mouvementCmd.ExecuteNonQueryAsync(ct);
            }

            await using (var upsertCmd = new SqlCommand(@"
                MERGE StockArticles AS t
                USING (VALUES (@art,@empl,@qte,@pu)) AS s(ArticleId,EmplacementId,Qte,Pu)
                ON t.ArticleId=s.ArticleId AND t.EmplacementId=s.EmplacementId
                WHEN MATCHED THEN UPDATE SET QuantiteDisponible=t.QuantiteDisponible+s.Qte,
                    PrixUnitaireMoyen=(t.PrixUnitaireMoyen*t.QuantiteDisponible+s.Qte*s.Pu)/(t.QuantiteDisponible+s.Qte),
                    UpdatedAt=GETUTCDATE()
                WHEN NOT MATCHED THEN INSERT (Id,ArticleId,EmplacementId,QuantiteDisponible,PrixUnitaireMoyen,CreatedAt,CreatedBy)
                    VALUES (NEWID(),s.ArticleId,s.EmplacementId,s.Qte,s.Pu,GETUTCDATE(),'commercial');", conn))
            {
                upsertCmd.Parameters.AddWithValue("@art", ligne.ArticleId);
                upsertCmd.Parameters.AddWithValue("@empl", emplacementId.Value);
                upsertCmd.Parameters.AddWithValue("@qte", (int)ligne.Quantite);
                upsertCmd.Parameters.AddWithValue("@pu", ligne.PrixUnitaire);
                await upsertCmd.ExecuteNonQueryAsync(ct);
            }
        }
    }
}
