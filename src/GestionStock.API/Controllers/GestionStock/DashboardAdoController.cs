using GestionStock.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Security.Claims;

namespace GestionStock.API.Controllers;

/// <summary>
/// Tableau de bord multi-tenant via ADO.NET.
/// Lit le claim "tenant" et route vers la bonne base.
/// </summary>
[ApiController]
[Route("api/dashboard")]
[Authorize(Policy = "Lecteur")]
[Tags("Tableau de Bord")]
public class DashboardAdoController : ControllerBase
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

    public DashboardAdoController(ITenantService tenant, IConfiguration config)
    {
        _tenant = tenant;
        _config = config;
    }

    [HttpGet]
    public async Task<IActionResult> GetDashboard()
    {
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();

            if (!await TableExistsAsync(conn, "Articles"))
                return Ok(CreateEmptyDashboard());

            var hasStocks = await TableExistsAsync(conn, "Stocks");
            var hasLegacyStocks = await TableExistsAsync(conn, "StockArticles");
            var stockTable = hasStocks ? "Stocks" : hasLegacyStocks ? "StockArticles" : null;

            var stockCte = stockTable is null
                ? "SELECT CAST(NULL AS uniqueidentifier) AS ArticleId, CAST(0 AS int) AS QteTotal WHERE 1 = 0"
                : $@"SELECT ArticleId, ISNULL(SUM(QuantiteDisponible),0) AS QteTotal
                     FROM {stockTable}
                     GROUP BY ArticleId";

            await using var statsCmd = new SqlCommand($@"
                WITH StockParArticle AS (
                    {stockCte}
                )
                SELECT
                    COUNT(*) AS TotalArticles,
                    ISNULL(SUM(CASE WHEN ISNULL(s.QteTotal,0) = 0 THEN 1 ELSE 0 END), 0) AS EnRupture,
                    ISNULL(SUM(CASE WHEN ISNULL(s.QteTotal,0) > 0
                                      AND ISNULL(a.SeuilAlerte, 0) > 0
                                      AND ISNULL(s.QteTotal,0) <= a.SeuilAlerte
                                     THEN 1 ELSE 0 END), 0) AS EnAlerte,
                    ISNULL(SUM(ISNULL(s.QteTotal,0) * ISNULL(a.PrixAchat, 0)), 0) AS ValeurTotale
                FROM Articles a
                LEFT JOIN StockParArticle s ON s.ArticleId = a.Id
                WHERE a.Statut = 1", conn);

            var totalArticles = 0;
            var enRupture = 0;
            var enAlerte = 0;
            decimal valeurTotale = 0;

            await using (var r = await statsCmd.ExecuteReaderAsync())
            {
                if (await r.ReadAsync())
                {
                    totalArticles = r.IsDBNull(0) ? 0 : r.GetInt32(0);
                    enRupture = r.IsDBNull(1) ? 0 : r.GetInt32(1);
                    enAlerte = r.IsDBNull(2) ? 0 : r.GetInt32(2);
                    valeurTotale = GetDecimal(r, 3);
                }
            }

            var alertes = new List<object>();
            await using (var alertCmd = new SqlCommand($@"
                WITH StockParArticle2 AS (
                    {stockCte}
                )
                SELECT TOP 10
                    a.Id,
                    a.Code,
                    a.Designation,
                    ISNULL(s.QteTotal,0) AS Qte,
                    ISNULL(a.SeuilAlerte,0) AS SeuilAlerte,
                    ISNULL(a.StockMinimum,0) AS StockMinimum,
                    CASE WHEN ISNULL(s.QteTotal,0) = 0 THEN 'RUPTURE' ELSE 'ALERTE' END AS TypeAlerte
                FROM Articles a
                LEFT JOIN StockParArticle2 s ON s.ArticleId = a.Id
                WHERE a.Statut = 1
                  AND (
                    ISNULL(s.QteTotal,0) = 0
                    OR (ISNULL(a.SeuilAlerte,0) > 0 AND ISNULL(s.QteTotal,0) <= a.SeuilAlerte)
                  )
                ORDER BY Qte ASC", conn))
            {
                await using var r = await alertCmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    alertes.Add(new
                    {
                        articleId = r.GetGuid(0),
                        articleCode = r.GetString(1),
                        articleDesignation = r.GetString(2),
                        quantiteActuelle = r.GetInt32(3),
                        seuilAlerte = r.GetInt32(4),
                        stockMinimum = r.GetInt32(5),
                        typeAlerte = r.GetString(6)
                    });
                }
            }

            var mouvements = new List<object>();
            if (await TableExistsAsync(conn, "MouvementsStock"))
            {
                await using var colCmd = new SqlCommand(@"
                    SELECT
                        CASE WHEN EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('MouvementsStock') AND name='DateMouvement')
                             THEN 'DateMouvement' ELSE 'CreatedAt' END AS ColDate,
                        CASE WHEN EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('MouvementsStock') AND name='ValeurUnitaire')
                             THEN 'ValeurUnitaire' ELSE 'PrixUnitaire' END AS ColPrix", conn);

                var colDate = "CreatedAt";
                var colPrix = "PrixUnitaire";
                await using (var colR = await colCmd.ExecuteReaderAsync())
                {
                    if (await colR.ReadAsync())
                    {
                        colDate = colR.GetString(0);
                        colPrix = colR.GetString(1);
                    }
                }

                var hasEmplacements = await TableExistsAsync(conn, "Emplacements");
                var emplacementsJoin = hasEmplacements
                    ? "LEFT JOIN Emplacements es ON es.Id = m.EmplacementSourceId"
                    : string.Empty;
                var emplacementsSelect = hasEmplacements
                    ? "ISNULL(es.Code,'')"
                    : "CAST('' AS nvarchar(50))";

                await using var mvtCmd = new SqlCommand($@"
                    SELECT TOP 10
                        m.Id,
                        ISNULL(a.Code,'') AS ArticleCode,
                        ISNULL(a.Designation,'') AS ArticleDesignation,
                        {emplacementsSelect} AS EmplacementSource,
                        m.TypeMouvement,
                        m.Quantite,
                        ISNULL(m.{colPrix},0) AS ValeurUnitaire,
                        ISNULL(m.Quantite * m.{colPrix},0) AS ValeurTotale,
                        ISNULL(m.Reference,'') AS Reference,
                        ISNULL(m.Motif,'') AS Motif,
                        m.{colDate} AS DateMouvement,
                        ISNULL(m.CreatedBy,'') AS CreatedBy
                    FROM MouvementsStock m
                    LEFT JOIN Articles a ON a.Id = m.ArticleId
                    {emplacementsJoin}
                    WHERE m.{colDate} >= DATEADD(day, -10, GETUTCDATE())
                    ORDER BY m.{colDate} DESC", conn);

                await using var r = await mvtCmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    var typeMvt = r.GetInt32(4);
                    mouvements.Add(new
                    {
                        id = r.GetGuid(0),
                        articleCode = r.GetString(1),
                        articleDesignation = r.GetString(2),
                        emplacementSource = r.GetString(3),
                        typeMouvement = typeMvt,
                        typeMouvementLibelle = typeMvt switch
                        {
                            1 => "Entree",
                            2 => "Sortie",
                            3 => "Transfert",
                            4 => "Ajustement",
                            _ => "Autre"
                        },
                        quantite = GetDecimal(r, 5),
                        valeurUnitaire = GetDecimal(r, 6),
                        valeurTotale = GetDecimal(r, 7),
                        reference = r.GetString(8),
                        motif = r.GetString(9),
                        dateMouvement = r.GetDateTime(10),
                        createdBy = r.GetString(11)
                    });
                }
            }

            var commandesEnAttente = 0;
            if (await TableExistsAsync(conn, "CommandesAchat"))
            {
                try
                {
                    await using var cmdCmd = new SqlCommand(
                        "SELECT COUNT(*) FROM CommandesAchat WHERE Statut IN (1,2)", conn);
                    commandesEnAttente = Convert.ToInt32(await cmdCmd.ExecuteScalarAsync());
                }
                catch
                {
                    commandesEnAttente = 0;
                }
            }

            return Ok(new
            {
                totalArticles,
                articlesEnAlerte = enAlerte,
                articlesEnRupture = enRupture,
                commandesEnAttente,
                lotsSurveiller = 0,
                valeurTotaleStock = valeurTotale,
                dernieresAlertes = alertes,
                derniersMouvements = mouvements,
                commandesUrgentes = Array.Empty<object>()
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                succes = false,
                message = ex.Message,
                detail = ex.InnerException?.Message
            });
        }
    }

    private static async Task<bool> TableExistsAsync(SqlConnection conn, string tableName)
    {
        await using var cmd = new SqlCommand(
            "SELECT CASE WHEN OBJECT_ID(@tableName, 'U') IS NOT NULL THEN 1 ELSE 0 END",
            conn);
        cmd.Parameters.AddWithValue("@tableName", tableName);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) == 1;
    }

    private static object CreateEmptyDashboard() => new
    {
        totalArticles = 0,
        articlesEnAlerte = 0,
        articlesEnRupture = 0,
        commandesEnAttente = 0,
        lotsSurveiller = 0,
        valeurTotaleStock = 0m,
        dernieresAlertes = Array.Empty<object>(),
        derniersMouvements = Array.Empty<object>(),
        commandesUrgentes = Array.Empty<object>()
    };

    private static decimal GetDecimal(SqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? 0m : Convert.ToDecimal(reader.GetValue(ordinal));
}
