using GestionStock.Application.DTOs;
using GestionStock.Application.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace GestionStock.Application.Services;

public class VenteCommercialCommandService : ICommercialVenteCommandService
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
    private readonly ILogger<VenteCommercialCommandService> _logger;

    public VenteCommercialCommandService(
        ICommercialConnectionStringProvider connectionStringProvider,
        ILogger<VenteCommercialCommandService> logger)
    {
        _connectionStringProvider = connectionStringProvider;
        _logger = logger;
    }

    public async Task<(ResultDto Result, Guid? Id, string? Numero)> CreerVenteAsync(
        CommercialVenteRequestDto dto,
        string userId,
        CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqlConnection(_connectionStringProvider.GetCurrentConnectionString());
            await conn.OpenAsync(ct);
            await EnsureCommercialTablesAsync(conn, ct);
            return await CreateVenteCoreAsync(conn, dto, userId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la creation d'un document de vente");
            return (ResultDto.Erreur(ex.Message), null, null);
        }
    }

    public async Task<ResultDto> ModifierVenteAsync(
        Guid id,
        CommercialVenteRequestDto dto,
        CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqlConnection(_connectionStringProvider.GetCurrentConnectionString());
            await conn.OpenAsync(ct);
            await EnsureCommercialTablesAsync(conn, ct);

            int typeDocument;
            int statut;
            bool estVerrouille;
            await using (var chk = new SqlCommand(
                "SELECT TypeDocument, Statut, EstVerrouille FROM DocumentsVente WHERE Id=@id", conn))
            {
                chk.Parameters.AddWithValue("@id", id);
                await using var reader = await chk.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                    return ResultDto.Erreur("Document introuvable.");

                typeDocument = reader.GetInt32(0);
                statut = reader.GetInt32(1);
                estVerrouille = reader.GetBoolean(2);
            }

            if (estVerrouille || NormalizeSimpleStatus(statut) != 1)
                return ResultDto.Erreur("Seuls les documents au statut Saisi sont modifiables.");

            if (typeDocument == 3)
                return ResultDto.Erreur("La modification d'un bon de livraison saisi n'est pas autorisee depuis cet ecran.");

            var (mht, mremise, mtva, mttc) = CalculerTotaux(dto);

            await using (var upd = new SqlCommand(@"
                UPDATE DocumentsVente
                SET ClientId=@client,
                    RepresentantId=@rep,
                    DateDocument=@date,
                    DateEcheance=@ech,
                    DateLivraisonPrevue=@dliv,
                    AdresseLivraison=@adr,
                    DepotId=@depot,
                    MontantHT=@ht,
                    MontantRemise=@rem,
                    MontantTVA=@tva,
                    MontantTTC=@ttc,
                    FraisLivraison=@frliv,
                    MontantAcompte=@acompte,
                    TauxTVA=@tautva,
                    ConditionsPaiement=@cond,
                    NotesInternes=@noti,
                    NotesExterne=@note,
                    UpdatedAt=GETUTCDATE()
                WHERE Id=@id", conn))
            {
                upd.Parameters.AddWithValue("@id", id);
                upd.Parameters.AddWithValue("@client", dto.ClientId);
                upd.Parameters.AddWithValue("@rep", (object?)dto.RepresentantId ?? DBNull.Value);
                upd.Parameters.AddWithValue("@date", dto.DateDocument ?? DateTime.Today);
                upd.Parameters.AddWithValue("@ech", (object?)dto.DateEcheance ?? DBNull.Value);
                upd.Parameters.AddWithValue("@dliv", (object?)dto.DateLivraisonPrevue ?? DBNull.Value);
                upd.Parameters.AddWithValue("@adr", (object?)dto.AdresseLivraison ?? DBNull.Value);
                upd.Parameters.AddWithValue("@depot", (object?)dto.DepotId ?? DBNull.Value);
                upd.Parameters.AddWithValue("@ht", mht);
                upd.Parameters.AddWithValue("@rem", mremise);
                upd.Parameters.AddWithValue("@tva", mtva);
                upd.Parameters.AddWithValue("@ttc", mttc);
                upd.Parameters.AddWithValue("@frliv", dto.FraisLivraison);
                upd.Parameters.AddWithValue("@acompte", dto.MontantAcompte);
                upd.Parameters.AddWithValue("@tautva", dto.TauxTVA);
                upd.Parameters.AddWithValue("@cond", (object?)dto.ConditionsPaiement ?? DBNull.Value);
                upd.Parameters.AddWithValue("@noti", (object?)dto.NotesInternes ?? DBNull.Value);
                upd.Parameters.AddWithValue("@note", (object?)dto.NotesExterne ?? DBNull.Value);
                await upd.ExecuteNonQueryAsync(ct);
            }

            await using (var del = new SqlCommand("DELETE FROM LignesDocumentVente WHERE DocumentId=@id", conn))
            {
                del.Parameters.AddWithValue("@id", id);
                await del.ExecuteNonQueryAsync(ct);
            }

            await InsererLignesAsync(conn, id, dto.Lignes, ct);
            return ResultDto.Ok("Document mis a jour.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la modification du document de vente {DocumentId}", id);
            return ResultDto.Erreur(ex.Message);
        }
    }

    public async Task<ResultDto> SetStatutVenteAsync(Guid id, int statut, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqlConnection(_connectionStringProvider.GetCurrentConnectionString());
            await conn.OpenAsync(ct);
            await EnsureCommercialTablesAsync(conn, ct);

            await using var cmd = new SqlCommand(@"
                UPDATE DocumentsVente
                SET Statut=@s, UpdatedAt=GETUTCDATE()
                WHERE Id=@id
                  AND EstVerrouille=0
                  AND (
                        (@s=2 AND Statut=1)
                     OR (@s=5 AND Statut IN (2,3,4))
                     OR (@s=10 AND Statut IN (1,2,3,4))
                  )", conn);
            cmd.Parameters.AddWithValue("@s", statut);
            cmd.Parameters.AddWithValue("@id", id);

            var rows = await cmd.ExecuteNonQueryAsync(ct);
            return rows > 0
                ? ResultDto.Ok("Statut mis a jour.")
                : ResultDto.Erreur("Document introuvable ou verrouille.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors du changement de statut du document de vente {DocumentId}", id);
            return ResultDto.Erreur(ex.Message);
        }
    }

    public async Task<ResultDto> AnnulerVenteAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqlConnection(_connectionStringProvider.GetCurrentConnectionString());
            await conn.OpenAsync(ct);
            await EnsureCommercialTablesAsync(conn, ct);

            await using (var chk = new SqlCommand("SELECT EstVerrouille FROM DocumentsVente WHERE Id=@id", conn))
            {
                chk.Parameters.AddWithValue("@id", id);
                await using var reader = await chk.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                    return ResultDto.Erreur("Document introuvable.");

                if (reader.GetBoolean(0))
                    return ResultDto.Erreur("Document regle, annulation impossible.");
            }

            return await SetStatutVenteAsync(id, 10, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de l'annulation du document de vente {DocumentId}", id);
            return ResultDto.Erreur(ex.Message);
        }
    }

    public async Task<(ResultDto Result, decimal? Solde, bool? EstRegle)> AjouterReglementAsync(
        Guid id,
        CommercialReglementRequestDto dto,
        string userId,
        CancellationToken ct = default)
    {
        try
        {
            await using var conn = new SqlConnection(_connectionStringProvider.GetCurrentConnectionString());
            await conn.OpenAsync(ct);
            await EnsureCommercialTablesAsync(conn, ct);

            decimal montantTtc;
            decimal montantRegle;

            await using (var chk = new SqlCommand(
                "SELECT MontantTTC, MontantRegle, EstVerrouille FROM DocumentsVente WHERE Id=@id", conn))
            {
                chk.Parameters.AddWithValue("@id", id);
                await using var reader = await chk.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                    return (ResultDto.Erreur("Document introuvable."), null, null);

                if (reader.GetBoolean(2))
                    return (ResultDto.Erreur("Document deja totalement regle."), null, null);

                montantTtc = reader.GetDecimal(0);
                montantRegle = reader.GetDecimal(1);
            }

            var soldeRestant = montantTtc - montantRegle;
            if (dto.Montant > soldeRestant)
                return (ResultDto.Erreur($"Montant superieur au solde restant ({soldeRestant:N2})."), null, null);

            await using (var reglementCmd = new SqlCommand(@"
                INSERT INTO Reglements (Id,DocumentId,TypeDocument,ModeReglement,Montant,DateReglement,Reference,Notes,CreatedAt,CreatedBy)
                VALUES (NEWID(),@doc,1,@mode,@mnt,@date,@ref,@notes,GETUTCDATE(),@user)", conn))
            {
                reglementCmd.Parameters.AddWithValue("@doc", id);
                reglementCmd.Parameters.AddWithValue("@mode", dto.ModeReglement);
                reglementCmd.Parameters.AddWithValue("@mnt", dto.Montant);
                reglementCmd.Parameters.AddWithValue("@date", dto.DateReglement ?? DateTime.Today);
                reglementCmd.Parameters.AddWithValue("@ref", (object?)dto.Reference ?? DBNull.Value);
                reglementCmd.Parameters.AddWithValue("@notes", (object?)dto.Notes ?? DBNull.Value);
                reglementCmd.Parameters.AddWithValue("@user", userId);
                await reglementCmd.ExecuteNonQueryAsync(ct);
            }

            var nouveauMontantRegle = montantRegle + dto.Montant;
            var estRegle = nouveauMontantRegle >= montantTtc;

            await using (var upd = new SqlCommand(@"
                UPDATE DocumentsVente
                SET MontantRegle=@r,
                    EstVerrouille=@v,
                    Statut=CASE WHEN @v=1 THEN 4 ELSE 3 END,
                    UpdatedAt=GETUTCDATE()
                WHERE Id=@id", conn))
            {
                upd.Parameters.AddWithValue("@r", nouveauMontantRegle);
                upd.Parameters.AddWithValue("@v", estRegle);
                upd.Parameters.AddWithValue("@id", id);
                await upd.ExecuteNonQueryAsync(ct);
            }

            return (
                ResultDto.Ok(estRegle
                    ? "Document integralement regle et verrouille."
                    : "Reglement partiel enregistre."),
                montantTtc - nouveauMontantRegle,
                estRegle);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de l'ajout d'un reglement sur le document de vente {DocumentId}", id);
            return (ResultDto.Erreur(ex.Message), null, null);
        }
    }

    public async Task<(ResultDto Result, Guid? Id, string? Numero)> TransformerVenteAsync(
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

            CommercialVenteRequestDto? nouveauDocument = null;

            await using (var cmd = new SqlCommand(@"
                SELECT TypeDocument, ClientId, RepresentantId, AdresseLivraison, DepotId,
                       FraisLivraison, TauxTVA, ConditionsPaiement
                FROM DocumentsVente
                WHERE Id=@id", conn))
            {
                cmd.Parameters.AddWithValue("@id", id);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                    return (ResultDto.Erreur("Document introuvable."), null, null);

                var typeSource = reader.GetInt32(0);
                if (typeDoc <= typeSource)
                    return (ResultDto.Erreur("La transformation doit etre vers un type superieur."), null, null);

                nouveauDocument = new CommercialVenteRequestDto(
                    typeDoc,
                    reader.GetGuid(1),
                    reader.IsDBNull(2) ? null : reader.GetGuid(2),
                    id,
                    null,
                    null,
                    null,
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetGuid(4),
                    reader.GetDecimal(5),
                    0,
                    reader.GetDecimal(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    null,
                    null,
                    Array.Empty<CommercialVenteLigneRequestDto>());
            }

            var lignes = new List<CommercialVenteLigneRequestDto>();
            await using (var lCmd = new SqlCommand(@"
                SELECT ArticleId, Designation, Quantite, PrixUnitaireHT, TauxRemise, TauxTVA, NumeroLot
                FROM LignesDocumentVente
                WHERE DocumentId=@id
                ORDER BY Ordre", conn))
            {
                lCmd.Parameters.AddWithValue("@id", id);
                await using var reader = await lCmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    lignes.Add(new CommercialVenteLigneRequestDto(
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
                "UPDATE DocumentsVente SET Statut=@s, UpdatedAt=GETUTCDATE() WHERE Id=@id", conn))
            {
                upd.Parameters.AddWithValue("@s", nouveauStatut);
                upd.Parameters.AddWithValue("@id", id);
                await upd.ExecuteNonQueryAsync(ct);
            }

            return await CreateVenteCoreAsync(conn, nouveauDocument, userId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la transformation du document de vente {DocumentId}", id);
            return (ResultDto.Erreur(ex.Message), null, null);
        }
    }

    private static (decimal MontantHt, decimal MontantRemise, decimal MontantTva, decimal MontantTtc) CalculerTotaux(CommercialVenteRequestDto dto)
    {
        var montantHt = dto.Lignes.Sum(l => l.Quantite * l.PrixUnitaireHT * (1 - l.TauxRemise / 100));
        var montantRemise = dto.Lignes.Sum(l => l.Quantite * l.PrixUnitaireHT * l.TauxRemise / 100);
        var montantTva = dto.Lignes.Sum(l => l.Quantite * l.PrixUnitaireHT * (1 - l.TauxRemise / 100) * l.TauxTVA / 100);
        var montantTtc = montantHt + montantTva + dto.FraisLivraison - dto.MontantAcompte;
        return (montantHt, montantRemise, montantTva, montantTtc);
    }

    private static async Task<(ResultDto Result, Guid? Id, string? Numero)> CreateVenteCoreAsync(
        SqlConnection conn,
        CommercialVenteRequestDto dto,
        string userId,
        CancellationToken ct)
    {
        var (_, prefixe) = TypesVente.GetValueOrDefault(dto.TypeDocument, ("?", "DOC"));
        var numero = await NextNumeroAsync(conn, $"V{dto.TypeDocument}", prefixe, ct);
        var id = Guid.NewGuid();

        var (mht, mremise, mtva, mttc) = CalculerTotaux(dto);

        await using (var cmd = new SqlCommand(@"
            INSERT INTO DocumentsVente (Id,Numero,TypeDocument,Statut,ClientId,RepresentantId,
                DocumentParentId,DateDocument,DateEcheance,DateLivraisonPrevue,AdresseLivraison,
                DepotId,MontantHT,MontantRemise,MontantTVA,MontantTTC,FraisLivraison,
                MontantAcompte,TauxTVA,ConditionsPaiement,NotesInternes,NotesExterne,CreatedAt,CreatedBy)
            VALUES (@id,@num,@type,1,@client,@rep,@parent,@date,@ech,@dliv,@adr,
                @depot,@ht,@rem,@tva,@ttc,@frliv,@acompte,@tautva,@cond,@noti,@note,GETUTCDATE(),@user)", conn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@num", numero);
            cmd.Parameters.AddWithValue("@type", dto.TypeDocument);
            cmd.Parameters.AddWithValue("@client", dto.ClientId);
            cmd.Parameters.AddWithValue("@rep", (object?)dto.RepresentantId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@parent", (object?)dto.DocumentParentId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@date", dto.DateDocument ?? DateTime.Today);
            cmd.Parameters.AddWithValue("@ech", (object?)dto.DateEcheance ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@dliv", (object?)dto.DateLivraisonPrevue ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@adr", (object?)dto.AdresseLivraison ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@depot", (object?)dto.DepotId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ht", mht);
            cmd.Parameters.AddWithValue("@rem", mremise);
            cmd.Parameters.AddWithValue("@tva", mtva);
            cmd.Parameters.AddWithValue("@ttc", mttc);
            cmd.Parameters.AddWithValue("@frliv", dto.FraisLivraison);
            cmd.Parameters.AddWithValue("@acompte", dto.MontantAcompte);
            cmd.Parameters.AddWithValue("@tautva", dto.TauxTVA);
            cmd.Parameters.AddWithValue("@cond", (object?)dto.ConditionsPaiement ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@noti", (object?)dto.NotesInternes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@note", (object?)dto.NotesExterne ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@user", userId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await InsererLignesAsync(conn, id, dto.Lignes, ct);

        if (dto.TypeDocument == 3 && dto.DepotId.HasValue)
            await ImpacterStockVenteAsync(conn, id, dto.DepotId.Value, false, ct);

        return (ResultDto.Ok($"Document {numero} cree."), id, numero);
    }

    private static async Task InsererLignesAsync(
        SqlConnection conn,
        Guid documentId,
        IReadOnlyList<CommercialVenteLigneRequestDto> lignes,
        CancellationToken ct)
    {
        for (var i = 0; i < lignes.Count; i++)
        {
            var ligne = lignes[i];
            var prixNet = ligne.PrixUnitaireHT * (1 - ligne.TauxRemise / 100);
            var montantRemise = ligne.Quantite * ligne.PrixUnitaireHT * ligne.TauxRemise / 100;
            var montantTva = ligne.Quantite * prixNet * ligne.TauxTVA / 100;
            var montantTtc = ligne.Quantite * prixNet + montantTva;

            await using var cmd = new SqlCommand(@"
                INSERT INTO LignesDocumentVente (Id,DocumentId,ArticleId,Designation,Quantite,QuantiteLivree,
                    PrixUnitaireHT,TauxRemise,MontantRemise,PrixNetHT,TauxTVA,MontantTVA,MontantTTC,NumeroLot,Ordre)
                VALUES (NEWID(),@doc,@art,@des,@qte,0,@pu,@trem,@mrem,@pnet,@ttva,@mtva,@mttc,@lot,@ord)", conn);

            cmd.Parameters.AddWithValue("@doc", documentId);
            cmd.Parameters.AddWithValue("@art", ligne.ArticleId);
            cmd.Parameters.AddWithValue("@des", ligne.Designation);
            cmd.Parameters.AddWithValue("@qte", ligne.Quantite);
            cmd.Parameters.AddWithValue("@pu", ligne.PrixUnitaireHT);
            cmd.Parameters.AddWithValue("@trem", ligne.TauxRemise);
            cmd.Parameters.AddWithValue("@mrem", montantRemise);
            cmd.Parameters.AddWithValue("@pnet", prixNet);
            cmd.Parameters.AddWithValue("@ttva", ligne.TauxTVA);
            cmd.Parameters.AddWithValue("@mtva", montantTva);
            cmd.Parameters.AddWithValue("@mttc", montantTtc);
            cmd.Parameters.AddWithValue("@lot", (object?)ligne.NumeroLot ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ord", i + 1);
            await cmd.ExecuteNonQueryAsync(ct);
        }
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
                Notes nvarchar(500) NULL)",
            @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Reglements' AND xtype='U')
              CREATE TABLE Reglements (Id uniqueidentifier NOT NULL PRIMARY KEY,
                DocumentId uniqueidentifier NULL,
                TypeDocument int NOT NULL DEFAULT 1,
                ModeReglement int NOT NULL DEFAULT 1,
                Montant decimal(18,2) NOT NULL DEFAULT 0,
                DateReglement datetime2 NOT NULL DEFAULT GETUTCDATE(),
                Reference nvarchar(100) NULL,
                Notes nvarchar(1000) NULL,
                ClientId uniqueidentifier NULL,
                FournisseurId uniqueidentifier NULL,
                CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
                CreatedBy nvarchar(450) NOT NULL DEFAULT '')"
        };

        foreach (var script in scripts)
        {
            await using var cmd = new SqlCommand(script, conn);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task ImpacterStockVenteAsync(SqlConnection conn, Guid documentId, Guid depotId, bool annuler, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(@"
            SELECT ArticleId, Quantite, NumeroLot
            FROM LignesDocumentVente
            WHERE DocumentId=@id", conn);
        cmd.Parameters.AddWithValue("@id", documentId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var lignes = new List<(Guid ArticleId, decimal Quantite, string? NumeroLot)>();
        while (await reader.ReadAsync(ct))
        {
            lignes.Add((
                reader.GetGuid(0),
                reader.GetDecimal(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        await reader.CloseAsync();

        foreach (var ligne in lignes)
        {
            var quantite = annuler ? ligne.Quantite : -ligne.Quantite;
            await using var stockCmd = new SqlCommand(@"
                IF EXISTS (SELECT 1 FROM StockArticles WHERE ArticleId=@article AND DepotId=@depot AND (@lot IS NULL OR NumeroLot=@lot))
                    UPDATE StockArticles
                    SET QuantiteDisponible = QuantiteDisponible + @qte
                    WHERE ArticleId=@article AND DepotId=@depot AND (@lot IS NULL OR NumeroLot=@lot)
                ELSE
                    INSERT INTO StockArticles (Id, ArticleId, DepotId, QuantiteDisponible, NumeroLot)
                    VALUES (NEWID(), @article, @depot, @qte, @lot)", conn);

            stockCmd.Parameters.AddWithValue("@article", ligne.ArticleId);
            stockCmd.Parameters.AddWithValue("@depot", depotId);
            stockCmd.Parameters.AddWithValue("@qte", quantite);
            stockCmd.Parameters.AddWithValue("@lot", (object?)ligne.NumeroLot ?? DBNull.Value);
            await stockCmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static int NormalizeSimpleStatus(int rawStatus) => rawStatus switch
    {
        10 => 10,
        9 => 4,
        8 => 3,
        7 => 2,
        > 1 => 2,
        _ => 1
    };
}
