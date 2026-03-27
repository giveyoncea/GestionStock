using GestionStock.API.Services;
using GestionStock.Application.DTOs;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Security.Claims;

namespace GestionStock.API.Controllers;

[ApiController]
[Route("api/commandes")]
[Authorize]
[Tags("Commandes d'Achat")]
public class CommandesAdoController : ControllerBase
{
    private readonly ITenantService _tenant;
    private readonly IConfiguration _config;
    private readonly IValidator<CreerCommandeAchatDto> _validator;

    private string ConnStr
    {
        get
        {
            var t = User.FindFirstValue("tenant");
            return !string.IsNullOrEmpty(t)
                ? _tenant.GetConnectionString(t)
                : _config.GetConnectionString("DefaultConnection")!;
        }
    }
    private string UserId => User.FindFirstValue("sub") ?? "system";

    public CommandesAdoController(ITenantService tenant, IConfiguration config,
        IValidator<CreerCommandeAchatDto> validator)
    {
        _tenant    = tenant;
        _config    = config;
        _validator = validator;
    }

    private static string GetStatutDocument(int statut) => statut switch
    {
        1 => "Saisi",
        2 => "Validé",
        3 => "En cours de règlement",
        4 => "Réglé",
        5 => "Comptabilisé",
        9 => "Annulé",
        _ => "Inconnu"
    };

    private async Task<string> GetStockTableAsync(SqlConnection conn)
    {
        await using var cmd = new SqlCommand(
            "SELECT CASE WHEN OBJECT_ID('Stocks') IS NOT NULL THEN 'Stocks' ELSE 'StockArticles' END", conn);
        return (string)(await cmd.ExecuteScalarAsync() ?? "StockArticles");
    }

    [HttpGet]
    [Authorize(Policy = "Acheteur")]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();

            await using var countCmd = new SqlCommand(
                "SELECT COUNT(1) FROM CommandesAchat", conn);
            var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

            await using var cmd = new SqlCommand(@"
                SELECT c.Id, c.Numero, c.FournisseurId,
                       ISNULL(f.RaisonSociale,'') AS FournisseurNom,
                       c.DateCommande, c.DateLivraisonPrevue, c.DateLivraisonReelle,
                       c.Statut, ISNULL(c.MontantHT,0), ISNULL(c.TVA,20),
                       ISNULL(c.MontantTTC,0), ISNULL(c.Commentaire,'')
                FROM CommandesAchat c
                LEFT JOIN Fournisseurs f ON f.Id=c.FournisseurId
                ORDER BY c.DateCommande DESC
                OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY", conn);
            cmd.Parameters.AddWithValue("@skip", (page-1)*pageSize);
            cmd.Parameters.AddWithValue("@take", pageSize);

            var items = new List<object>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var statut = r.GetInt32(7);
                items.Add(new
                {
                    id                  = r.GetGuid(0),
                    numero              = r.GetString(1),
                    fournisseurId       = r.GetGuid(2),
                    fournisseurNom      = r.GetString(3),
                    dateCommande        = r.GetDateTime(4),
                    dateLivraisonPrevue = r.GetDateTime(5),
                    dateLivraisonReelle = r.IsDBNull(6) ? (DateTime?)null : r.GetDateTime(6),
                    statut,
                    statutLibelle       = GetStatutDocument(statut),
                    montantHT           = r.GetDecimal(8),
                    tva                 = r.GetDecimal(9),
                    montantTTC          = r.GetDecimal(10),
                    commentaire         = r.GetString(11),
                    lignes              = new List<object>()
                });
            }
            return Ok(new { items, totalCount = total, page, pageSize,
                totalPages = (int)Math.Ceiling(total / (double)pageSize) });
        }
        catch (Exception ex) { return StatusCode(500, new { succes = false, message = ex.Message }); }
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "Acheteur")]
    public async Task<IActionResult> GetById(Guid id)
    {
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();

            await using var cmdHead = new SqlCommand(@"
                SELECT c.Id, c.Numero, c.FournisseurId,
                       ISNULL(f.RaisonSociale,''), c.DateCommande, c.DateLivraisonPrevue,
                       c.DateLivraisonReelle, c.Statut, ISNULL(c.MontantHT,0),
                       ISNULL(c.TVA,20), ISNULL(c.MontantTTC,0), ISNULL(c.Commentaire,'')
                FROM CommandesAchat c
                LEFT JOIN Fournisseurs f ON f.Id=c.FournisseurId
                WHERE c.Id=@id", conn);
            cmdHead.Parameters.AddWithValue("@id", id);
            await using var rh = await cmdHead.ExecuteReaderAsync();
            if (!await rh.ReadAsync()) return NotFound();

            var statut = rh.GetInt32(7);
            var head = new
            {
                id                  = rh.GetGuid(0),
                numero              = rh.GetString(1),
                fournisseurId       = rh.GetGuid(2),
                fournisseurNom      = rh.GetString(3),
                dateCommande        = rh.GetDateTime(4),
                dateLivraisonPrevue = rh.GetDateTime(5),
                dateLivraisonReelle = rh.IsDBNull(6) ? (DateTime?)null : rh.GetDateTime(6),
                statut,
                statutLibelle       = GetStatutDocument(statut),
                montantHT           = rh.GetDecimal(8),
                tva                 = rh.GetDecimal(9),
                montantTTC          = rh.GetDecimal(10),
                commentaire         = rh.GetString(11)
            };
            await rh.CloseAsync();

            // Lignes
            await using var cmdLines = new SqlCommand(@"
                SELECT l.Id, l.ArticleId, ISNULL(a.Code,'') AS ArticleCode,
                       ISNULL(l.Designation,a.Designation) AS Designation,
                       l.QuantiteCommandee, ISNULL(l.QuantiteRecue,0),
                       ISNULL(l.PrixUnitaire,0), ISNULL(l.Unite,'PCS'),
                       ISNULL(l.QuantiteCommandee*l.PrixUnitaire,0) AS MontantLigne
                FROM LignesCommandeAchat l
                LEFT JOIN Articles a ON a.Id=l.ArticleId
                WHERE l.CommandeId=@id", conn);
            cmdLines.Parameters.AddWithValue("@id", id);
            var lignes = new List<object>();
            await using var rl = await cmdLines.ExecuteReaderAsync();
            while (await rl.ReadAsync())
            {
                lignes.Add(new
                {
                    id               = rl.GetGuid(0),
                    articleId        = rl.GetGuid(1),
                    articleCode      = rl.GetString(2),
                    designation      = rl.GetString(3),
                    quantiteCommandee = rl.GetInt32(4),
                    quantiteRecue    = rl.GetInt32(5),
                    prixUnitaire     = rl.GetDecimal(6),
                    unite            = rl.GetString(7),
                    montantLigne     = rl.GetDecimal(8)
                });
            }
            return Ok(new { head.id, head.numero, head.fournisseurId, head.fournisseurNom,
                head.dateCommande, head.dateLivraisonPrevue, head.dateLivraisonReelle,
                head.statut, head.statutLibelle, head.montantHT, head.tva, head.montantTTC,
                head.commentaire, lignes });
        }
        catch (Exception ex) { return StatusCode(500, new { succes = false, message = ex.Message }); }
    }

    [HttpGet("en-attente")]
    [Authorize(Policy = "Acheteur")]
    public async Task<IActionResult> GetEnAttente()
    {
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(@"
                SELECT c.Id, c.Numero, ISNULL(f.RaisonSociale,'') AS FournisseurNom,
                       c.DateLivraisonPrevue, c.Statut,
                       ISNULL(c.MontantTTC,0) AS MontantTTC
                FROM CommandesAchat c
                LEFT JOIN Fournisseurs f ON f.Id=c.FournisseurId
                WHERE c.Statut IN (1,2,3)
                ORDER BY c.DateLivraisonPrevue ASC", conn);
            var items = new List<object>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                items.Add(new
                {
                    id = r.GetGuid(0), numero = r.GetString(1),
                    fournisseurNom = r.GetString(2),
                    dateLivraisonPrevue = r.GetDateTime(3),
                    statut = r.GetInt32(4), montantTTC = r.GetDecimal(5)
                });
            }
            return Ok(items);
        }
        catch (Exception ex) { return StatusCode(500, new { succes = false, message = ex.Message }); }
    }

    [HttpPost]
    [Authorize(Policy = "Acheteur")]
    public async Task<IActionResult> Creer([FromBody] CreerCommandeAchatDto dto)
    {
        var v = await _validator.ValidateAsync(dto);
        if (!v.IsValid) return BadRequest(v.Errors.Select(e => e.ErrorMessage));
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();

            var id    = Guid.NewGuid();
            var annee = DateTime.UtcNow.Year;
            // Générer numéro de commande
            await using var numCmd = new SqlCommand(
                $"SELECT ISNULL(MAX(CAST(RIGHT(Numero,4) AS int)),0)+1 FROM CommandesAchat WHERE Numero LIKE 'CA{annee}-%'", conn);
            var seq = Convert.ToInt32(await numCmd.ExecuteScalarAsync());
            var numero = $"CA{annee}-{seq:D4}";

            var montantHT = dto.Lignes.Sum(l => l.Quantite * l.PrixUnitaire);
            var tva       = 20m;
            var montantTTC = montantHT * (1 + tva / 100);

            await using var cmdHead = new SqlCommand(@"
                INSERT INTO CommandesAchat
                    (Id,Numero,FournisseurId,DateCommande,DateLivraisonPrevue,
                     Statut,MontantHT,TVA,MontantTTC,Commentaire,CreatedAt,CreatedBy)
                VALUES
                    (@id,@num,@fid,GETUTCDATE(),@dlp,
                     1,@mht,@tva,@mttc,@com,GETUTCDATE(),@user)", conn);
            cmdHead.Parameters.AddWithValue("@id",   id);
            cmdHead.Parameters.AddWithValue("@num",  numero);
            cmdHead.Parameters.AddWithValue("@fid",  dto.FournisseurId);
            cmdHead.Parameters.AddWithValue("@dlp",  dto.DateLivraisonPrevue);
            cmdHead.Parameters.AddWithValue("@mht",  montantHT);
            cmdHead.Parameters.AddWithValue("@tva",  tva);
            cmdHead.Parameters.AddWithValue("@mttc", montantTTC);
            cmdHead.Parameters.AddWithValue("@com",  (object?)dto.Commentaire ?? DBNull.Value);
            cmdHead.Parameters.AddWithValue("@user", UserId);
            await cmdHead.ExecuteNonQueryAsync();

            foreach (var l in dto.Lignes)
            {
                await using var cmdLigne = new SqlCommand(@"
                    INSERT INTO LignesCommandeAchat
                        (Id,CommandeId,ArticleId,Designation,QuantiteCommandee,
                         QuantiteRecue,PrixUnitaire,Unite)
                    SELECT NEWID(),@cid,@artId,a.Designation,@qte,0,@prix,ISNULL(a.Unite,'PCS')
                    FROM Articles a WHERE a.Id=@artId", conn);
                cmdLigne.Parameters.AddWithValue("@cid",   id);
                cmdLigne.Parameters.AddWithValue("@artId", l.ArticleId);
                cmdLigne.Parameters.AddWithValue("@qte",   l.Quantite);
                cmdLigne.Parameters.AddWithValue("@prix",  l.PrixUnitaire);
                await cmdLigne.ExecuteNonQueryAsync();
            }
            return CreatedAtAction(nameof(GetById), new { id },
                new { succes = true, data = id, numero });
        }
        catch (Exception ex) { return StatusCode(500, new { succes = false, message = ex.Message }); }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "Acheteur")]
    public async Task<IActionResult> Modifier(Guid id, [FromBody] CreerCommandeAchatDto dto)
    {
        var v = await _validator.ValidateAsync(dto);
        if (!v.IsValid) return BadRequest(v.Errors.Select(e => e.ErrorMessage));
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();

            await using (var chk = new SqlCommand("SELECT Statut FROM CommandesAchat WHERE Id=@id", conn))
            {
                chk.Parameters.AddWithValue("@id", id);
                var current = await chk.ExecuteScalarAsync();
                if (current == null) return NotFound();
                if (Convert.ToInt32(current) != 1)
                    return BadRequest(new { succes = false, message = "Seules les commandes au statut Saisi sont modifiables." });
            }

            var montantHT = dto.Lignes.Sum(l => l.Quantite * l.PrixUnitaire);
            var tva = 20m;
            var montantTTC = montantHT * (1 + tva / 100);

            await using (var cmdHead = new SqlCommand(@"
                UPDATE CommandesAchat
                SET FournisseurId=@fid,
                    DateLivraisonPrevue=@dlp,
                    MontantHT=@mht,
                    TVA=@tva,
                    MontantTTC=@mttc,
                    Commentaire=@com,
                    UpdatedAt=GETUTCDATE(),
                    UpdatedBy=@user
                WHERE Id=@id", conn))
            {
                cmdHead.Parameters.AddWithValue("@id", id);
                cmdHead.Parameters.AddWithValue("@fid", dto.FournisseurId);
                cmdHead.Parameters.AddWithValue("@dlp", dto.DateLivraisonPrevue);
                cmdHead.Parameters.AddWithValue("@mht", montantHT);
                cmdHead.Parameters.AddWithValue("@tva", tva);
                cmdHead.Parameters.AddWithValue("@mttc", montantTTC);
                cmdHead.Parameters.AddWithValue("@com", (object?)dto.Commentaire ?? DBNull.Value);
                cmdHead.Parameters.AddWithValue("@user", UserId);
                await cmdHead.ExecuteNonQueryAsync();
            }

            await using (var del = new SqlCommand("DELETE FROM LignesCommandeAchat WHERE CommandeId=@id", conn))
            {
                del.Parameters.AddWithValue("@id", id);
                await del.ExecuteNonQueryAsync();
            }

            foreach (var l in dto.Lignes)
            {
                await using var cmdLigne = new SqlCommand(@"
                    INSERT INTO LignesCommandeAchat
                        (Id,CommandeId,ArticleId,Designation,QuantiteCommandee,
                         QuantiteRecue,PrixUnitaire,Unite)
                    SELECT NEWID(),@cid,@artId,a.Designation,@qte,0,@prix,ISNULL(a.Unite,'PCS')
                    FROM Articles a WHERE a.Id=@artId", conn);
                cmdLigne.Parameters.AddWithValue("@cid", id);
                cmdLigne.Parameters.AddWithValue("@artId", l.ArticleId);
                cmdLigne.Parameters.AddWithValue("@qte", l.Quantite);
                cmdLigne.Parameters.AddWithValue("@prix", l.PrixUnitaire);
                await cmdLigne.ExecuteNonQueryAsync();
            }

            return Ok(new { succes = true, message = "Commande mise a jour." });
        }
        catch (Exception ex) { return StatusCode(500, new { succes = false, message = ex.Message }); }
    }

    [HttpPost("{id:guid}/valider")]
    [Authorize(Policy = "Acheteur")]
    public async Task<IActionResult> Valider(Guid id)
    {
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "UPDATE CommandesAchat SET Statut=2, UpdatedAt=GETUTCDATE(), UpdatedBy=@user WHERE Id=@id AND Statut=1", conn);
            cmd.Parameters.AddWithValue("@id",   id);
            cmd.Parameters.AddWithValue("@user", UserId);
            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0
                ? Ok(new { succes = true, message = "Commande validée." })
                : BadRequest(new { succes = false, message = "Commande introuvable ou déjà validée." });
        }
        catch (Exception ex) { return StatusCode(500, new { succes = false, message = ex.Message }); }
    }

    [HttpPost("{id:guid}/comptabiliser")]
    [Authorize(Policy = "Acheteur")]
    public async Task<IActionResult> Comptabiliser(Guid id)
    {
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "UPDATE CommandesAchat SET Statut=5, UpdatedAt=GETUTCDATE(), UpdatedBy=@user WHERE Id=@id AND Statut IN (2,3,4)", conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@user", UserId);
            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0
                ? Ok(new { succes = true, message = "Commande comptabilisée." })
                : BadRequest(new { succes = false, message = "Commande introuvable ou non comptabilisable." });
        }
        catch (Exception ex) { return StatusCode(500, new { succes = false, message = ex.Message }); }
    }

    [HttpPost("reception")]
    [Authorize(Policy = "Magasinier")]
    public async Task<IActionResult> Receptionner([FromBody] ReceptionCommandeDto dto)
    {
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            var stockTable = await GetStockTableAsync(conn);

            var colDate = "CreatedAt";
            await using var colCmd = new SqlCommand(
                "SELECT CASE WHEN EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('MouvementsStock') AND name='DateMouvement') THEN 'DateMouvement' ELSE 'CreatedAt' END", conn);
            colDate = (string)(await colCmd.ExecuteScalarAsync() ?? "CreatedAt");

            var colPrix = "PrixUnitaire";
            await using var colCmd2 = new SqlCommand(
                "SELECT CASE WHEN EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('MouvementsStock') AND name='ValeurUnitaire') THEN 'ValeurUnitaire' ELSE 'PrixUnitaire' END", conn);
            colPrix = (string)(await colCmd2.ExecuteScalarAsync() ?? "PrixUnitaire");

            var totalRecues = 0;
            var totalCommandees = 0;

            foreach (var ligne in dto.Lignes)
            {
                // Lire ligne commande
                await using var rdCmd = new SqlCommand(
                    "SELECT ArticleId, QuantiteCommandee, QuantiteRecue, ISNULL(PrixUnitaire,0) FROM LignesCommandeAchat WHERE Id=@id", conn);
                rdCmd.Parameters.AddWithValue("@id", ligne.LigneId);
                await using var rdR = await rdCmd.ExecuteReaderAsync();
                if (!await rdR.ReadAsync()) continue;
                var articleId    = rdR.GetGuid(0);
                var qteCmd       = rdR.GetInt32(1);
                var qteRecueAvant = rdR.GetInt32(2);
                var prixUnit     = rdR.GetDecimal(3);
                await rdR.CloseAsync();

                totalCommandees += qteCmd;
                totalRecues     += qteRecueAvant + ligne.QuantiteRecue;

                // Mettre à jour ligne
                await using var updLigne = new SqlCommand(
                    "UPDATE LignesCommandeAchat SET QuantiteRecue=QuantiteRecue+@q WHERE Id=@id", conn);
                updLigne.Parameters.AddWithValue("@q",  ligne.QuantiteRecue);
                updLigne.Parameters.AddWithValue("@id", ligne.LigneId);
                await updLigne.ExecuteNonQueryAsync();

                // Upsert stock
                await using var upsert = new SqlCommand($@"
                    IF EXISTS (SELECT 1 FROM {stockTable} WHERE ArticleId=@artId AND EmplacementId=@empId)
                        UPDATE {stockTable} SET QuantiteDisponible=QuantiteDisponible+@q WHERE ArticleId=@artId AND EmplacementId=@empId
                    ELSE
                        INSERT INTO {stockTable} (Id,ArticleId,EmplacementId,QuantiteDisponible,QuantiteReservee)
                        VALUES (NEWID(),@artId,@empId,@q,0)", conn);
                upsert.Parameters.AddWithValue("@artId", articleId);
                upsert.Parameters.AddWithValue("@empId", dto.EmplacementReceptionId);
                upsert.Parameters.AddWithValue("@q",     ligne.QuantiteRecue);
                await upsert.ExecuteNonQueryAsync();

                // Mouvement
                await using var mvt = new SqlCommand($@"
                    INSERT INTO MouvementsStock
                        (Id,ArticleId,EmplacementSourceId,TypeMouvement,Quantite,
                         {colPrix},{colDate},Reference,NumeroLot,CreatedBy)
                    VALUES
                        (NEWID(),@artId,@empId,1,@q,@prix,GETUTCDATE(),@ref,@lot,@user)", conn);
                mvt.Parameters.AddWithValue("@artId", articleId);
                mvt.Parameters.AddWithValue("@empId", dto.EmplacementReceptionId);
                mvt.Parameters.AddWithValue("@q",     ligne.QuantiteRecue);
                mvt.Parameters.AddWithValue("@prix",  prixUnit);
                mvt.Parameters.AddWithValue("@ref",   $"Réception commande {dto.CommandeId}");
                mvt.Parameters.AddWithValue("@lot",   (object?)dto.NumeroLot ?? DBNull.Value);
                mvt.Parameters.AddWithValue("@user",  UserId);
                await mvt.ExecuteNonQueryAsync();
            }

            // Statut commande
            var nouveauStatut = totalRecues >= totalCommandees ? 4 : 2;
            await using var updCmd = new SqlCommand(
                "UPDATE CommandesAchat SET Statut=@s, DateLivraisonReelle=CASE WHEN @s=4 THEN GETUTCDATE() ELSE DateLivraisonReelle END, UpdatedAt=GETUTCDATE(), UpdatedBy=@user WHERE Id=@id", conn);
            updCmd.Parameters.AddWithValue("@s",    nouveauStatut);
            updCmd.Parameters.AddWithValue("@id",   dto.CommandeId);
            updCmd.Parameters.AddWithValue("@user", UserId);
            await updCmd.ExecuteNonQueryAsync();

            return Ok(new { succes = true, message = nouveauStatut == 4
                ? "Commande réglée." : "Commande validée." });
        }
        catch (Exception ex) { return StatusCode(500, new { succes = false, message = ex.Message }); }
    }

    [HttpPost("{id:guid}/annuler")]
    [Authorize(Policy = "Acheteur")]
    public async Task<IActionResult> Annuler(Guid id, [FromBody] AnnulationRequest req)
    {
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(@"
                UPDATE CommandesAchat
                SET Statut=9, Commentaire=ISNULL(Commentaire,'')+' | Annulé: '+@motif,
                    UpdatedAt=GETUTCDATE(), UpdatedBy=@user
                WHERE Id=@id AND Statut NOT IN (5,9)", conn);
            cmd.Parameters.AddWithValue("@id",    id);
            cmd.Parameters.AddWithValue("@motif", req.Motif ?? "");
            cmd.Parameters.AddWithValue("@user",  UserId);
            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0
                ? Ok(new { succes = true, message = "Commande annulée." })
                : BadRequest(new { succes = false, message = "Commande introuvable ou ne peut pas être annulée." });
        }
        catch (Exception ex) { return StatusCode(500, new { succes = false, message = ex.Message }); }
    }

    public record AnnulationRequest(string? Motif);
}
