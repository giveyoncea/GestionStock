using GestionStock.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Security.Claims;

namespace GestionStock.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TracabiliteController : ControllerBase
{
    private readonly ITenantService _tenant;
    private readonly IConfiguration _config;

    private string ConnStr
    {
        get
        {
            var tenantCode = User.FindFirstValue("tenant");
            if (!string.IsNullOrEmpty(tenantCode))
                return _tenant.GetConnectionString(tenantCode);
            return _config.GetConnectionString("DefaultConnection")!;
        }
    }

    public TracabiliteController(ITenantService tenant, IConfiguration config)
    {
        _tenant = tenant;
        _config = config;
    }

    // ─── Fiche traçabilité complète d'un article ──────────────────────────────
    [HttpGet("articles/{articleId:guid}")]
    public async Task<IActionResult> GetArticleTrace(Guid articleId,
        [FromQuery] DateTime? du = null, [FromQuery] DateTime? au = null)
    {
        var debut = du ?? DateTime.UtcNow.AddDays(-90);
        var fin   = au?.AddDays(1) ?? DateTime.UtcNow.AddDays(1);

        var result = new
        {
            Article    = await GetArticleInfo(articleId),
            Lots       = await GetLotsArticle(articleId),
            Mouvements = await GetMouvementsArticle(articleId, debut, fin),
            Resume     = await GetResumeArticle(articleId)
        };
        return Ok(result);
    }

    // ─── Lots d'un article ────────────────────────────────────────────────────
    [HttpGet("lots")]
    public async Task<IActionResult> GetLots(
        [FromQuery] Guid? articleId = null,
        [FromQuery] string? statut = null,
        [FromQuery] bool alertePeremption = false)
    {
        var list = new List<object>();
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();

        var where = new List<string> { "1=1" };
        if (articleId.HasValue) where.Add("l.ArticleId = @articleId");
        if (!string.IsNullOrEmpty(statut) && int.TryParse(statut, out var s)) where.Add($"l.Statut = {s}");
        if (alertePeremption) where.Add("l.DatePeremption IS NOT NULL AND l.DatePeremption <= DATEADD(day, 30, GETUTCDATE())");

        var sql = $@"
            SELECT l.Id, l.NumeroLot, l.ArticleId,
                   a.Code AS ArticleCode, a.Designation AS ArticleDesignation,
                   l.DateReception, l.DateFabrication, l.DatePeremption,
                   l.QuantiteInitiale, l.QuantiteRestante, l.Statut,
                   l.NumeroSerie, l.CertifiqueQualite,
                   CASE WHEN l.DatePeremption < GETUTCDATE() THEN 1 ELSE 0 END AS EstPerime,
                   CASE WHEN l.DatePeremption < DATEADD(day,30,GETUTCDATE()) AND l.DatePeremption IS NOT NULL AND l.DatePeremption >= GETUTCDATE() THEN 1 ELSE 0 END AS EnAlertePeremption,
                   l.CreatedBy, l.CreatedAt
            FROM Lots l
            JOIN Articles a ON a.Id = l.ArticleId
            WHERE {string.Join(" AND ", where)}
            ORDER BY l.DateReception DESC";

        await using var cmd = new SqlCommand(sql, conn);
        if (articleId.HasValue) cmd.Parameters.AddWithValue("@articleId", articleId.Value);

        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new {
                Id = r.GetGuid(0), NumeroLot = r.GetString(1), ArticleId = r.GetGuid(2),
                ArticleCode = r.GetString(3), ArticleDesignation = r.GetString(4),
                DateReception = r.GetDateTime(5),
                DateFabrication = r.GetDateTime(6),
                DatePeremption = r.IsDBNull(7) ? (DateTime?)null : r.GetDateTime(7),
                QuantiteInitiale = r.GetInt32(8), QuantiteRestante = r.GetInt32(9),
                Statut = r.GetInt32(10), StatutLibelle = GetStatutLot(r.GetInt32(10)),
                NumeroSerie = r.IsDBNull(11) ? null : r.GetString(11),
                CertifiqueQualite = r.IsDBNull(12) ? null : r.GetString(12),
                EstPerime = r.GetInt32(13) == 1,
                EnAlertePeremption = r.GetInt32(14) == 1,
                CreatedBy = r.IsDBNull(15) ? null : r.GetString(15),
                CreatedAt = r.GetDateTime(16)
            });

        return Ok(list);
    }

    // ─── Créer/enregistrer un lot ─────────────────────────────────────────────
    [HttpPost("lots")]
    public async Task<IActionResult> CreerLot([FromBody] LotRequest dto)
    {
        if (dto.ArticleId == Guid.Empty || string.IsNullOrWhiteSpace(dto.NumeroLot))
            return BadRequest(new { succes = false, message = "ArticleId et NumeroLot obligatoires." });

        var userId = User.FindFirstValue("sub") ?? "system";
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();

        // Unicité
        await using (var chk = new SqlCommand(
            "SELECT COUNT(1) FROM Lots WHERE ArticleId=@a AND NumeroLot=@n", conn))
        {
            chk.Parameters.AddWithValue("@a", dto.ArticleId);
            chk.Parameters.AddWithValue("@n", dto.NumeroLot.Trim());
            if (Convert.ToInt32(await chk.ExecuteScalarAsync() ?? 0) > 0)
                return BadRequest(new { succes = false, message = "Ce numéro de lot existe déjà pour cet article." });
        }

        var id = Guid.NewGuid();
        await using var cmd = new SqlCommand(@"
            INSERT INTO Lots (Id, NumeroLot, ArticleId, DateReception, DateFabrication,
                DatePeremption, QuantiteInitiale, QuantiteRestante, Statut,
                NumeroSerie, CertifiqueQualite, CreatedAt, CreatedBy)
            VALUES (@id, @num, @article, @reception, @fabrication,
                @peremption, @qte, @qte, 1,
                @serie, @certif, GETUTCDATE(), @user)", conn);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@num", dto.NumeroLot.Trim());
        cmd.Parameters.AddWithValue("@article", dto.ArticleId);
        cmd.Parameters.AddWithValue("@reception", dto.DateReception);
        cmd.Parameters.AddWithValue("@fabrication", dto.DateFabrication ?? dto.DateReception);
        cmd.Parameters.AddWithValue("@peremption", (object?)dto.DatePeremption ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@qte", dto.Quantite);
        cmd.Parameters.AddWithValue("@serie", (object?)dto.NumeroSerie ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@certif", (object?)dto.CertifiqueQualite ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@user", userId);
        await cmd.ExecuteNonQueryAsync();

        return Ok(new { succes = true, message = "Lot créé.", id });
    }

    // ─── Modifier statut d'un lot ─────────────────────────────────────────────
    [HttpPut("lots/{lotId:guid}/statut")]
    public async Task<IActionResult> ModifierStatutLot(Guid lotId, [FromBody] StatutLotRequest dto)
    {
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(
            "UPDATE Lots SET Statut=@s, UpdatedAt=GETUTCDATE() WHERE Id=@id", conn);
        cmd.Parameters.AddWithValue("@s", dto.Statut);
        cmd.Parameters.AddWithValue("@id", lotId);
        int rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0
            ? Ok(new { succes = true, message = "Statut mis à jour." })
            : NotFound(new { succes = false, message = "Lot introuvable." });
    }

    // ─── Mouvements d'un lot spécifique ──────────────────────────────────────
    [HttpGet("lots/{lotId:guid}/mouvements")]
    public async Task<IActionResult> GetMouvementsLot(Guid lotId)
    {
        var list = new List<object>();
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(@"
            SELECT ms.Id, ms.DateMouvement, ms.TypeMouvement, ms.Quantite,
                   ms.ValeurUnitaire, ms.ValeurUnitaire * ms.Quantite AS ValeurTotale,
                   ms.Reference, ms.Motif,
                   a.Code AS ArticleCode, a.Designation,
                   e1.Code AS EmplSource, e2.Code AS EmplDest,
                   ISNULL(ms.NumeroSerie, l.NumeroSerie) AS NumeroSerie,
                   ms.CreatedBy
            FROM MouvementsStock ms
            JOIN Articles a ON a.Id = ms.ArticleId
            LEFT JOIN Lots l ON l.Id = ms.LotId
            JOIN Emplacements e1 ON e1.Id = ms.EmplacementSourceId
            LEFT JOIN Emplacements e2 ON e2.Id = ms.EmplacementDestinationId
            WHERE ms.LotId = @lotId
            ORDER BY ms.DateMouvement DESC", conn);
        cmd.Parameters.AddWithValue("@lotId", lotId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new {
                Id = r.GetGuid(0), DateMouvement = r.GetDateTime(1),
                TypeMouvement = r.GetInt32(2), TypeLibelle = GetTypeMvt(r.GetInt32(2)),
                Quantite = r.GetInt32(3), ValeurUnitaire = r.GetDecimal(4),
                ValeurTotale = r.GetDecimal(5),
                Reference = r.IsDBNull(6) ? null : r.GetString(6),
                Motif = r.IsDBNull(7) ? null : r.GetString(7),
                ArticleCode = r.GetString(8), ArticleDesignation = r.GetString(9),
                EmplacementSource = r.IsDBNull(10) ? null : r.GetString(10),
                EmplacementDestination = r.IsDBNull(11) ? null : r.GetString(11),
                NumeroSerie = r.IsDBNull(12) ? null : r.GetString(12),
                CreatedBy = r.IsDBNull(13) ? null : r.GetString(13)
            });
        return Ok(list);
    }

    // ─── Alertes péremption ───────────────────────────────────────────────────
    [HttpGet("alertes-peremption")]
    public async Task<IActionResult> GetAlertesPeremption([FromQuery] int joursAvance = 30)
    {
        var list = new List<object>();
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(@"
            SELECT l.Id, l.NumeroLot, l.DatePeremption,
                   l.QuantiteRestante, l.Statut,
                   a.Code AS ArticleCode, a.Designation,
                   DATEDIFF(day, GETUTCDATE(), l.DatePeremption) AS JoursRestants
            FROM Lots l
            JOIN Articles a ON a.Id = l.ArticleId
            WHERE l.DatePeremption IS NOT NULL
              AND l.DatePeremption <= DATEADD(day, @jours, GETUTCDATE())
              AND l.Statut NOT IN (3, 4, 5)  -- pas épuisé/périmé/bloqué
              AND l.QuantiteRestante > 0
            ORDER BY l.DatePeremption ASC", conn);
        cmd.Parameters.AddWithValue("@jours", joursAvance);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new {
                Id = r.GetGuid(0), NumeroLot = r.GetString(1),
                DatePeremption = r.GetDateTime(2),
                QuantiteRestante = r.GetInt32(3), Statut = r.GetInt32(4),
                ArticleCode = r.GetString(5), ArticleDesignation = r.GetString(6),
                JoursRestants = r.GetInt32(7),
                Critique = r.GetInt32(7) <= 7
            });
        return Ok(list);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────
    private async Task<object?> GetArticleInfo(Guid id)
    {
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(
            "SELECT Code, Designation, Categorie, FamilleArticle, Unite, PrixAchat, GestionLot, GestionDLUO, ISNULL(GestionNumeroDeSerie,0) FROM Articles WHERE Id=@id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new { Code = r.GetString(0), Designation = r.GetString(1),
            Categorie = r.IsDBNull(2) ? "" : r.GetString(2),
            FamilleArticle = r.IsDBNull(3) ? "" : r.GetString(3),
            Unite = r.GetString(4), PrixAchat = r.GetDecimal(5),
            GestionLot = r.GetBoolean(6), GestionDLUO = r.GetBoolean(7),
            GestionNumeroDeSerie = r.GetBoolean(8) };
    }

    private async Task<List<object>> GetLotsArticle(Guid articleId)
    {
        var list = new List<object>();
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(@"
            SELECT Id, NumeroLot, DateReception, DatePeremption,
                   QuantiteInitiale, QuantiteRestante, Statut, NumeroSerie,
                   CASE WHEN DatePeremption < GETUTCDATE() THEN 1 ELSE 0 END AS EstPerime
            FROM Lots WHERE ArticleId=@id ORDER BY DateReception DESC", conn);
        cmd.Parameters.AddWithValue("@id", articleId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new { Id = r.GetGuid(0), NumeroLot = r.GetString(1),
                DateReception = r.GetDateTime(2),
                DatePeremption = r.IsDBNull(3) ? (DateTime?)null : r.GetDateTime(3),
                QuantiteInitiale = r.GetInt32(4), QuantiteRestante = r.GetInt32(5),
                Statut = r.GetInt32(6), StatutLibelle = GetStatutLot(r.GetInt32(6)),
                NumeroSerie = r.IsDBNull(7) ? null : r.GetString(7),
                EstPerime = r.GetInt32(8) == 1 });
        return list;
    }

    private async Task<List<object>> GetMouvementsArticle(Guid articleId, DateTime du, DateTime au)
    {
        var list = new List<object>();
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(@"
            SELECT ms.Id, ms.DateMouvement, ms.TypeMouvement, ms.Quantite,
                   ms.ValeurUnitaire * ms.Quantite AS ValeurTotale,
                   ms.Reference, ms.Motif,
                   e1.Code AS EmplSource, e2.Code AS EmplDest,
                   l.NumeroLot, ISNULL(ms.NumeroSerie, l.NumeroSerie) AS NumeroSerie, ms.CreatedBy
            FROM MouvementsStock ms
            JOIN Emplacements e1 ON e1.Id = ms.EmplacementSourceId
            LEFT JOIN Emplacements e2 ON e2.Id = ms.EmplacementDestinationId
            LEFT JOIN Lots l ON l.Id = ms.LotId
            WHERE ms.ArticleId = @id AND ms.DateMouvement BETWEEN @du AND @au
            ORDER BY ms.DateMouvement DESC", conn);
        cmd.Parameters.AddWithValue("@id", articleId);
        cmd.Parameters.AddWithValue("@du", du);
        cmd.Parameters.AddWithValue("@au", au);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            list.Add(new { Id = r.GetGuid(0), DateMouvement = r.GetDateTime(1),
                TypeMouvement = r.GetInt32(2), TypeLibelle = GetTypeMvt(r.GetInt32(2)),
                Quantite = r.GetInt32(3), ValeurTotale = r.GetDecimal(4),
                Reference = r.IsDBNull(5) ? null : r.GetString(5),
                Motif = r.IsDBNull(6) ? null : r.GetString(6),
                EmplacementSource = r.IsDBNull(7) ? null : r.GetString(7),
                EmplacementDestination = r.IsDBNull(8) ? null : r.GetString(8),
                NumeroLot = r.IsDBNull(9) ? null : r.GetString(9),
                NumeroSerie = r.IsDBNull(10) ? null : r.GetString(10),
                CreatedBy = r.IsDBNull(11) ? null : r.GetString(11) });
        return list;
    }

    private async Task<object> GetResumeArticle(Guid articleId)
    {
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(@"
            SELECT
                COUNT(*) AS NbLots,
                ISNULL(SUM(QuantiteRestante), 0) AS QteEnLot,
                SUM(CASE WHEN DatePeremption < GETUTCDATE() THEN 1 ELSE 0 END) AS LotsPerimes,
                SUM(CASE WHEN DatePeremption <= DATEADD(day,30,GETUTCDATE()) AND DatePeremption >= GETUTCDATE() THEN 1 ELSE 0 END) AS LotsAlerte
            FROM Lots WHERE ArticleId = @id", conn);
        cmd.Parameters.AddWithValue("@id", articleId);
        await using var r = await cmd.ExecuteReaderAsync();
        if (await r.ReadAsync())
            return new { NbLots = r.GetInt32(0), QteEnLot = r.GetInt32(1),
                LotsPerimes = r.GetInt32(2), LotsAlerte = r.GetInt32(3) };
        return new { NbLots = 0, QteEnLot = 0, LotsPerimes = 0, LotsAlerte = 0 };
    }

    private static string GetStatutLot(int s) => s switch {
        1 => "Disponible", 2 => "Quarantaine", 3 => "Épuisé",
        4 => "Périmé", 5 => "Bloqué", _ => "Inconnu" };

    private static string GetTypeMvt(int t) => t switch {
        1 => "Entrée", 2 => "Sortie", 3 => "Transfert",
        4 => "Ajustement", 5 => "Retour", 6 => "Perte", 7 => "Production", _ => "?" };
}

public class LotRequest
{
    public Guid ArticleId { get; set; }
    public string NumeroLot { get; set; } = string.Empty;
    public DateTime DateReception { get; set; } = DateTime.Today;
    public DateTime? DateFabrication { get; set; }
    public DateTime? DatePeremption { get; set; }
    public int Quantite { get; set; } = 1;
    public string? NumeroSerie { get; set; }
    public string? CertifiqueQualite { get; set; }
}

public class StatutLotRequest { public int Statut { get; set; } }
