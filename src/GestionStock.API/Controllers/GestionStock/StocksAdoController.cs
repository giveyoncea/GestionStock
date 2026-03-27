using GestionStock.API.Services;
using GestionStock.Application.DTOs;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Security.Claims;

namespace GestionStock.API.Controllers;

/// <summary>
/// Stocks et mouvements multi-tenant via ADO.NET.
/// Remplace StocksController (EF Core) pour supporter le routage tenant.
/// Gère les deux schémas : Stocks (GestionStockDB) et StockArticles (GestionStock_GOUN).
/// </summary>
[ApiController]
[Route("api/stocks")]
[Authorize]
[Tags("Gestion des Stocks")]
public class StocksAdoController : ControllerBase
{
    private readonly ITenantService _tenant;
    private readonly IConfiguration _config;
    private readonly IValidator<EntreeStockDto>   _entreeValidator;
    private readonly IValidator<SortieStockDto>   _sortieValidator;

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

    public StocksAdoController(
        ITenantService tenant, IConfiguration config,
        IValidator<EntreeStockDto> entreeValidator,
        IValidator<SortieStockDto> sortieValidator)
    {
        _tenant           = tenant;
        _config           = config;
        _entreeValidator  = entreeValidator;
        _sortieValidator  = sortieValidator;
    }

    // ─── HELPERS ──────────────────────────────────────────────────────────────
    private async Task<(string stockTable, string colDate, string colPrix)> GetSchemaAsync(SqlConnection conn)
    {
        await using var cmd = new SqlCommand(@"
            SELECT
                CASE WHEN OBJECT_ID('Stocks') IS NOT NULL THEN 'Stocks' ELSE 'StockArticles' END,
                CASE WHEN EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('MouvementsStock') AND name='DateMouvement')
                     THEN 'DateMouvement' ELSE 'CreatedAt' END,
                CASE WHEN EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('MouvementsStock') AND name='ValeurUnitaire')
                     THEN 'ValeurUnitaire' ELSE 'PrixUnitaire' END", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        return (r.GetString(0), r.GetString(1), r.GetString(2));
    }

    // ─── GET /api/stocks — Résumé des stocks ──────────────────────────────────
    [HttpGet]
    [Authorize(Policy = "Lecteur")]
    public async Task<IActionResult> GetResume()
    {
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            var (stockTable, _, _) = await GetSchemaAsync(conn);

            await using var cmd = new SqlCommand($@"
                SELECT
                    a.Id, a.Code, a.Designation,
                    ISNULL(a.Unite,'PCS') AS Unite,
                    ISNULL(a.PrixAchat,0) AS PrixAchat,
                    ISNULL(a.SeuilAlerte,0) AS SeuilAlerte,
                    ISNULL(s.QteTotal,0) AS QuantiteTotale,
                    ISNULL(s.QteRes,0)   AS QuantiteReservee
                FROM Articles a
                LEFT JOIN (
                    SELECT ArticleId,
                           SUM(QuantiteDisponible) AS QteTotal,
                           SUM(QuantiteReservee)   AS QteRes
                    FROM {stockTable}
                    GROUP BY ArticleId
                ) s ON s.ArticleId = a.Id
                WHERE a.Statut = 1
                ORDER BY a.Code", conn);

            var items = new List<object>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var qte   = r.GetInt32(6);
                var seuil = r.GetInt32(5);
                items.Add(new
                {
                    articleId          = r.GetGuid(0),
                    articleCode        = r.GetString(1),
                    articleDesignation = r.GetString(2),
                    unite              = r.GetString(3),
                    prixAchat          = r.GetDecimal(4),
                    seuilAlerte        = seuil,
                    quantiteTotale     = qte,
                    quantiteReservee   = r.GetInt32(7),
                    estEnAlerte        = qte <= seuil && seuil > 0,
                    estEnRupture       = qte == 0,
                    valeurTotale       = qte * r.GetDecimal(4)
                });
            }
            return Ok(items);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { succes = false, message = ex.Message });
        }
    }

    // ─── GET /api/stocks/article/{id} — Détail par article ────────────────────
    [HttpGet("article/{articleId:guid}")]
    [Authorize(Policy = "Lecteur")]
    public async Task<IActionResult> GetDetailsByArticle(Guid articleId)
    {
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            var (stockTable, _, _) = await GetSchemaAsync(conn);

            await using var cmd = new SqlCommand($@"
                SELECT
                    s.Id, s.ArticleId, s.EmplacementId,
                    ISNULL(e.Code,'') AS EmplacementCode,
                    ISNULL(e.Libelle,'') AS EmplacementLibelle,
                    s.QuantiteDisponible, s.QuantiteReservee,
                    ISNULL(s.NumeroLot,'') AS NumeroLot
                FROM {stockTable} s
                LEFT JOIN Emplacements e ON e.Id = s.EmplacementId
                WHERE s.ArticleId = @id
                ORDER BY e.Code", conn);
            cmd.Parameters.AddWithValue("@id", articleId);

            var items = new List<object>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                items.Add(new
                {
                    id                  = r.GetGuid(0),
                    articleId           = r.GetGuid(1),
                    emplacementId       = r.GetGuid(2),
                    emplacementCode     = r.GetString(3),
                    emplacementLibelle  = r.GetString(4),
                    quantiteDisponible  = r.GetInt32(5),
                    quantiteReservee    = r.GetInt32(6),
                    numeroLot           = r.GetString(7)
                });
            }
            return Ok(items);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { succes = false, message = ex.Message });
        }
    }

    // ─── GET /api/stocks/mouvements — Historique ───────────────────────────────
    [HttpGet("mouvements")]
    [Authorize(Policy = "Lecteur")]
    public async Task<IActionResult> GetHistorique(
        [FromQuery] Guid? articleId,
        [FromQuery] DateTime? du,
        [FromQuery] DateTime? au)
    {
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            var (_, colDate, colPrix) = await GetSchemaAsync(conn);

            var where = $"WHERE m.{colDate} >= @du AND m.{colDate} <= @au";
            if (articleId.HasValue) where += " AND m.ArticleId = @artId";

            await using var cmd = new SqlCommand($@"
                SELECT
                    m.Id,
                    ISNULL(a.Code,'')        AS ArticleCode,
                    ISNULL(a.Designation,'') AS ArticleDesignation,
                    ISNULL(es.Code,'')       AS EmplacementSource,
                    ISNULL(ed.Code,'')       AS EmplacementDest,
                    m.TypeMouvement,
                    m.Quantite,
                    ISNULL(m.{colPrix},0)    AS ValeurUnitaire,
                    ISNULL(m.Quantite * m.{colPrix},0) AS ValeurTotale,
                    ISNULL(m.Reference,'')   AS Reference,
                    ISNULL(m.Motif,'')       AS Motif,
                    ISNULL(m.NumeroLot,'')   AS NumeroLot,
                    m.{colDate}              AS DateMouvement,
                    m.CreatedBy
                FROM MouvementsStock m
                LEFT JOIN Articles    a  ON a.Id  = m.ArticleId
                LEFT JOIN Emplacements es ON es.Id = m.EmplacementSourceId
                LEFT JOIN Emplacements ed ON ed.Id = m.EmplacementDestinationId
                {where}
                ORDER BY m.{colDate} DESC", conn);

            cmd.Parameters.AddWithValue("@du", du ?? DateTime.UtcNow.AddDays(-30));
            cmd.Parameters.AddWithValue("@au", au ?? DateTime.UtcNow.AddDays(1));
            if (articleId.HasValue)
                cmd.Parameters.AddWithValue("@artId", articleId.Value);

            var items = new List<object>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var typeMvt = r.GetInt32(5);
                items.Add(new
                {
                    id                   = r.GetGuid(0),
                    articleCode          = r.GetString(1),
                    articleDesignation   = r.GetString(2),
                    emplacementSource    = r.GetString(3),
                    emplacementDestination = r.GetString(4),
                    typeMouvement        = typeMvt,
                    typeMouvementLibelle = typeMvt switch
                    {
                        1 => "Entree", 2 => "Sortie", 3 => "Transfert",
                        4 => "Ajustement", _ => "Autre"
                    },
                    quantite             = r.GetDecimal(6),
                    valeurUnitaire       = r.GetDecimal(7),
                    valeurTotale         = r.GetDecimal(8),
                    reference            = r.GetString(9),
                    motif                = r.GetString(10),
                    numeroLot            = r.GetString(11),
                    dateMouvement        = r.GetDateTime(12),
                    createdBy            = r.GetString(13)
                });
            }
            return Ok(items);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { succes = false, message = ex.Message });
        }
    }

    // ─── POST /api/stocks/entree ───────────────────────────────────────────────
    [HttpPost("entree")]
    [Authorize(Policy = "Magasinier")]
    public async Task<IActionResult> Entree([FromBody] EntreeStockDto dto)
    {
        var validation = await _entreeValidator.ValidateAsync(dto);
        if (!validation.IsValid)
            return BadRequest(validation.Errors.Select(e => e.ErrorMessage));

        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            var (stockTable, colDate, colPrix) = await GetSchemaAsync(conn);

            // Upsert stock
            await using var upsertCmd = new SqlCommand($@"
                IF EXISTS (SELECT 1 FROM {stockTable} WHERE ArticleId=@artId AND EmplacementId=@empId)
                    UPDATE {stockTable}
                    SET QuantiteDisponible = QuantiteDisponible + @qte
                    WHERE ArticleId=@artId AND EmplacementId=@empId
                ELSE
                    INSERT INTO {stockTable} (Id,ArticleId,EmplacementId,QuantiteDisponible,QuantiteReservee)
                    VALUES (NEWID(),@artId,@empId,@qte,0)", conn);
            upsertCmd.Parameters.AddWithValue("@artId", dto.ArticleId);
            upsertCmd.Parameters.AddWithValue("@empId", dto.EmplacementId);
            upsertCmd.Parameters.AddWithValue("@qte",   dto.Quantite);
            await upsertCmd.ExecuteNonQueryAsync();

            // Mouvement
            await using var mvtCmd = new SqlCommand($@"
                INSERT INTO MouvementsStock
                    (Id,ArticleId,EmplacementSourceId,TypeMouvement,Quantite,
                     {colPrix},{colDate},Reference,Motif,NumeroLot,CreatedBy)
                VALUES
                    (NEWID(),@artId,@empId,1,@qte,@prix,GETUTCDATE(),@ref,@motif,@lot,@user)", conn);
            mvtCmd.Parameters.AddWithValue("@artId", dto.ArticleId);
            mvtCmd.Parameters.AddWithValue("@empId", dto.EmplacementId);
            mvtCmd.Parameters.AddWithValue("@qte",   dto.Quantite);
            mvtCmd.Parameters.AddWithValue("@prix",  dto.PrixUnitaire);
            mvtCmd.Parameters.AddWithValue("@ref",   (object?)dto.Reference  ?? DBNull.Value);
            mvtCmd.Parameters.AddWithValue("@motif", (object?)dto.Motif      ?? DBNull.Value);
            mvtCmd.Parameters.AddWithValue("@lot",   (object?)dto.NumeroLot  ?? DBNull.Value);
            mvtCmd.Parameters.AddWithValue("@user",  UserId);
            await mvtCmd.ExecuteNonQueryAsync();

            return Ok(new { succes = true, message = "Entrée enregistrée." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { succes = false, message = ex.Message });
        }
    }

    // ─── POST /api/stocks/sortie ───────────────────────────────────────────────
    [HttpPost("sortie")]
    [Authorize(Policy = "Magasinier")]
    public async Task<IActionResult> Sortie([FromBody] SortieStockDto dto)
    {
        var validation = await _sortieValidator.ValidateAsync(dto);
        if (!validation.IsValid)
            return BadRequest(validation.Errors.Select(e => e.ErrorMessage));

        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            var (stockTable, colDate, colPrix) = await GetSchemaAsync(conn);

            // Vérifier stock suffisant
            await using var chkCmd = new SqlCommand(
                $"SELECT ISNULL(SUM(QuantiteDisponible),0) FROM {stockTable} WHERE ArticleId=@artId AND EmplacementId=@empId", conn);
            chkCmd.Parameters.AddWithValue("@artId", dto.ArticleId);
            chkCmd.Parameters.AddWithValue("@empId", dto.EmplacementId);
            var qteActuelle = Convert.ToInt32(await chkCmd.ExecuteScalarAsync());
            if (qteActuelle < dto.Quantite)
                return BadRequest(new { succes = false, message = $"Stock insuffisant : {qteActuelle} disponible(s)." });

            // Décrémenter
            await using var updCmd = new SqlCommand(
                $"UPDATE {stockTable} SET QuantiteDisponible = QuantiteDisponible - @qte WHERE ArticleId=@artId AND EmplacementId=@empId", conn);
            updCmd.Parameters.AddWithValue("@artId", dto.ArticleId);
            updCmd.Parameters.AddWithValue("@empId", dto.EmplacementId);
            updCmd.Parameters.AddWithValue("@qte",   dto.Quantite);
            await updCmd.ExecuteNonQueryAsync();

            // Mouvement
            // Lire prix unitaire depuis Articles
            await using var prixCmd = new SqlCommand(
                "SELECT ISNULL(PrixAchat,0) FROM Articles WHERE Id=@id", conn);
            prixCmd.Parameters.AddWithValue("@id", dto.ArticleId);
            var prix = Convert.ToDecimal(await prixCmd.ExecuteScalarAsync() ?? 0m);

            await using var mvtCmd = new SqlCommand($@"
                INSERT INTO MouvementsStock
                    (Id,ArticleId,EmplacementSourceId,TypeMouvement,Quantite,
                     {colPrix},{colDate},Reference,Motif,NumeroLot,CreatedBy)
                VALUES
                    (NEWID(),@artId,@empId,2,@qte,@prix,GETUTCDATE(),@ref,@motif,@lot,@user)", conn);
            mvtCmd.Parameters.AddWithValue("@artId", dto.ArticleId);
            mvtCmd.Parameters.AddWithValue("@empId", dto.EmplacementId);
            mvtCmd.Parameters.AddWithValue("@qte",   dto.Quantite);
            mvtCmd.Parameters.AddWithValue("@prix",  prix);
            mvtCmd.Parameters.AddWithValue("@ref",   (object?)dto.Reference ?? DBNull.Value);
            mvtCmd.Parameters.AddWithValue("@motif", (object?)dto.Motif     ?? DBNull.Value);
            mvtCmd.Parameters.AddWithValue("@lot",   (object?)dto.NumeroLot ?? DBNull.Value);
            mvtCmd.Parameters.AddWithValue("@user",  UserId);
            await mvtCmd.ExecuteNonQueryAsync();

            return Ok(new { succes = true, message = "Sortie enregistrée." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { succes = false, message = ex.Message });
        }
    }

    // ─── POST /api/stocks/transfert ───────────────────────────────────────────
    [HttpPost("transfert")]
    [Authorize(Policy = "Magasinier")]
    public async Task<IActionResult> Transfert([FromBody] TransfertStockDto dto)
    {
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            var (stockTable, colDate, colPrix) = await GetSchemaAsync(conn);

            // Décrémenter source
            await using var decCmd = new SqlCommand(
                $"UPDATE {stockTable} SET QuantiteDisponible = QuantiteDisponible - @qte WHERE ArticleId=@artId AND EmplacementId=@src", conn);
            decCmd.Parameters.AddWithValue("@artId", dto.ArticleId);
            decCmd.Parameters.AddWithValue("@src",   dto.EmplacementSourceId);
            decCmd.Parameters.AddWithValue("@qte",   dto.Quantite);
            await decCmd.ExecuteNonQueryAsync();

            // Incrémenter destination
            await using var incCmd = new SqlCommand($@"
                IF EXISTS (SELECT 1 FROM {stockTable} WHERE ArticleId=@artId AND EmplacementId=@dst)
                    UPDATE {stockTable} SET QuantiteDisponible = QuantiteDisponible + @qte WHERE ArticleId=@artId AND EmplacementId=@dst
                ELSE
                    INSERT INTO {stockTable} (Id,ArticleId,EmplacementId,QuantiteDisponible,QuantiteReservee)
                    VALUES (NEWID(),@artId,@dst,@qte,0)", conn);
            incCmd.Parameters.AddWithValue("@artId", dto.ArticleId);
            incCmd.Parameters.AddWithValue("@dst",   dto.EmplacementDestinationId);
            incCmd.Parameters.AddWithValue("@qte",   dto.Quantite);
            await incCmd.ExecuteNonQueryAsync();

            // Mouvement
            await using var mvtCmd = new SqlCommand($@"
                INSERT INTO MouvementsStock
                    (Id,ArticleId,EmplacementSourceId,EmplacementDestinationId,
                     TypeMouvement,Quantite,{colDate},Reference,Motif,CreatedBy)
                VALUES
                    (NEWID(),@artId,@src,@dst,3,@qte,GETUTCDATE(),@ref,@motif,@user)", conn);
            mvtCmd.Parameters.AddWithValue("@artId", dto.ArticleId);
            mvtCmd.Parameters.AddWithValue("@src",   dto.EmplacementSourceId);
            mvtCmd.Parameters.AddWithValue("@dst",   dto.EmplacementDestinationId);
            mvtCmd.Parameters.AddWithValue("@qte",   dto.Quantite);
            mvtCmd.Parameters.AddWithValue("@ref",   DBNull.Value);
            mvtCmd.Parameters.AddWithValue("@motif", DBNull.Value);
            mvtCmd.Parameters.AddWithValue("@user",  UserId);
            await mvtCmd.ExecuteNonQueryAsync();

            return Ok(new { succes = true, message = "Transfert effectué." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { succes = false, message = ex.Message });
        }
    }

    // ─── POST /api/stocks/ajustement ──────────────────────────────────────────
    [HttpPost("ajustement")]
    [Authorize(Policy = "Magasinier")]
    public async Task<IActionResult> Ajustement([FromBody] AjustementStockDto dto)
    {
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            var (stockTable, colDate, _) = await GetSchemaAsync(conn);

            // Lire quantité actuelle
            await using var chkCmd = new SqlCommand(
                $"SELECT ISNULL(SUM(QuantiteDisponible),0) FROM {stockTable} WHERE ArticleId=@artId AND EmplacementId=@empId", conn);
            chkCmd.Parameters.AddWithValue("@artId", dto.ArticleId);
            chkCmd.Parameters.AddWithValue("@empId", dto.EmplacementId);
            var qteActuelle = Convert.ToInt32(await chkCmd.ExecuteScalarAsync());
            var ecart = dto.QuantiteReelle - qteActuelle;

            // Upsert stock
            await using var updCmd = new SqlCommand($@"
                IF EXISTS (SELECT 1 FROM {stockTable} WHERE ArticleId=@artId AND EmplacementId=@empId)
                    UPDATE {stockTable} SET QuantiteDisponible=@qte WHERE ArticleId=@artId AND EmplacementId=@empId
                ELSE
                    INSERT INTO {stockTable} (Id,ArticleId,EmplacementId,QuantiteDisponible,QuantiteReservee)
                    VALUES (NEWID(),@artId,@empId,@qte,0)", conn);
            updCmd.Parameters.AddWithValue("@artId", dto.ArticleId);
            updCmd.Parameters.AddWithValue("@empId", dto.EmplacementId);
            updCmd.Parameters.AddWithValue("@qte",   dto.QuantiteReelle);
            await updCmd.ExecuteNonQueryAsync();

            // Mouvement d'ajustement
            await using var mvtCmd = new SqlCommand($@"
                INSERT INTO MouvementsStock
                    (Id,ArticleId,EmplacementSourceId,TypeMouvement,Quantite,{colDate},Motif,CreatedBy)
                VALUES
                    (NEWID(),@artId,@empId,4,@ecart,GETUTCDATE(),@motif,@user)", conn);
            mvtCmd.Parameters.AddWithValue("@artId", dto.ArticleId);
            mvtCmd.Parameters.AddWithValue("@empId", dto.EmplacementId);
            mvtCmd.Parameters.AddWithValue("@ecart", ecart);
            mvtCmd.Parameters.AddWithValue("@motif", (object?)dto.Motif ?? DBNull.Value);
            mvtCmd.Parameters.AddWithValue("@user",  UserId);
            await mvtCmd.ExecuteNonQueryAsync();

            return Ok(new { succes = true, message = $"Ajustement : {ecart:+#;-#;0} unité(s)." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { succes = false, message = ex.Message });
        }
    }
}
