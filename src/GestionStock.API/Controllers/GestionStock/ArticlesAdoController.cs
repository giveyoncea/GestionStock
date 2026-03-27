using GestionStock.API.Services;
using GestionStock.Application.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Security.Claims;

namespace GestionStock.API.Controllers;

/// <summary>
/// Implémentation ADO.NET des articles — supporte le routage multi-tenant.
/// Le claim "tenant" du JWT route vers GestionStock_{CODE}.
/// </summary>
[ApiController]
[Route("api/articles")]
[Authorize]
[Tags("Articles")]
public class ArticlesAdoController : ControllerBase
{
    private readonly ITenantService _tenant;
    private readonly IConfiguration _config;

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

    public ArticlesAdoController(ITenantService tenant, IConfiguration config)
    {
        _tenant = tenant;
        _config = config;
    }

    // ─── Détecte le nom de la table de stock ─────────────────────────────────
    private static async Task<string> GetStockTableAsync(SqlConnection conn)
    {
        // Stocks = schéma standard (nouvelles bases + GestionStockDB)
        // StockArticles = ancien schéma (base GOUN avant standardisation)
        await using var cmd = new SqlCommand(
            "SELECT CASE WHEN OBJECT_ID('Stocks') IS NOT NULL THEN 'Stocks' ELSE 'StockArticles' END",
            conn);
        return (string)(await cmd.ExecuteScalarAsync() ?? "Stocks");
    }

    // ─── Construit la sous-requête stock ─────────────────────────────────────
    private static string StockSub(string stockTable)
        => stockTable is null
            ? "0"
            : $"ISNULL((SELECT SUM(QuantiteDisponible) FROM {stockTable} WHERE ArticleId=a.Id), 0)";

    // ─── Liste paginée ────────────────────────────────────────────────────────
    [HttpGet]
    [Authorize(Policy = "Lecteur")]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? categorie = null)
    {
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();

            var stockTable = await GetStockTableAsync(conn);
            var stockSub   = StockSub(stockTable);

            var where = "WHERE a.Statut = 1";
            if (!string.IsNullOrWhiteSpace(search))
                where += " AND (a.Code LIKE @s OR a.Designation LIKE @s OR a.CodeBarres LIKE @s)";
            if (!string.IsNullOrWhiteSpace(categorie))
                where += " AND a.Categorie = @cat";

            // Compte total
            await using var countCmd = new SqlCommand(
                $"SELECT COUNT(1) FROM Articles a {where}", conn);
            if (!string.IsNullOrWhiteSpace(search))  countCmd.Parameters.AddWithValue("@s", $"%{search}%");
            if (!string.IsNullOrWhiteSpace(categorie)) countCmd.Parameters.AddWithValue("@cat", categorie);
            var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

            // Données paginées
            var sql = $@"
                SELECT a.Id, a.Code, ISNULL(a.CodeBarres,'') AS CodeBarres,
                       a.Designation, ISNULL(a.Description,'') AS Description,
                       ISNULL(a.Categorie,'') AS Categorie,
                       ISNULL(a.FamilleArticle,'') AS FamilleArticle,
                       ISNULL(a.Unite,'PCS') AS Unite,
                       ISNULL(a.PrixAchat,0) AS PrixAchat,
                       ISNULL(a.PrixVente,0) AS PrixVente,
                       ISNULL(a.SeuilAlerte,0) AS SeuilAlerte,
                       ISNULL(a.StockMinimum,0) AS StockMinimum,
                       ISNULL(a.StockMaximum,0) AS StockMaximum,
                       ISNULL(a.GestionLot,0) AS GestionLot,
                       ISNULL(a.GestionDLUO,0) AS GestionDLUO,
                       ISNULL(a.Statut,1) AS Statut,
                       ISNULL(a.CreatedAt, GETUTCDATE()) AS CreatedAt,
                       {stockSub} AS QuantiteTotale,
                       ISNULL(f.RaisonSociale,'') AS FournisseurNom
                FROM Articles a
                LEFT JOIN Fournisseurs f ON f.Id = a.FournisseurPrincipalId
                {where}
                ORDER BY a.Code
                OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@skip", (page - 1) * pageSize);
            cmd.Parameters.AddWithValue("@take", pageSize);
            if (!string.IsNullOrWhiteSpace(search))   cmd.Parameters.AddWithValue("@s", $"%{search}%");
            if (!string.IsNullOrWhiteSpace(categorie)) cmd.Parameters.AddWithValue("@cat", categorie);

            var items = new List<object>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var qte   = reader.GetInt32(reader.GetOrdinal("QuantiteTotale"));
                var seuil = reader.GetInt32(reader.GetOrdinal("SeuilAlerte"));
                items.Add(new
                {
                    id             = reader.GetGuid(reader.GetOrdinal("Id")),
                    code           = reader.GetString(reader.GetOrdinal("Code")),
                    codeBarres     = reader.GetString(reader.GetOrdinal("CodeBarres")),
                    designation    = reader.GetString(reader.GetOrdinal("Designation")),
                    description    = reader.GetString(reader.GetOrdinal("Description")),
                    categorie      = reader.GetString(reader.GetOrdinal("Categorie")),
                    familleArticle = reader.GetString(reader.GetOrdinal("FamilleArticle")),
                    unite          = reader.GetString(reader.GetOrdinal("Unite")),
                    prixAchat      = reader.GetDecimal(reader.GetOrdinal("PrixAchat")),
                    prixVente      = reader.GetDecimal(reader.GetOrdinal("PrixVente")),
                    seuilAlerte    = seuil,
                    stockMinimum   = reader.GetInt32(reader.GetOrdinal("StockMinimum")),
                    stockMaximum   = reader.GetInt32(reader.GetOrdinal("StockMaximum")),
                    gestionLot     = reader.GetBoolean(reader.GetOrdinal("GestionLot")),
                    gestionDLUO    = reader.GetBoolean(reader.GetOrdinal("GestionDLUO")),
                    statut         = reader.GetInt32(reader.GetOrdinal("Statut")),
                    createdAt      = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                    quantiteTotale = qte,
                    estEnAlerte    = qte <= seuil && seuil > 0,
                    estEnRupture   = qte == 0,
                    fournisseurNom = reader.GetString(reader.GetOrdinal("FournisseurNom"))
                });
            }

            return Ok(new
            {
                items,
                totalCount = total,
                page,
                pageSize,
                totalPages = (int)Math.Ceiling(total / (double)pageSize)
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { succes = false, message = ex.Message, detail = ex.InnerException?.Message });
        }
    }

    // ─── Détail par ID ────────────────────────────────────────────────────────
    [HttpGet("{id:guid}")]
    [Authorize(Policy = "Lecteur")]
    public async Task<IActionResult> GetById(Guid id)
    {
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        var stockSub = StockSub(await GetStockTableAsync(conn));
        await using var cmd = new SqlCommand($@"
            SELECT a.*, ISNULL(f.RaisonSociale,'') AS FournisseurNom,
                   {stockSub} AS QuantiteTotale
            FROM Articles a
            LEFT JOIN Fournisseurs f ON f.Id = a.FournisseurPrincipalId
            WHERE a.Id=@id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return NotFound();
        return Ok(BuildArticleObject(r));
    }

    // ─── Recherche par code-barres ────────────────────────────────────────────
    [HttpGet("barcode/{codeBarres}")]
    [Authorize(Policy = "Lecteur")]
    public async Task<IActionResult> GetByCodeBarres(string codeBarres)
    {
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        var stockSub = StockSub(await GetStockTableAsync(conn));
        await using var cmd = new SqlCommand($@"
            SELECT a.*, ISNULL(f.RaisonSociale,'') AS FournisseurNom,
                   {stockSub} AS QuantiteTotale
            FROM Articles a
            LEFT JOIN Fournisseurs f ON f.Id=a.FournisseurPrincipalId
            WHERE a.CodeBarres=@cb AND a.Statut=1", conn);
        cmd.Parameters.AddWithValue("@cb", codeBarres);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return NotFound();
        return Ok(BuildArticleObject(r));
    }

    // ─── Catégories ───────────────────────────────────────────────────────────
    [HttpGet("categories")]
    [Authorize(Policy = "Lecteur")]
    public async Task<IActionResult> GetCategories()
    {
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(
            "SELECT DISTINCT Categorie FROM Articles WHERE Statut=1 AND Categorie<>'' ORDER BY Categorie", conn);
        var cats = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) cats.Add(r.GetString(0));
        return Ok(cats);
    }

    // ─── Articles en alerte ───────────────────────────────────────────────────
    [HttpGet("alertes")]
    [Authorize(Policy = "Lecteur")]
    public async Task<IActionResult> GetAlertes()
    {
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        var stockSub = StockSub(await GetStockTableAsync(conn));
        await using var cmd = new SqlCommand($@"
            SELECT a.Id, a.Code, a.Designation, a.Categorie, a.SeuilAlerte,
                   {stockSub} AS QuantiteTotale
            FROM Articles a
            WHERE a.Statut=1
            HAVING {stockSub} <= a.SeuilAlerte", conn);
        var items = new List<object>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            items.Add(new
            {
                id = r.GetGuid(0), code = r.GetString(1),
                designation = r.GetString(2), categorie = r.GetString(3),
                seuilAlerte = r.GetInt32(4), quantiteTotale = r.GetInt32(5)
            });
        }
        return Ok(items);
    }

    // ─── Créer ────────────────────────────────────────────────────────────────
    [HttpPost]
    [Authorize(Policy = "Magasinier")]
    public async Task<IActionResult> Creer([FromBody] CreerArticleDto dto)
    {
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        await using var chk = new SqlCommand(
            "SELECT COUNT(1) FROM Articles WHERE Code=@c AND Statut=1", conn);
        chk.Parameters.AddWithValue("@c", dto.Code.Trim().ToUpper());
        if (Convert.ToInt32(await chk.ExecuteScalarAsync()) > 0)
            return BadRequest(new { succes = false, message = $"Le code '{dto.Code}' existe déjà." });

        var id = Guid.NewGuid();
        await using var cmd = new SqlCommand(@"
            INSERT INTO Articles
                (Id,Code,CodeBarres,Designation,Description,Categorie,FamilleArticle,Unite,
                 PrixAchat,PrixVente,ValeurStockMoyen,SeuilAlerte,StockMinimum,StockMaximum,
                 GestionLot,GestionNumeroDeSerie,GestionDLUO,GestionDLC,Statut,CreatedAt,CreatedBy)
            VALUES
                (@id,@code,@cb,@desig,@desc,@cat,@fam,@unite,
                 @pa,@pv,0,@sa,@sm,@smax,
                 @gl,0,@gdluo,0,1,GETUTCDATE(),@user)", conn);
        cmd.Parameters.AddWithValue("@id",    id);
        cmd.Parameters.AddWithValue("@code",  dto.Code.Trim().ToUpper());
        cmd.Parameters.AddWithValue("@cb",    (object?)dto.CodeBarres ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@desig", dto.Designation.Trim());
        cmd.Parameters.AddWithValue("@desc",  (object?)dto.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cat",   dto.Categorie?.Trim() ?? "");
        cmd.Parameters.AddWithValue("@fam",   dto.FamilleArticle?.Trim() ?? "");
        cmd.Parameters.AddWithValue("@unite", dto.Unite?.Trim() ?? "PCS");
        cmd.Parameters.AddWithValue("@pa",    dto.PrixAchat);
        cmd.Parameters.AddWithValue("@pv",    dto.PrixVente);
        cmd.Parameters.AddWithValue("@sa",    dto.SeuilAlerte);
        cmd.Parameters.AddWithValue("@sm",    dto.StockMinimum);
        cmd.Parameters.AddWithValue("@smax",  dto.StockMaximum);
        cmd.Parameters.AddWithValue("@gl",    dto.GestionLot ? 1 : 0);
        cmd.Parameters.AddWithValue("@gdluo", dto.GestionDLUO ? 1 : 0);
        cmd.Parameters.AddWithValue("@user",  UserId);
        await cmd.ExecuteNonQueryAsync();
        return CreatedAtAction(nameof(GetById), new { id }, new { succes = true, data = id });
    }

    // ─── Modifier ─────────────────────────────────────────────────────────────
    [HttpPut("{id:guid}")]
    [Authorize(Policy = "Magasinier")]
    public async Task<IActionResult> Modifier(Guid id, [FromBody] ModifierArticleDto dto)
    {
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(@"
            UPDATE Articles SET
                Designation=@desig, Description=@desc, Categorie=@cat,
                FamilleArticle=@fam, Unite=@unite, PrixAchat=@pa, PrixVente=@pv,
                SeuilAlerte=@sa, StockMinimum=@sm, StockMaximum=@smax,
                GestionLot=@gl, GestionDLUO=@gdluo,
                UpdatedAt=GETUTCDATE(), UpdatedBy=@user
            WHERE Id=@id", conn);
        cmd.Parameters.AddWithValue("@id",    id);
        cmd.Parameters.AddWithValue("@desig", dto.Designation.Trim());
        cmd.Parameters.AddWithValue("@desc",  (object?)dto.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cat",   dto.Categorie?.Trim() ?? "");
        cmd.Parameters.AddWithValue("@fam",   dto.FamilleArticle?.Trim() ?? "");
        cmd.Parameters.AddWithValue("@unite", dto.Unite?.Trim() ?? "PCS");
        cmd.Parameters.AddWithValue("@pa",    dto.PrixAchat);
        cmd.Parameters.AddWithValue("@pv",    dto.PrixVente);
        cmd.Parameters.AddWithValue("@sa",    dto.SeuilAlerte);
        cmd.Parameters.AddWithValue("@sm",    dto.StockMinimum);
        cmd.Parameters.AddWithValue("@smax",  dto.StockMaximum);
        cmd.Parameters.AddWithValue("@gl",    dto.GestionLot ? 1 : 0);
        cmd.Parameters.AddWithValue("@gdluo", dto.GestionDLUO ? 1 : 0);
        cmd.Parameters.AddWithValue("@user",  UserId);
        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0
            ? Ok(new { succes = true, message = "Article modifié." })
            : NotFound(new { succes = false, message = "Article introuvable." });
    }

    // ─── Désactiver ───────────────────────────────────────────────────────────
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Desactiver(Guid id)
    {
        await using var conn = new SqlConnection(ConnStr);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(
            "UPDATE Articles SET Statut=2, UpdatedAt=GETUTCDATE(), UpdatedBy=@user WHERE Id=@id", conn);
        cmd.Parameters.AddWithValue("@id",   id);
        cmd.Parameters.AddWithValue("@user", UserId);
        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0
            ? Ok(new { succes = true, message = "Article désactivé." })
            : NotFound(new { succes = false, message = "Article introuvable." });
    }

    // ─── Helper ───────────────────────────────────────────────────────────────
    private static object BuildArticleObject(SqlDataReader r)
    {
        int qte = 0, seuil = 0;
        try { qte   = r.GetInt32(r.GetOrdinal("QuantiteTotale")); } catch { }
        try { seuil = r.GetInt32(r.GetOrdinal("SeuilAlerte"));    } catch { }
        return new
        {
            id             = r.GetGuid(r.GetOrdinal("Id")),
            code           = r.GetString(r.GetOrdinal("Code")),
            codeBarres     = r.IsDBNull(r.GetOrdinal("CodeBarres"))     ? "" : r.GetString(r.GetOrdinal("CodeBarres")),
            designation    = r.GetString(r.GetOrdinal("Designation")),
            description    = r.IsDBNull(r.GetOrdinal("Description"))    ? "" : r.GetString(r.GetOrdinal("Description")),
            categorie      = r.IsDBNull(r.GetOrdinal("Categorie"))      ? "" : r.GetString(r.GetOrdinal("Categorie")),
            familleArticle = r.IsDBNull(r.GetOrdinal("FamilleArticle")) ? "" : r.GetString(r.GetOrdinal("FamilleArticle")),
            unite          = r.IsDBNull(r.GetOrdinal("Unite"))          ? "PCS" : r.GetString(r.GetOrdinal("Unite")),
            prixAchat      = r.IsDBNull(r.GetOrdinal("PrixAchat"))      ? 0m : r.GetDecimal(r.GetOrdinal("PrixAchat")),
            prixVente      = r.IsDBNull(r.GetOrdinal("PrixVente"))      ? 0m : r.GetDecimal(r.GetOrdinal("PrixVente")),
            seuilAlerte    = seuil,
            stockMinimum   = r.IsDBNull(r.GetOrdinal("StockMinimum"))   ? 0 : r.GetInt32(r.GetOrdinal("StockMinimum")),
            stockMaximum   = r.IsDBNull(r.GetOrdinal("StockMaximum"))   ? 0 : r.GetInt32(r.GetOrdinal("StockMaximum")),
            gestionLot     = r.IsDBNull(r.GetOrdinal("GestionLot"))     ? false : r.GetBoolean(r.GetOrdinal("GestionLot")),
            gestionDLUO    = r.IsDBNull(r.GetOrdinal("GestionDLUO"))    ? false : r.GetBoolean(r.GetOrdinal("GestionDLUO")),
            statut         = r.IsDBNull(r.GetOrdinal("Statut"))         ? 1 : r.GetInt32(r.GetOrdinal("Statut")),
            createdAt      = r.IsDBNull(r.GetOrdinal("CreatedAt"))      ? DateTime.UtcNow : r.GetDateTime(r.GetOrdinal("CreatedAt")),
            quantiteTotale = qte,
            estEnAlerte    = qte <= seuil && seuil > 0,
            estEnRupture   = qte == 0,
            fournisseurNom = r.IsDBNull(r.GetOrdinal("FournisseurNom")) ? "" : r.GetString(r.GetOrdinal("FournisseurNom"))
        };
    }
}
