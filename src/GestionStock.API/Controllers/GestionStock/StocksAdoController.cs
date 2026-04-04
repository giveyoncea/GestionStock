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

    private sealed record MovementInsertSchema(
        bool HasPrixUnitaire,
        bool HasValeurUnitaire,
        bool HasCreatedAt,
        bool HasDateMouvement,
        bool HasReference,
        bool HasMotif,
        bool HasNumeroLot,
        bool HasNumeroSerie,
        bool HasSource,
        bool HasDestination,
        bool HasCreatedBy);

    private static async Task<MovementInsertSchema> GetMovementInsertSchemaAsync(SqlConnection conn, SqlTransaction? tx = null)
    {
        await using var cmd = new SqlCommand(@"
            SELECT name, is_computed
            FROM sys.columns
            WHERE object_id = OBJECT_ID('MouvementsStock')", conn, tx);
        await using var reader = await cmd.ExecuteReaderAsync();

        var columns = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
        {
            columns[reader.GetString(0)] = reader.GetBoolean(1);
        }

        return new MovementInsertSchema(
            columns.TryGetValue("PrixUnitaire", out var prixComputed) && !prixComputed,
            columns.TryGetValue("ValeurUnitaire", out var valeurComputed) && !valeurComputed,
            columns.TryGetValue("CreatedAt", out var createdAtComputed) && !createdAtComputed,
            columns.TryGetValue("DateMouvement", out var dateComputed) && !dateComputed,
            columns.ContainsKey("Reference"),
            columns.ContainsKey("Motif"),
            columns.ContainsKey("NumeroLot"),
            columns.ContainsKey("NumeroSerie"),
            columns.ContainsKey("EmplacementSourceId"),
            columns.ContainsKey("EmplacementDestinationId"),
            columns.ContainsKey("CreatedBy"));
    }

    private static string BuildMovementInsertSql(MovementInsertSchema schema, int typeMouvement, bool includeDestination)
    {
        var columns = new List<string> { "Id", "ArticleId" };
        var values = new List<string> { "NEWID()", "@artId" };

        if (schema.HasSource)
        {
            columns.Add("EmplacementSourceId");
            values.Add("@src");
        }

        if (includeDestination && schema.HasDestination)
        {
            columns.Add("EmplacementDestinationId");
            values.Add("@dst");
        }

        columns.Add("TypeMouvement");
        values.Add(typeMouvement.ToString());

        columns.Add("Quantite");
        values.Add("@qte");

        if (schema.HasPrixUnitaire)
        {
            columns.Add("PrixUnitaire");
            values.Add("@prix");
        }

        if (schema.HasValeurUnitaire)
        {
            columns.Add("ValeurUnitaire");
            values.Add("@prix");
        }

        if (schema.HasCreatedAt)
        {
            columns.Add("CreatedAt");
            values.Add("GETUTCDATE()");
        }

        if (schema.HasDateMouvement)
        {
            columns.Add("DateMouvement");
            values.Add("GETUTCDATE()");
        }

        if (schema.HasReference)
        {
            columns.Add("Reference");
            values.Add("@ref");
        }

        if (schema.HasMotif)
        {
            columns.Add("Motif");
            values.Add("@motif");
        }

        if (schema.HasNumeroLot)
        {
            columns.Add("NumeroLot");
            values.Add("@lot");
        }

        if (schema.HasNumeroSerie)
        {
            columns.Add("NumeroSerie");
            values.Add("@serie");
        }

        if (schema.HasCreatedBy)
        {
            columns.Add("CreatedBy");
            values.Add("@user");
        }

        return $@"INSERT INTO MouvementsStock ({string.Join(",", columns)})
            VALUES ({string.Join(",", values)})";
    }

    private static async Task<bool> IsStockNegatifAutoriseAsync(SqlConnection conn)
    {
        await using var cmd = new SqlCommand(
            "SELECT ISNULL(AutoriserStockNegatif,0) FROM Parametres WHERE Id = 1", conn);
        return Convert.ToBoolean(await cmd.ExecuteScalarAsync() ?? false);
    }

    private static async Task<HashSet<string>> GetTableColumnsAsync(SqlConnection conn, string tableName, SqlTransaction? tx = null)
    {
        await using var cmd = new SqlCommand(@"
            SELECT name
            FROM sys.columns
            WHERE object_id = OBJECT_ID(@tableName)", conn, tx);
        cmd.Parameters.AddWithValue("@tableName", tableName);
        await using var reader = await cmd.ExecuteReaderAsync();

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }

    private static async Task EnsureColumnDefaultAsync(
        SqlConnection conn,
        string tableName,
        string columnName,
        string constraintName,
        string defaultSql)
    {
        await using var cmd = new SqlCommand($@"
IF EXISTS (
    SELECT 1
    FROM sys.columns c
    INNER JOIN sys.tables t ON t.object_id = c.object_id
    INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
    WHERE t.name = @tableName
      AND s.name = 'dbo'
      AND c.name = @columnName
      AND c.is_nullable = 0
      AND c.default_object_id = 0
)
BEGIN
    ALTER TABLE dbo.[{tableName}]
    ADD CONSTRAINT [{constraintName}] DEFAULT ({defaultSql}) FOR [{columnName}]
END", conn);
        cmd.Parameters.AddWithValue("@tableName", tableName);
        cmd.Parameters.AddWithValue("@columnName", columnName);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task EnsureLegacyStockDefaultsAsync(SqlConnection conn)
    {
        await EnsureColumnDefaultAsync(conn, "Stocks", "QuantiteEnCommande", "DF_Stocks_QuantiteEnCommande_Auto", "0");
        await EnsureColumnDefaultAsync(conn, "Stocks", "CreatedAt", "DF_Stocks_CreatedAt_Auto", "GETUTCDATE()");
        await EnsureColumnDefaultAsync(conn, "MouvementsStock", "CreatedAt", "DF_MouvementsStock_CreatedAt_Auto", "GETUTCDATE()");
        await EnsureColumnDefaultAsync(conn, "MouvementsStock", "DateMouvement", "DF_MouvementsStock_DateMouvement_Auto", "GETUTCDATE()");
    }

    private static async Task EnsureStockDocumentTablesAsync(SqlConnection conn)
    {
        const string sql = @"
IF OBJECT_ID('DocumentsStock', 'U') IS NULL
BEGIN
    CREATE TABLE DocumentsStock (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        Numero NVARCHAR(30) NOT NULL,
        TypeDocument INT NOT NULL,
        Reference NVARCHAR(100) NULL,
        DateDocument DATETIME2 NOT NULL,
        Statut INT NOT NULL CONSTRAINT DF_DocumentsStock_Statut DEFAULT(0),
        NombreLignes INT NOT NULL CONSTRAINT DF_DocumentsStock_NombreLignes DEFAULT(0),
        QuantiteTotale INT NOT NULL CONSTRAINT DF_DocumentsStock_QuantiteTotale DEFAULT(0),
        ValeurTotale DECIMAL(18,2) NOT NULL CONSTRAINT DF_DocumentsStock_ValeurTotale DEFAULT(0),
        Motif NVARCHAR(500) NULL,
        ValidatedAt DATETIME2 NULL,
        ValidatedBy NVARCHAR(450) NULL,
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_DocumentsStock_CreatedAt DEFAULT(GETUTCDATE()),
        CreatedBy NVARCHAR(450) NOT NULL CONSTRAINT DF_DocumentsStock_CreatedBy DEFAULT('')
    );
    CREATE INDEX IX_DocumentsStock_DateDocument ON DocumentsStock(DateDocument DESC);
    CREATE INDEX IX_DocumentsStock_TypeDocument ON DocumentsStock(TypeDocument);
    CREATE UNIQUE INDEX IX_DocumentsStock_Numero ON DocumentsStock(Numero);
END

IF COL_LENGTH('DocumentsStock', 'Statut') IS NULL
BEGIN
    ALTER TABLE DocumentsStock
    ADD Statut INT NOT NULL CONSTRAINT DF_DocumentsStock_Statut_Auto DEFAULT(0) WITH VALUES;
END

IF COL_LENGTH('DocumentsStock', 'ValidatedAt') IS NULL
BEGIN
    ALTER TABLE DocumentsStock
    ADD ValidatedAt DATETIME2 NULL;
END

IF COL_LENGTH('DocumentsStock', 'ValidatedBy') IS NULL
BEGIN
    ALTER TABLE DocumentsStock
    ADD ValidatedBy NVARCHAR(450) NULL;
END

IF OBJECT_ID('LignesDocumentsStock', 'U') IS NULL
BEGIN
    CREATE TABLE LignesDocumentsStock (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        DocumentId UNIQUEIDENTIFIER NOT NULL,
        ArticleId UNIQUEIDENTIFIER NOT NULL,
        ArticleCode NVARCHAR(50) NOT NULL,
        Designation NVARCHAR(255) NOT NULL,
        EmplacementSourceId UNIQUEIDENTIFIER NULL,
        EmplacementSourceCode NVARCHAR(100) NULL,
        EmplacementDestinationId UNIQUEIDENTIFIER NULL,
        EmplacementDestinationCode NVARCHAR(100) NULL,
        Quantite INT NOT NULL,
        ValeurUnitaire DECIMAL(18,4) NOT NULL CONSTRAINT DF_LignesDocumentsStock_ValeurUnitaire DEFAULT(0),
        ValeurTotale DECIMAL(18,2) NOT NULL CONSTRAINT DF_LignesDocumentsStock_ValeurTotale DEFAULT(0),
        NumeroLot NVARCHAR(100) NULL,
        NumeroSerie NVARCHAR(100) NULL,
        Motif NVARCHAR(500) NULL,
        Ordre INT NOT NULL CONSTRAINT DF_LignesDocumentsStock_Ordre DEFAULT(0),
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_LignesDocumentsStock_CreatedAt DEFAULT(GETUTCDATE())
    );
    ALTER TABLE LignesDocumentsStock
        ADD CONSTRAINT FK_LignesDocumentsStock_Document
        FOREIGN KEY (DocumentId) REFERENCES DocumentsStock(Id) ON DELETE CASCADE;
    CREATE INDEX IX_LignesDocumentsStock_DocumentId ON LignesDocumentsStock(DocumentId);
END";

        await using var cmd = new SqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static string GetDocumentTypeLabel(int typeDocument) => typeDocument switch
    {
        1 => "Entree",
        2 => "Sortie",
        3 => "Transfert",
        4 => "Inventaire",
        _ => "Document"
    };

    private static string GetDocumentStatusLabel(int statut) => statut switch
    {
        1 => "Valide",
        _ => "Saisi"
    };

    private static string GetDocumentTypePrefix(int typeDocument) => typeDocument switch
    {
        1 => "ENT",
        2 => "SOR",
        3 => "TRF",
        4 => "INV",
        _ => "DOC"
    };

    private sealed record DocumentStockLineInput(
        Guid ArticleId,
        string ArticleCode,
        string Designation,
        Guid? EmplacementSourceId,
        string? EmplacementSourceCode,
        Guid? EmplacementDestinationId,
        string? EmplacementDestinationCode,
        int Quantite,
        decimal ValeurUnitaire,
        decimal ValeurTotale,
        string? NumeroLot,
        string? NumeroSerie,
        string? Motif);

    private sealed record ArticleStockMeta(
        Guid Id,
        string Code,
        string Designation,
        bool SansSuiviStock,
        bool GestionNumeroDeSerie,
        decimal PrixAchat);

    private static async Task<ArticleStockMeta?> GetArticleStockMetaAsync(SqlConnection conn, Guid articleId, SqlTransaction? tx = null)
    {
        await using var cmd = new SqlCommand(@"
            SELECT
                Id,
                ISNULL(Code,''),
                ISNULL(Designation,''),
                ISNULL(SansSuiviStock,0),
                ISNULL(GestionNumeroDeSerie,0),
                ISNULL(PrixAchat,0)
            FROM Articles
            WHERE Id = @id", conn, tx);
        cmd.Parameters.AddWithValue("@id", articleId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new ArticleStockMeta(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetBoolean(3),
            reader.GetBoolean(4),
            reader.GetDecimal(5));
    }

    private static async Task<string?> GetEmplacementCodeAsync(SqlConnection conn, Guid emplacementId, SqlTransaction? tx = null)
    {
        await using var cmd = new SqlCommand("SELECT ISNULL(Code,'') FROM Emplacements WHERE Id = @id", conn, tx);
        cmd.Parameters.AddWithValue("@id", emplacementId);
        return Convert.ToString(await cmd.ExecuteScalarAsync());
    }

    private static string BuildResumeArticles(IEnumerable<LigneDocumentStockDto> lignes)
    {
        var articles = lignes
            .Select(l => l.Designation)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return articles.Count switch
        {
            0 => "Aucun article",
            <= 3 => string.Join(", ", articles),
            _ => string.Join(", ", articles.Take(3)) + $" +{articles.Count - 3}"
        };
    }

    private static async Task<string> NextDocumentStockNumeroAsync(SqlConnection conn, SqlTransaction tx, int typeDocument, DateTime dateDocument)
    {
        var prefix = GetDocumentTypePrefix(typeDocument);
        await using var cmd = new SqlCommand(@"
            SELECT COUNT(1)
            FROM DocumentsStock
            WHERE TypeDocument = @type
              AND YEAR(DateDocument) = @annee", conn, tx);
        cmd.Parameters.AddWithValue("@type", typeDocument);
        cmd.Parameters.AddWithValue("@annee", dateDocument.Year);
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
        return $"{prefix}-{dateDocument:yyyy}-{count + 1:000000}";
    }

    private static async Task<Guid> CreateDocumentStockAsync(
        SqlConnection conn,
        SqlTransaction tx,
        int typeDocument,
        DateTime dateDocument,
        string? reference,
        string? motif,
        string createdBy,
        IReadOnlyList<DocumentStockLineInput> lignes)
    {
        var documentId = Guid.NewGuid();
        var numero = await NextDocumentStockNumeroAsync(conn, tx, typeDocument, dateDocument);
        var quantiteTotale = lignes.Sum(l => Math.Abs(l.Quantite));
        var valeurTotale = lignes.Sum(l => l.ValeurTotale);

        await using (var docCmd = new SqlCommand(@"
            INSERT INTO DocumentsStock
            (Id, Numero, TypeDocument, Reference, DateDocument, NombreLignes, QuantiteTotale, ValeurTotale, Motif, CreatedAt, CreatedBy)
            VALUES
            (@id, @numero, @type, @reference, @date, @nb, @qte, @valeur, @motif, GETUTCDATE(), @user)", conn, tx))
        {
            docCmd.Parameters.AddWithValue("@id", documentId);
            docCmd.Parameters.AddWithValue("@numero", numero);
            docCmd.Parameters.AddWithValue("@type", typeDocument);
            docCmd.Parameters.AddWithValue("@reference", (object?)reference ?? DBNull.Value);
            docCmd.Parameters.AddWithValue("@date", dateDocument);
            docCmd.Parameters.AddWithValue("@nb", lignes.Count);
            docCmd.Parameters.AddWithValue("@qte", quantiteTotale);
            docCmd.Parameters.AddWithValue("@valeur", valeurTotale);
            docCmd.Parameters.AddWithValue("@motif", (object?)motif ?? DBNull.Value);
            docCmd.Parameters.AddWithValue("@user", createdBy);
            await docCmd.ExecuteNonQueryAsync();
        }

        for (var i = 0; i < lignes.Count; i++)
        {
            var ligne = lignes[i];
            await using var ligneCmd = new SqlCommand(@"
                INSERT INTO LignesDocumentsStock
                (Id, DocumentId, ArticleId, ArticleCode, Designation, EmplacementSourceId, EmplacementSourceCode,
                 EmplacementDestinationId, EmplacementDestinationCode, Quantite, ValeurUnitaire, ValeurTotale,
                 NumeroLot, NumeroSerie, Motif, Ordre, CreatedAt)
                VALUES
                (@id, @documentId, @articleId, @articleCode, @designation, @srcId, @srcCode,
                 @dstId, @dstCode, @qte, @pu, @pt, @lot, @serie, @motif, @ordre, GETUTCDATE())", conn, tx);
            ligneCmd.Parameters.AddWithValue("@id", Guid.NewGuid());
            ligneCmd.Parameters.AddWithValue("@documentId", documentId);
            ligneCmd.Parameters.AddWithValue("@articleId", ligne.ArticleId);
            ligneCmd.Parameters.AddWithValue("@articleCode", ligne.ArticleCode);
            ligneCmd.Parameters.AddWithValue("@designation", ligne.Designation);
            ligneCmd.Parameters.AddWithValue("@srcId", (object?)ligne.EmplacementSourceId ?? DBNull.Value);
            ligneCmd.Parameters.AddWithValue("@srcCode", (object?)ligne.EmplacementSourceCode ?? DBNull.Value);
            ligneCmd.Parameters.AddWithValue("@dstId", (object?)ligne.EmplacementDestinationId ?? DBNull.Value);
            ligneCmd.Parameters.AddWithValue("@dstCode", (object?)ligne.EmplacementDestinationCode ?? DBNull.Value);
            ligneCmd.Parameters.AddWithValue("@qte", ligne.Quantite);
            ligneCmd.Parameters.AddWithValue("@pu", ligne.ValeurUnitaire);
            ligneCmd.Parameters.AddWithValue("@pt", ligne.ValeurTotale);
            ligneCmd.Parameters.AddWithValue("@lot", (object?)ligne.NumeroLot ?? DBNull.Value);
            ligneCmd.Parameters.AddWithValue("@serie", (object?)ligne.NumeroSerie ?? DBNull.Value);
            ligneCmd.Parameters.AddWithValue("@motif", (object?)ligne.Motif ?? DBNull.Value);
            ligneCmd.Parameters.AddWithValue("@ordre", i + 1);
            await ligneCmd.ExecuteNonQueryAsync();
        }

        return documentId;
    }

    [HttpGet("documents")]
    [Authorize(Policy = "Lecteur")]
    public async Task<IActionResult> GetDocuments(
        [FromQuery] DateTime? du,
        [FromQuery] DateTime? au,
        [FromQuery] int? type,
        [FromQuery] string? q)
    {
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            await EnsureStockDocumentTablesAsync(conn);

            var where = new List<string>
            {
                "d.DateDocument >= @du",
                "d.DateDocument <= @au"
            };

            if (type.HasValue && type.Value > 0)
                where.Add("d.TypeDocument = @type");

            if (!string.IsNullOrWhiteSpace(q))
            {
                where.Add(@"(
                    d.Numero LIKE @q OR
                    ISNULL(d.Reference,'') LIKE @q OR
                    ISNULL(d.Motif,'') LIKE @q OR
                    ISNULL(d.CreatedBy,'') LIKE @q OR
                    EXISTS (
                        SELECT 1
                        FROM LignesDocumentsStock l
                        WHERE l.DocumentId = d.Id
                          AND (l.ArticleCode LIKE @q OR l.Designation LIKE @q OR ISNULL(l.NumeroLot,'') LIKE @q OR ISNULL(l.NumeroSerie,'') LIKE @q)
                    )
                )");
            }

            var sql = $@"
                SELECT
                    d.Id,
                    d.Numero,
                    d.TypeDocument,
                    d.Reference,
                    d.DateDocument,
                    ISNULL(d.Statut,0),
                    d.NombreLignes,
                    d.QuantiteTotale,
                    d.ValeurTotale,
                    d.CreatedBy,
                    d.ValidatedAt,
                    d.ValidatedBy,
                    d.Motif
                FROM DocumentsStock d
                WHERE {string.Join(" AND ", where)}
                ORDER BY d.DateDocument DESC, d.Numero DESC";

            var documents = new List<DocumentStockDto>();
            var ids = new List<Guid>();

            await using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@du", du ?? DateTime.Today.AddDays(-90));
                cmd.Parameters.AddWithValue("@au", au ?? DateTime.Today.AddDays(1));
                if (type.HasValue && type.Value > 0)
                    cmd.Parameters.AddWithValue("@type", type.Value);
                if (!string.IsNullOrWhiteSpace(q))
                    cmd.Parameters.AddWithValue("@q", $"%{q.Trim()}%");

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var id = reader.GetGuid(0);
                    ids.Add(id);
                    documents.Add(new DocumentStockDto(
                        id,
                        reader.GetString(1),
                        reader.GetInt32(2),
                        GetDocumentTypeLabel(reader.GetInt32(2)),
                        reader.IsDBNull(3) ? null : reader.GetString(3),
                        reader.GetDateTime(4),
                        reader.GetInt32(5),
                        GetDocumentStatusLabel(reader.GetInt32(5)),
                        reader.GetInt32(6),
                        reader.GetInt32(7),
                        reader.GetDecimal(8),
                        reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                        reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                        reader.IsDBNull(11) ? null : reader.GetString(11),
                        reader.IsDBNull(12) ? null : reader.GetString(12),
                        string.Empty,
                        new List<LigneDocumentStockDto>()));
                }
            }

            if (ids.Count == 0)
                return Ok(documents);

            var linesByDoc = new Dictionary<Guid, List<LigneDocumentStockDto>>();
            var idsSql = string.Join(",", ids.Select((_, i) => $"@id{i}"));
            await using (var linesCmd = new SqlCommand($@"
                SELECT
                    Id, DocumentId, ArticleId, ArticleCode, Designation,
                    EmplacementSourceId, EmplacementSourceCode,
                    EmplacementDestinationId, EmplacementDestinationCode,
                    Quantite, ValeurUnitaire, ValeurTotale,
                    NumeroLot, NumeroSerie, Motif, Ordre
                FROM LignesDocumentsStock
                WHERE DocumentId IN ({idsSql})
                ORDER BY DocumentId, Ordre", conn))
            {
                for (var i = 0; i < ids.Count; i++)
                    linesCmd.Parameters.AddWithValue($"@id{i}", ids[i]);

                await using var reader = await linesCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var documentId = reader.GetGuid(1);
                    if (!linesByDoc.TryGetValue(documentId, out var lines))
                    {
                        lines = new List<LigneDocumentStockDto>();
                        linesByDoc[documentId] = lines;
                    }

                    lines.Add(new LigneDocumentStockDto(
                        reader.GetGuid(0),
                        reader.GetGuid(2),
                        reader.GetString(3),
                        reader.GetString(4),
                        reader.IsDBNull(5) ? null : reader.GetGuid(5),
                        reader.IsDBNull(6) ? null : reader.GetString(6),
                        reader.IsDBNull(7) ? null : reader.GetGuid(7),
                        reader.IsDBNull(8) ? null : reader.GetString(8),
                        reader.GetInt32(9),
                        reader.GetDecimal(10),
                        reader.GetDecimal(11),
                        reader.IsDBNull(12) ? null : reader.GetString(12),
                        reader.IsDBNull(13) ? null : reader.GetString(13),
                        reader.IsDBNull(14) ? null : reader.GetString(14),
                        reader.GetInt32(15)));
                }
            }

            var hydrated = documents
                .Select(d =>
                {
                    var lignes = linesByDoc.TryGetValue(d.Id, out var docLines) ? docLines : new List<LigneDocumentStockDto>();
                    return d with
                    {
                        ResumeArticles = BuildResumeArticles(lignes),
                        Lignes = lignes
                    };
                })
                .ToList();

            return Ok(hydrated);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { succes = false, message = ex.Message });
        }
    }

    [HttpGet("documents/{id:guid}")]
    [Authorize(Policy = "Lecteur")]
    public async Task<IActionResult> GetDocument(Guid id)
    {
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            await EnsureStockDocumentTablesAsync(conn);

            DocumentStockDto? document = null;
            await using (var cmd = new SqlCommand(@"
                SELECT
                    Id, Numero, TypeDocument, Reference, DateDocument,
                    ISNULL(Statut,0), NombreLignes, QuantiteTotale, ValeurTotale, CreatedBy, ValidatedAt, ValidatedBy, Motif
                FROM DocumentsStock
                WHERE Id = @id", conn))
            {
                cmd.Parameters.AddWithValue("@id", id);
                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var typeDocument = reader.GetInt32(2);
                    document = new DocumentStockDto(
                        reader.GetGuid(0),
                        reader.GetString(1),
                        typeDocument,
                        GetDocumentTypeLabel(typeDocument),
                        reader.IsDBNull(3) ? null : reader.GetString(3),
                        reader.GetDateTime(4),
                        reader.GetInt32(5),
                        GetDocumentStatusLabel(reader.GetInt32(5)),
                        reader.GetInt32(6),
                        reader.GetInt32(7),
                        reader.GetDecimal(8),
                        reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                        reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                        reader.IsDBNull(11) ? null : reader.GetString(11),
                        reader.IsDBNull(12) ? null : reader.GetString(12),
                        string.Empty,
                        new List<LigneDocumentStockDto>());
                }
            }

            if (document is null)
                return NotFound(new { succes = false, message = "Document de stock introuvable." });

            var lignes = new List<LigneDocumentStockDto>();
            await using (var cmd = new SqlCommand(@"
                SELECT
                    Id, DocumentId, ArticleId, ArticleCode, Designation,
                    EmplacementSourceId, EmplacementSourceCode,
                    EmplacementDestinationId, EmplacementDestinationCode,
                    Quantite, ValeurUnitaire, ValeurTotale,
                    NumeroLot, NumeroSerie, Motif, Ordre
                FROM LignesDocumentsStock
                WHERE DocumentId = @id
                ORDER BY Ordre", conn))
            {
                cmd.Parameters.AddWithValue("@id", id);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    lignes.Add(new LigneDocumentStockDto(
                        reader.GetGuid(0),
                        reader.GetGuid(2),
                        reader.GetString(3),
                        reader.GetString(4),
                        reader.IsDBNull(5) ? null : reader.GetGuid(5),
                        reader.IsDBNull(6) ? null : reader.GetString(6),
                        reader.IsDBNull(7) ? null : reader.GetGuid(7),
                        reader.IsDBNull(8) ? null : reader.GetString(8),
                        reader.GetInt32(9),
                        reader.GetDecimal(10),
                        reader.GetDecimal(11),
                        reader.IsDBNull(12) ? null : reader.GetString(12),
                        reader.IsDBNull(13) ? null : reader.GetString(13),
                        reader.IsDBNull(14) ? null : reader.GetString(14),
                        reader.GetInt32(15)));
                }
            }

            return Ok(document with
            {
                ResumeArticles = BuildResumeArticles(lignes),
                Lignes = lignes
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { succes = false, message = ex.Message });
        }
    }

    [HttpPost("documents/{id:guid}/valider")]
    [Authorize(Policy = "Magasinier")]
    public async Task<IActionResult> ValiderDocument(Guid id)
    {
        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            await EnsureStockDocumentTablesAsync(conn);

            await using var cmd = new SqlCommand(@"
                UPDATE DocumentsStock
                SET Statut = 1,
                    ValidatedAt = GETUTCDATE(),
                    ValidatedBy = @user
                WHERE Id = @id
                  AND ISNULL(Statut,0) <> 1", conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@user", UserId);

            var rows = await cmd.ExecuteNonQueryAsync();
            if (rows == 0)
                return BadRequest(new { succes = false, message = "Le document est introuvable ou deja valide." });

            return Ok(new { succes = true, message = "Document de stock valide." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { succes = false, message = ex.Message });
        }
    }

    [HttpPost("documents/entree")]
    [Authorize(Policy = "Magasinier")]
    public async Task<IActionResult> CreerDocumentEntree([FromBody] CreerDocumentStockEntreeDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Reference))
            return BadRequest(new { succes = false, message = "La reference du document est obligatoire." });

        if (dto.Lignes is null || dto.Lignes.Count == 0)
            return BadRequest(new { succes = false, message = "Ajoutez au moins une ligne au document." });

        if (dto.Lignes.Any(l => l.ArticleId == Guid.Empty || l.EmplacementId == Guid.Empty || l.Quantite <= 0))
            return BadRequest(new { succes = false, message = "Chaque ligne doit contenir un article, un emplacement et une quantite valide." });

        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            await EnsureLegacyStockDefaultsAsync(conn);
            await EnsureStockDocumentTablesAsync(conn);
            var (stockTable, _, _) = await GetSchemaAsync(conn);

            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();
            var insertSql = await BuildStockInsertSqlAsync(conn, stockTable, "@empId", tx);
            var movementSchema = await GetMovementInsertSchemaAsync(conn, tx);
            var documentLines = new List<DocumentStockLineInput>();

            foreach (var ligne in dto.Lignes)
            {
                var article = await GetArticleStockMetaAsync(conn, ligne.ArticleId, tx);
                if (article is null)
                    return BadRequest(new { succes = false, message = "Un article de ligne est introuvable." });

                if (article.SansSuiviStock)
                    return BadRequest(new { succes = false, message = $"L'article {article.Code} est configure sans suivi de stock." });

                if (article.GestionNumeroDeSerie)
                {
                    if (string.IsNullOrWhiteSpace(ligne.NumeroSerie))
                        return BadRequest(new { succes = false, message = $"Le numero de serie est obligatoire pour l'article {article.Code}." });

                    if (ligne.Quantite != 1)
                        return BadRequest(new { succes = false, message = $"L'article serialize {article.Code} doit avoir une quantite egale a 1." });
                }

                var emplacementCode = await GetEmplacementCodeAsync(conn, ligne.EmplacementId, tx) ?? string.Empty;

                await using (var upsertCmd = new SqlCommand($@"
                    IF EXISTS (SELECT 1 FROM {stockTable} WHERE ArticleId=@artId AND EmplacementId=@empId)
                        UPDATE {stockTable}
                        SET QuantiteDisponible = QuantiteDisponible + @qte
                        WHERE ArticleId=@artId AND EmplacementId=@empId
                    ELSE
                        {insertSql}", conn, tx))
                {
                    upsertCmd.Parameters.AddWithValue("@artId", ligne.ArticleId);
                    upsertCmd.Parameters.AddWithValue("@empId", ligne.EmplacementId);
                    upsertCmd.Parameters.AddWithValue("@qte", ligne.Quantite);
                    upsertCmd.Parameters.AddWithValue("@createdBy", UserId);
                    await upsertCmd.ExecuteNonQueryAsync();
                }

                await using (var mvtCmd = new SqlCommand(
                    BuildMovementInsertSql(movementSchema, 1, includeDestination: false), conn, tx))
                {
                    mvtCmd.Parameters.AddWithValue("@artId", ligne.ArticleId);
                    mvtCmd.Parameters.AddWithValue("@src", ligne.EmplacementId);
                    mvtCmd.Parameters.AddWithValue("@qte", ligne.Quantite);
                    mvtCmd.Parameters.AddWithValue("@prix", ligne.PrixUnitaire);
                    mvtCmd.Parameters.AddWithValue("@ref", dto.Reference);
                    mvtCmd.Parameters.AddWithValue("@motif", (object?)(ligne.Motif ?? dto.Motif) ?? DBNull.Value);
                    mvtCmd.Parameters.AddWithValue("@lot", (object?)ligne.NumeroLot ?? DBNull.Value);
                    mvtCmd.Parameters.AddWithValue("@serie", (object?)ligne.NumeroSerie ?? DBNull.Value);
                    mvtCmd.Parameters.AddWithValue("@user", UserId);
                    await mvtCmd.ExecuteNonQueryAsync();
                }

                documentLines.Add(new DocumentStockLineInput(
                    article.Id,
                    article.Code,
                    article.Designation,
                    null,
                    null,
                    ligne.EmplacementId,
                    emplacementCode,
                    ligne.Quantite,
                    ligne.PrixUnitaire,
                    ligne.Quantite * ligne.PrixUnitaire,
                    ligne.NumeroLot,
                    ligne.NumeroSerie,
                    ligne.Motif ?? dto.Motif));
            }

            var documentId = await CreateDocumentStockAsync(
                conn,
                tx,
                1,
                dto.DateDocument == default ? DateTime.UtcNow : dto.DateDocument,
                dto.Reference,
                dto.Motif,
                UserId,
                documentLines);

            await tx.CommitAsync();

            return Ok(new { succes = true, message = "Document d'entree enregistre.", data = documentId });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { succes = false, message = ex.Message });
        }
    }

    [HttpPost("documents/sortie")]
    [Authorize(Policy = "Magasinier")]
    public async Task<IActionResult> CreerDocumentSortie([FromBody] CreerDocumentStockSortieDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Reference))
            return BadRequest(new { succes = false, message = "La reference du document est obligatoire." });

        if (dto.Lignes is null || dto.Lignes.Count == 0)
            return BadRequest(new { succes = false, message = "Ajoutez au moins une ligne au document." });

        if (dto.Lignes.Any(l => l.ArticleId == Guid.Empty || l.EmplacementId == Guid.Empty || l.Quantite <= 0))
            return BadRequest(new { succes = false, message = "Chaque ligne doit contenir un article, un emplacement et une quantite valide." });

        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            await EnsureLegacyStockDefaultsAsync(conn);
            await EnsureStockDocumentTablesAsync(conn);
            var (stockTable, _, _) = await GetSchemaAsync(conn);
            var autoriserStockNegatif = await IsStockNegatifAutoriseAsync(conn);

            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();
            var insertSql = (await BuildStockInsertSqlAsync(conn, stockTable, "@empId", tx)).Replace("@qte", "@qteInsert");
            var movementSchema = await GetMovementInsertSchemaAsync(conn, tx);
            var documentLines = new List<DocumentStockLineInput>();

            foreach (var ligne in dto.Lignes)
            {
                var article = await GetArticleStockMetaAsync(conn, ligne.ArticleId, tx);
                if (article is null)
                    return BadRequest(new { succes = false, message = "Un article du document est introuvable." });

                if (article.SansSuiviStock)
                    return BadRequest(new { succes = false, message = $"L'article {article.Code} est configure sans suivi de stock." });

                if (article.GestionNumeroDeSerie)
                {
                    if (ligne.Quantite != 1)
                        return BadRequest(new { succes = false, message = $"L'article serialize {article.Code} doit avoir une quantite egale a 1." });

                    if (string.IsNullOrWhiteSpace(ligne.NumeroSerie))
                        return BadRequest(new { succes = false, message = $"Le numero de serie est obligatoire pour l'article {article.Code}." });
                }

                await using (var chkCmd = new SqlCommand(
                    $"SELECT ISNULL(SUM(QuantiteDisponible),0) FROM {stockTable} WHERE ArticleId=@artId AND EmplacementId=@empId", conn, tx))
                {
                    chkCmd.Parameters.AddWithValue("@artId", ligne.ArticleId);
                    chkCmd.Parameters.AddWithValue("@empId", ligne.EmplacementId);
                    var qteActuelle = Convert.ToDecimal(await chkCmd.ExecuteScalarAsync() ?? 0m);

                    if (!autoriserStockNegatif && qteActuelle < ligne.Quantite)
                        return BadRequest(new { succes = false, message = $"Stock insuffisant pour {article.Designation} : {qteActuelle} disponible(s)." });
                }

                var emplacementCode = await GetEmplacementCodeAsync(conn, ligne.EmplacementId, tx) ?? string.Empty;

                await using (var updCmd = new SqlCommand($@"
                    IF EXISTS (SELECT 1 FROM {stockTable} WHERE ArticleId=@artId AND EmplacementId=@empId)
                        UPDATE {stockTable}
                        SET QuantiteDisponible = QuantiteDisponible - @qte
                        WHERE ArticleId=@artId AND EmplacementId=@empId
                    ELSE
                        {insertSql}", conn, tx))
                {
                    updCmd.Parameters.AddWithValue("@artId", ligne.ArticleId);
                    updCmd.Parameters.AddWithValue("@empId", ligne.EmplacementId);
                    updCmd.Parameters.AddWithValue("@qte", ligne.Quantite);
                    updCmd.Parameters.AddWithValue("@qteInsert", -ligne.Quantite);
                    updCmd.Parameters.AddWithValue("@createdBy", UserId);
                    await updCmd.ExecuteNonQueryAsync();
                }

                await using (var mvtCmd = new SqlCommand(
                    BuildMovementInsertSql(movementSchema, 2, includeDestination: false), conn, tx))
                {
                    mvtCmd.Parameters.AddWithValue("@artId", ligne.ArticleId);
                    mvtCmd.Parameters.AddWithValue("@src", ligne.EmplacementId);
                    mvtCmd.Parameters.AddWithValue("@qte", ligne.Quantite);
                    mvtCmd.Parameters.AddWithValue("@prix", ligne.PrixUnitaire);
                    mvtCmd.Parameters.AddWithValue("@ref", dto.Reference);
                    mvtCmd.Parameters.AddWithValue("@motif", (object?)(ligne.Motif ?? dto.Motif) ?? DBNull.Value);
                    mvtCmd.Parameters.AddWithValue("@lot", (object?)ligne.NumeroLot ?? DBNull.Value);
                    mvtCmd.Parameters.AddWithValue("@serie", (object?)ligne.NumeroSerie ?? DBNull.Value);
                    mvtCmd.Parameters.AddWithValue("@user", UserId);
                    await mvtCmd.ExecuteNonQueryAsync();
                }

                documentLines.Add(new DocumentStockLineInput(
                    article.Id,
                    article.Code,
                    article.Designation,
                    ligne.EmplacementId,
                    emplacementCode,
                    null,
                    null,
                    ligne.Quantite,
                    ligne.PrixUnitaire,
                    ligne.Quantite * ligne.PrixUnitaire,
                    ligne.NumeroLot,
                    ligne.NumeroSerie,
                    ligne.Motif ?? dto.Motif));
            }

            var documentId = await CreateDocumentStockAsync(
                conn,
                tx,
                2,
                dto.DateDocument == default ? DateTime.UtcNow : dto.DateDocument,
                dto.Reference,
                dto.Motif,
                UserId,
                documentLines);

            await tx.CommitAsync();

            return Ok(new { succes = true, message = "Document de sortie enregistre.", data = documentId });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { succes = false, message = ex.Message });
        }
    }

    [HttpPost("documents/transfert")]
    [Authorize(Policy = "Magasinier")]
    public async Task<IActionResult> CreerDocumentTransfert([FromBody] CreerDocumentStockTransfertDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Reference))
            return BadRequest(new { succes = false, message = "La reference du document est obligatoire." });

        if (dto.EmplacementSourceId == Guid.Empty || dto.EmplacementDestinationId == Guid.Empty)
            return BadRequest(new { succes = false, message = "Les emplacements source et destination sont obligatoires." });

        if (dto.EmplacementSourceId == dto.EmplacementDestinationId)
            return BadRequest(new { succes = false, message = "Les emplacements source et destination doivent etre differents." });

        if (dto.Lignes is null || dto.Lignes.Count == 0)
            return BadRequest(new { succes = false, message = "Ajoutez au moins une ligne au document." });

        if (dto.Lignes.Any(l => l.ArticleId == Guid.Empty || l.Quantite <= 0))
            return BadRequest(new { succes = false, message = "Chaque ligne doit contenir un article et une quantite valide." });

        try
        {
            await using var conn = new SqlConnection(ConnStr);
            await conn.OpenAsync();
            await EnsureLegacyStockDefaultsAsync(conn);
            await EnsureStockDocumentTablesAsync(conn);
            var (stockTable, _, _) = await GetSchemaAsync(conn);
            var autoriserStockNegatif = await IsStockNegatifAutoriseAsync(conn);

            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();
            var emplacementSourceCode = await GetEmplacementCodeAsync(conn, dto.EmplacementSourceId, tx) ?? string.Empty;
            var emplacementDestinationCode = await GetEmplacementCodeAsync(conn, dto.EmplacementDestinationId, tx) ?? string.Empty;
            var destinationInsertSql = await BuildStockInsertSqlAsync(conn, stockTable, "@dst", tx);
            var movementSchema = await GetMovementInsertSchemaAsync(conn, tx);
            var documentLines = new List<DocumentStockLineInput>();
            var cumulsSource = new Dictionary<Guid, int>();
            var numerosSerie = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var ligne in dto.Lignes)
            {
                var article = await GetArticleStockMetaAsync(conn, ligne.ArticleId, tx);
                if (article is null)
                    return BadRequest(new { succes = false, message = "Un article du document est introuvable." });

                if (article.SansSuiviStock)
                    return BadRequest(new { succes = false, message = $"L'article {article.Code} est configure sans suivi de stock." });

                if (article.GestionNumeroDeSerie)
                {
                    if (ligne.Quantite != 1)
                        return BadRequest(new { succes = false, message = $"L'article serialize {article.Code} doit avoir une quantite egale a 1." });

                    if (string.IsNullOrWhiteSpace(ligne.NumeroSerie))
                        return BadRequest(new { succes = false, message = $"Le numero de serie est obligatoire pour l'article {article.Code}." });

                    if (!numerosSerie.Add(ligne.NumeroSerie.Trim()))
                        return BadRequest(new { succes = false, message = $"Le numero de serie {ligne.NumeroSerie} est duplique dans le document." });
                }

                cumulsSource.TryGetValue(ligne.ArticleId, out var cumulActuel);
                var nouveauCumul = cumulActuel + ligne.Quantite;
                cumulsSource[ligne.ArticleId] = nouveauCumul;

                if (!autoriserStockNegatif)
                {
                    await using var chkCmd = new SqlCommand(
                        $"SELECT ISNULL(SUM(QuantiteDisponible),0) FROM {stockTable} WHERE ArticleId=@artId AND EmplacementId=@src", conn, tx);
                    chkCmd.Parameters.AddWithValue("@artId", ligne.ArticleId);
                    chkCmd.Parameters.AddWithValue("@src", dto.EmplacementSourceId);
                    var qteSource = Convert.ToDecimal(await chkCmd.ExecuteScalarAsync() ?? 0m);
                    if (qteSource < nouveauCumul)
                        return BadRequest(new { succes = false, message = $"Stock insuffisant sur l'emplacement source pour {article.Designation} : {qteSource} disponible(s)." });
                }

                await using (var decCmd = new SqlCommand(
                    $"UPDATE {stockTable} SET QuantiteDisponible = QuantiteDisponible - @qte WHERE ArticleId=@artId AND EmplacementId=@src", conn, tx))
                {
                    decCmd.Parameters.AddWithValue("@artId", ligne.ArticleId);
                    decCmd.Parameters.AddWithValue("@src", dto.EmplacementSourceId);
                    decCmd.Parameters.AddWithValue("@qte", ligne.Quantite);
                    await decCmd.ExecuteNonQueryAsync();
                }

                await using (var incCmd = new SqlCommand($@"
                    IF EXISTS (SELECT 1 FROM {stockTable} WHERE ArticleId=@artId AND EmplacementId=@dst)
                        UPDATE {stockTable}
                        SET QuantiteDisponible = QuantiteDisponible + @qte
                        WHERE ArticleId=@artId AND EmplacementId=@dst
                    ELSE
                        {destinationInsertSql}", conn, tx))
                {
                    incCmd.Parameters.AddWithValue("@artId", ligne.ArticleId);
                    incCmd.Parameters.AddWithValue("@dst", dto.EmplacementDestinationId);
                    incCmd.Parameters.AddWithValue("@qte", ligne.Quantite);
                    incCmd.Parameters.AddWithValue("@createdBy", UserId);
                    await incCmd.ExecuteNonQueryAsync();
                }

                await using (var mvtCmd = new SqlCommand(
                    BuildMovementInsertSql(movementSchema, 3, includeDestination: true), conn, tx))
                {
                    mvtCmd.Parameters.AddWithValue("@artId", ligne.ArticleId);
                    mvtCmd.Parameters.AddWithValue("@src", dto.EmplacementSourceId);
                    mvtCmd.Parameters.AddWithValue("@dst", dto.EmplacementDestinationId);
                    mvtCmd.Parameters.AddWithValue("@qte", ligne.Quantite);
                    mvtCmd.Parameters.AddWithValue("@prix", ligne.PrixUnitaire);
                    mvtCmd.Parameters.AddWithValue("@ref", dto.Reference);
                    mvtCmd.Parameters.AddWithValue("@motif", (object?)(ligne.Motif ?? dto.Motif) ?? DBNull.Value);
                    mvtCmd.Parameters.AddWithValue("@lot", (object?)ligne.NumeroLot ?? DBNull.Value);
                    mvtCmd.Parameters.AddWithValue("@serie", (object?)ligne.NumeroSerie ?? DBNull.Value);
                    mvtCmd.Parameters.AddWithValue("@user", UserId);
                    await mvtCmd.ExecuteNonQueryAsync();
                }

                documentLines.Add(new DocumentStockLineInput(
                    article.Id,
                    article.Code,
                    article.Designation,
                    dto.EmplacementSourceId,
                    emplacementSourceCode,
                    dto.EmplacementDestinationId,
                    emplacementDestinationCode,
                    ligne.Quantite,
                    ligne.PrixUnitaire,
                    ligne.Quantite * ligne.PrixUnitaire,
                    ligne.NumeroLot,
                    ligne.NumeroSerie,
                    ligne.Motif ?? dto.Motif));
            }

            var motifDocument = string.IsNullOrWhiteSpace(dto.Demandeur)
                ? dto.Motif
                : string.IsNullOrWhiteSpace(dto.Motif)
                    ? $"Demandeur: {dto.Demandeur}"
                    : $"Demandeur: {dto.Demandeur}{Environment.NewLine}{dto.Motif}";

            var documentId = await CreateDocumentStockAsync(
                conn,
                tx,
                3,
                dto.DateDocument == default ? DateTime.UtcNow : dto.DateDocument,
                dto.Reference,
                motifDocument,
                UserId,
                documentLines);

            await tx.CommitAsync();

            return Ok(new { succes = true, message = "Document de transfert enregistre.", data = documentId });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { succes = false, message = ex.Message });
        }
    }

    private static async Task<string> BuildStockInsertSqlAsync(SqlConnection conn, string stockTable, string emplacementParamName, SqlTransaction? tx = null)
    {
        if (!stockTable.Equals("Stocks", StringComparison.OrdinalIgnoreCase))
        {
            return $@"INSERT INTO {stockTable} (Id,ArticleId,EmplacementId,QuantiteDisponible,QuantiteReservee)
                VALUES (NEWID(),@artId,{emplacementParamName},@qte,0)";
        }

        var columns = await GetTableColumnsAsync(conn, stockTable, tx);
        var insertColumns = new List<string> { "Id", "ArticleId", "EmplacementId", "QuantiteDisponible", "QuantiteReservee" };
        var insertValues = new List<string> { "NEWID()", "@artId", emplacementParamName, "@qte", "0" };

        if (columns.Contains("QuantiteEnCommande"))
        {
            insertColumns.Add("QuantiteEnCommande");
            insertValues.Add("0");
        }

        if (columns.Contains("CreatedAt"))
        {
            insertColumns.Add("CreatedAt");
            insertValues.Add("GETUTCDATE()");
        }

        if (columns.Contains("CreatedBy"))
        {
            insertColumns.Add("CreatedBy");
            insertValues.Add("@createdBy");
        }

        return $@"INSERT INTO {stockTable} ({string.Join(",", insertColumns)})
            VALUES ({string.Join(",", insertValues)})";
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
                    ISNULL(m.NumeroSerie,'') AS NumeroSerie,
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
                    numeroSerie          = r.GetString(12),
                    dateMouvement        = r.GetDateTime(13),
                    createdBy            = r.GetString(14)
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
            await EnsureLegacyStockDefaultsAsync(conn);
            await EnsureStockDocumentTablesAsync(conn);
            var (stockTable, _, _) = await GetSchemaAsync(conn);
            var article = await GetArticleStockMetaAsync(conn, dto.ArticleId);
            if (article is null)
                return BadRequest(new { succes = false, message = "Article introuvable." });

            if (article.SansSuiviStock)
                return BadRequest(new { succes = false, message = "Cet article est configure sans suivi de stock." });

            if (article.GestionNumeroDeSerie)
            {
                if (string.IsNullOrWhiteSpace(dto.NumeroSerie))
                    return BadRequest(new { succes = false, message = "Le numero de serie est obligatoire pour cet article." });

                if (dto.Quantite != 1)
                    return BadRequest(new { succes = false, message = "Un article serialize doit etre enregistre avec une quantite egale a 1." });
            }

            // Upsert stock
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();
            var emplacementCode = await GetEmplacementCodeAsync(conn, dto.EmplacementId, tx) ?? string.Empty;

            var insertSql = await BuildStockInsertSqlAsync(conn, stockTable, "@empId", tx);
            await using (var upsertCmd = new SqlCommand($@"
                IF EXISTS (SELECT 1 FROM {stockTable} WHERE ArticleId=@artId AND EmplacementId=@empId)
                    UPDATE {stockTable}
                    SET QuantiteDisponible = QuantiteDisponible + @qte
                    WHERE ArticleId=@artId AND EmplacementId=@empId
                ELSE
                    {insertSql}", conn, tx))
            {
                upsertCmd.Parameters.AddWithValue("@artId", dto.ArticleId);
                upsertCmd.Parameters.AddWithValue("@empId", dto.EmplacementId);
                upsertCmd.Parameters.AddWithValue("@qte", dto.Quantite);
                upsertCmd.Parameters.AddWithValue("@createdBy", UserId);
                await upsertCmd.ExecuteNonQueryAsync();
            }

            // Mouvement
            var movementSchema = await GetMovementInsertSchemaAsync(conn, tx);
            await using var mvtCmd = new SqlCommand(
                BuildMovementInsertSql(movementSchema, 1, includeDestination: false), conn, tx);
            mvtCmd.Parameters.AddWithValue("@artId", dto.ArticleId);
            mvtCmd.Parameters.AddWithValue("@src",   dto.EmplacementId);
            mvtCmd.Parameters.AddWithValue("@qte",   dto.Quantite);
            mvtCmd.Parameters.AddWithValue("@prix",  dto.PrixUnitaire);
            mvtCmd.Parameters.AddWithValue("@ref",   (object?)dto.Reference  ?? DBNull.Value);
            mvtCmd.Parameters.AddWithValue("@motif", (object?)dto.Motif      ?? DBNull.Value);
            mvtCmd.Parameters.AddWithValue("@lot",   (object?)dto.NumeroLot  ?? DBNull.Value);
            mvtCmd.Parameters.AddWithValue("@serie", (object?)dto.NumeroSerie ?? DBNull.Value);
            mvtCmd.Parameters.AddWithValue("@user",  UserId);
            await mvtCmd.ExecuteNonQueryAsync();

            await CreateDocumentStockAsync(
                conn,
                tx,
                1,
                DateTime.UtcNow,
                dto.Reference,
                dto.Motif,
                UserId,
                new[]
                {
                    new DocumentStockLineInput(
                        article.Id,
                        article.Code,
                        article.Designation,
                        null,
                        null,
                        dto.EmplacementId,
                        emplacementCode,
                        dto.Quantite,
                        dto.PrixUnitaire,
                        dto.Quantite * dto.PrixUnitaire,
                        dto.NumeroLot,
                        dto.NumeroSerie,
                        dto.Motif)
                });

            await tx.CommitAsync();

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
            await EnsureLegacyStockDefaultsAsync(conn);
            await EnsureStockDocumentTablesAsync(conn);
            var (stockTable, _, _) = await GetSchemaAsync(conn);
            var autoriserStockNegatif = await IsStockNegatifAutoriseAsync(conn);

            // Vérifier stock suffisant
            var article = await GetArticleStockMetaAsync(conn, dto.ArticleId);
            if (article is null)
                return BadRequest(new { succes = false, message = "Article introuvable." });

            if (article.SansSuiviStock)
                return BadRequest(new { succes = false, message = "Cet article est configure sans suivi de stock." });

            if (article.GestionNumeroDeSerie)
            {
                if (string.IsNullOrWhiteSpace(dto.NumeroSerie))
                    return BadRequest(new { succes = false, message = "Le numero de serie est obligatoire pour cet article." });

                if (dto.Quantite != 1)
                    return BadRequest(new { succes = false, message = "Un article serialize doit etre enregistre avec une quantite egale a 1." });
            }

            await using var chkCmd = new SqlCommand(
                $"SELECT ISNULL(SUM(QuantiteDisponible),0) FROM {stockTable} WHERE ArticleId=@artId AND EmplacementId=@empId", conn);
            chkCmd.Parameters.AddWithValue("@artId", dto.ArticleId);
            chkCmd.Parameters.AddWithValue("@empId", dto.EmplacementId);
            var qteActuelle = Convert.ToInt32(await chkCmd.ExecuteScalarAsync());
            if (!autoriserStockNegatif && qteActuelle < dto.Quantite)
                return BadRequest(new { succes = false, message = $"Stock insuffisant : {qteActuelle} disponible(s)." });

            // Décrémenter
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();
            var emplacementCode = await GetEmplacementCodeAsync(conn, dto.EmplacementId, tx) ?? string.Empty;

            await using (var updCmd = new SqlCommand(
                $"UPDATE {stockTable} SET QuantiteDisponible = QuantiteDisponible - @qte WHERE ArticleId=@artId AND EmplacementId=@empId", conn, tx))
            {
                updCmd.Parameters.AddWithValue("@artId", dto.ArticleId);
                updCmd.Parameters.AddWithValue("@empId", dto.EmplacementId);
                updCmd.Parameters.AddWithValue("@qte", dto.Quantite);
                await updCmd.ExecuteNonQueryAsync();
            }

            var movementSchema = await GetMovementInsertSchemaAsync(conn, tx);
            await using var mvtCmd = new SqlCommand(
                BuildMovementInsertSql(movementSchema, 2, includeDestination: false), conn, tx);
            mvtCmd.Parameters.AddWithValue("@artId", dto.ArticleId);
            mvtCmd.Parameters.AddWithValue("@src",   dto.EmplacementId);
            mvtCmd.Parameters.AddWithValue("@qte",   dto.Quantite);
            mvtCmd.Parameters.AddWithValue("@prix",  article.PrixAchat);
            mvtCmd.Parameters.AddWithValue("@ref",   (object?)dto.Reference ?? DBNull.Value);
            mvtCmd.Parameters.AddWithValue("@motif", (object?)dto.Motif     ?? DBNull.Value);
            mvtCmd.Parameters.AddWithValue("@lot",   (object?)dto.NumeroLot ?? DBNull.Value);
            mvtCmd.Parameters.AddWithValue("@serie", (object?)dto.NumeroSerie ?? DBNull.Value);
            mvtCmd.Parameters.AddWithValue("@user",  UserId);
            await mvtCmd.ExecuteNonQueryAsync();

            await CreateDocumentStockAsync(
                conn,
                tx,
                2,
                DateTime.UtcNow,
                dto.Reference,
                dto.Motif,
                UserId,
                new[]
                {
                    new DocumentStockLineInput(
                        article.Id,
                        article.Code,
                        article.Designation,
                        dto.EmplacementId,
                        emplacementCode,
                        null,
                        null,
                        dto.Quantite,
                        article.PrixAchat,
                        dto.Quantite * article.PrixAchat,
                        dto.NumeroLot,
                        dto.NumeroSerie,
                        dto.Motif)
                });

            await tx.CommitAsync();

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
            await EnsureLegacyStockDefaultsAsync(conn);
            await EnsureStockDocumentTablesAsync(conn);
            var (stockTable, _, _) = await GetSchemaAsync(conn);
            var article = await GetArticleStockMetaAsync(conn, dto.ArticleId);
            if (article is null)
                return BadRequest(new { succes = false, message = "Article introuvable." });

            if (article.SansSuiviStock)
                return BadRequest(new { succes = false, message = "Cet article est configure sans suivi de stock." });

            // Décrémenter source
            var autoriserStockNegatif = await IsStockNegatifAutoriseAsync(conn);
            if (!autoriserStockNegatif)
            {
                await using var chkCmd = new SqlCommand(
                    $"SELECT ISNULL(SUM(QuantiteDisponible),0) FROM {stockTable} WHERE ArticleId=@artId AND EmplacementId=@src", conn);
                chkCmd.Parameters.AddWithValue("@artId", dto.ArticleId);
                chkCmd.Parameters.AddWithValue("@src", dto.EmplacementSourceId);
                var qteSource = Convert.ToDecimal(await chkCmd.ExecuteScalarAsync() ?? 0m);
                if (qteSource < dto.Quantite)
                    return BadRequest(new { succes = false, message = $"Stock insuffisant sur l'emplacement source : {qteSource} disponible(s)." });
            }

            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();
            var emplacementSourceCode = await GetEmplacementCodeAsync(conn, dto.EmplacementSourceId, tx) ?? string.Empty;
            var emplacementDestinationCode = await GetEmplacementCodeAsync(conn, dto.EmplacementDestinationId, tx) ?? string.Empty;

            await using (var decCmd = new SqlCommand(
                $"UPDATE {stockTable} SET QuantiteDisponible = QuantiteDisponible - @qte WHERE ArticleId=@artId AND EmplacementId=@src", conn, tx))
            {
                decCmd.Parameters.AddWithValue("@artId", dto.ArticleId);
                decCmd.Parameters.AddWithValue("@src",   dto.EmplacementSourceId);
                decCmd.Parameters.AddWithValue("@qte",   dto.Quantite);
                await decCmd.ExecuteNonQueryAsync();
            }

            // Incrémenter destination
            var destinationInsertSql = await BuildStockInsertSqlAsync(conn, stockTable, "@dst", tx);
            await using var incCmd = new SqlCommand($@"
                IF EXISTS (SELECT 1 FROM {stockTable} WHERE ArticleId=@artId AND EmplacementId=@dst)
                    UPDATE {stockTable} SET QuantiteDisponible = QuantiteDisponible + @qte WHERE ArticleId=@artId AND EmplacementId=@dst
                ELSE
                    {destinationInsertSql}", conn, tx);
            incCmd.Parameters.AddWithValue("@artId", dto.ArticleId);
            incCmd.Parameters.AddWithValue("@dst",   dto.EmplacementDestinationId);
            incCmd.Parameters.AddWithValue("@qte",   dto.Quantite);
            incCmd.Parameters.AddWithValue("@createdBy", UserId);
            await incCmd.ExecuteNonQueryAsync();

            // Mouvement
            var movementSchema = await GetMovementInsertSchemaAsync(conn, tx);
            await using var mvtCmd = new SqlCommand(
                BuildMovementInsertSql(movementSchema, 3, includeDestination: true), conn, tx);
            mvtCmd.Parameters.AddWithValue("@artId", dto.ArticleId);
            mvtCmd.Parameters.AddWithValue("@src",   dto.EmplacementSourceId);
            mvtCmd.Parameters.AddWithValue("@dst",   dto.EmplacementDestinationId);
            mvtCmd.Parameters.AddWithValue("@qte",   dto.Quantite);
            mvtCmd.Parameters.AddWithValue("@prix",  article.PrixAchat);
            mvtCmd.Parameters.AddWithValue("@ref",   DBNull.Value);
            mvtCmd.Parameters.AddWithValue("@motif", DBNull.Value);
            mvtCmd.Parameters.AddWithValue("@lot",   (object?)dto.NumeroLot ?? DBNull.Value);
            mvtCmd.Parameters.AddWithValue("@serie", DBNull.Value);
            mvtCmd.Parameters.AddWithValue("@user",  UserId);
            await mvtCmd.ExecuteNonQueryAsync();

            await CreateDocumentStockAsync(
                conn,
                tx,
                3,
                DateTime.UtcNow,
                null,
                null,
                UserId,
                new[]
                {
                    new DocumentStockLineInput(
                        article.Id,
                        article.Code,
                        article.Designation,
                        dto.EmplacementSourceId,
                        emplacementSourceCode,
                        dto.EmplacementDestinationId,
                        emplacementDestinationCode,
                        dto.Quantite,
                        article.PrixAchat,
                        dto.Quantite * article.PrixAchat,
                        dto.NumeroLot,
                        null,
                        null)
                });

            await tx.CommitAsync();

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
            await EnsureLegacyStockDefaultsAsync(conn);
            await EnsureStockDocumentTablesAsync(conn);
            var (stockTable, _, _) = await GetSchemaAsync(conn);
            var autoriserStockNegatif = await IsStockNegatifAutoriseAsync(conn);
            var article = await GetArticleStockMetaAsync(conn, dto.ArticleId);
            if (article is null)
                return BadRequest(new { succes = false, message = "Article introuvable." });

            if (article.SansSuiviStock)
                return BadRequest(new { succes = false, message = "Cet article est configure sans suivi de stock." });

            // Lire quantité actuelle
            await using var chkCmd = new SqlCommand(
                $"SELECT ISNULL(SUM(QuantiteDisponible),0) FROM {stockTable} WHERE ArticleId=@artId AND EmplacementId=@empId", conn);
            chkCmd.Parameters.AddWithValue("@artId", dto.ArticleId);
            chkCmd.Parameters.AddWithValue("@empId", dto.EmplacementId);
            var qteActuelle = Convert.ToInt32(await chkCmd.ExecuteScalarAsync());
            if (!autoriserStockNegatif && dto.QuantiteReelle < 0)
                return BadRequest(new { succes = false, message = "Le stock negatif n'est pas autorise dans les parametres." });
            var ecart = dto.QuantiteReelle - qteActuelle;

            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();
            var emplacementCode = await GetEmplacementCodeAsync(conn, dto.EmplacementId, tx) ?? string.Empty;

            var adjustInsertSql = await BuildStockInsertSqlAsync(conn, stockTable, "@empId", tx);
            await using var updCmd = new SqlCommand($@"
                IF EXISTS (SELECT 1 FROM {stockTable} WHERE ArticleId=@artId AND EmplacementId=@empId)
                    UPDATE {stockTable} SET QuantiteDisponible=@qte WHERE ArticleId=@artId AND EmplacementId=@empId
                ELSE
                    {adjustInsertSql}", conn, tx);
            updCmd.Parameters.AddWithValue("@artId", dto.ArticleId);
            updCmd.Parameters.AddWithValue("@empId", dto.EmplacementId);
            updCmd.Parameters.AddWithValue("@qte",   dto.QuantiteReelle);
            updCmd.Parameters.AddWithValue("@createdBy", UserId);
            await updCmd.ExecuteNonQueryAsync();

            // Mouvement d'ajustement
            var movementSchema = await GetMovementInsertSchemaAsync(conn, tx);
            await using var mvtCmd = new SqlCommand(
                BuildMovementInsertSql(movementSchema, 4, includeDestination: false), conn, tx);
            mvtCmd.Parameters.AddWithValue("@artId", dto.ArticleId);
            mvtCmd.Parameters.AddWithValue("@src",   dto.EmplacementId);
            mvtCmd.Parameters.AddWithValue("@ecart", ecart);
            mvtCmd.Parameters.AddWithValue("@qte",   ecart);
            mvtCmd.Parameters.AddWithValue("@prix",  article.PrixAchat);
            mvtCmd.Parameters.AddWithValue("@ref",   DBNull.Value);
            mvtCmd.Parameters.AddWithValue("@motif", (object?)dto.Motif ?? DBNull.Value);
            mvtCmd.Parameters.AddWithValue("@lot",   DBNull.Value);
            mvtCmd.Parameters.AddWithValue("@serie", DBNull.Value);
            mvtCmd.Parameters.AddWithValue("@user",  UserId);
            await mvtCmd.ExecuteNonQueryAsync();

            await CreateDocumentStockAsync(
                conn,
                tx,
                4,
                DateTime.UtcNow,
                null,
                dto.Motif,
                UserId,
                new[]
                {
                    new DocumentStockLineInput(
                        article.Id,
                        article.Code,
                        article.Designation,
                        dto.EmplacementId,
                        emplacementCode,
                        null,
                        null,
                        ecart,
                        article.PrixAchat,
                        ecart * article.PrixAchat,
                        null,
                        null,
                        dto.Motif)
                });

            await tx.CommitAsync();

            return Ok(new { succes = true, message = $"Ajustement : {ecart:+#;-#;0} unité(s)." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { succes = false, message = ex.Message });
        }
    }
}
