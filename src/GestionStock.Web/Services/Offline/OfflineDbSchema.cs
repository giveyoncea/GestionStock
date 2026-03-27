using System.Text;

namespace GestionStock.Web.Services;

public static class OfflineDbSchema
{
    public const int SchemaVersion = 1;

    public static IReadOnlyList<string> GetCreateScripts()
    {
        return new[]
        {
            "PRAGMA foreign_keys = ON;",
            BuildSyncMetadata(),
            BuildParametresLocal(),
            BuildDepotsLocal(),
            BuildArticlesLocal(),
            BuildStocksResumeLocal(),
            BuildClientsLocal(),
            BuildFournisseursLocal(),
            BuildDocumentsVenteLocal(),
            BuildLignesVenteLocal(),
            BuildDocumentsAchatLocal(),
            BuildLignesAchatLocal(),
            BuildReglementsLocal(),
            BuildAcomptesLocal(),
            BuildMouvementsLocal(),
            BuildSyncQueue(),
            BuildSyncConflicts(),
            BuildIndexes()
        };
    }

    private static string BuildSyncMetadata() => @"
CREATE TABLE IF NOT EXISTS SyncMetadata (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    TenantCode TEXT NOT NULL,
    UserId TEXT,
    LastFullSyncUtc TEXT,
    LastDeltaSyncUtc TEXT,
    AppVersion TEXT,
    SchemaVersion INTEGER NOT NULL DEFAULT 1
);";

    private static string BuildParametresLocal() => @"
CREATE TABLE IF NOT EXISTS ParametresLocal (
    Id TEXT PRIMARY KEY,
    RaisonSociale TEXT,
    Devise TEXT NOT NULL,
    SymboleDevise TEXT NOT NULL,
    NombreDecimalesMontant INTEGER NOT NULL DEFAULT 2,
    NombreDecimalesQuantite INTEGER NOT NULL DEFAULT 3,
    FormatImprimeDocuments TEXT,
    FormatImprimeRecus TEXT,
    GabaritInterface TEXT,
    LogoBase64 TEXT,
    UpdatedAtUtc TEXT
);";

    private static string BuildDepotsLocal() => @"
CREATE TABLE IF NOT EXISTS DepotsLocal (
    Id TEXT PRIMARY KEY,
    Code TEXT,
    Nom TEXT NOT NULL,
    Adresse TEXT,
    Actif INTEGER NOT NULL DEFAULT 1,
    UpdatedAtUtc TEXT
);";

    private static string BuildArticlesLocal() => @"
CREATE TABLE IF NOT EXISTS ArticlesLocal (
    Id TEXT PRIMARY KEY,
    Code TEXT NOT NULL,
    Designation TEXT NOT NULL,
    Description TEXT,
    CodeBarres TEXT,
    Unite TEXT,
    PrixAchat REAL NOT NULL DEFAULT 0,
    PrixVente REAL NOT NULL DEFAULT 0,
    StockMinimum REAL NOT NULL DEFAULT 0,
    Actif INTEGER NOT NULL DEFAULT 1,
    UpdatedAtUtc TEXT
);";

    private static string BuildStocksResumeLocal() => @"
CREATE TABLE IF NOT EXISTS StocksResumeLocal (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ArticleId TEXT NOT NULL,
    DepotId TEXT NOT NULL,
    QuantiteDisponible REAL NOT NULL DEFAULT 0,
    QuantiteReservee REAL NOT NULL DEFAULT 0,
    QuantiteTheorique REAL NOT NULL DEFAULT 0,
    UpdatedAtUtc TEXT,
    UNIQUE (ArticleId, DepotId)
);";

    private static string BuildClientsLocal() => @"
CREATE TABLE IF NOT EXISTS ClientsLocal (
    Id TEXT PRIMARY KEY,
    Code TEXT,
    Nom TEXT NOT NULL,
    Telephone TEXT,
    Email TEXT,
    Adresse TEXT,
    Ville TEXT,
    Pays TEXT,
    PlafondCredit REAL NOT NULL DEFAULT 0,
    Actif INTEGER NOT NULL DEFAULT 1,
    UpdatedAtUtc TEXT
);";

    private static string BuildFournisseursLocal() => @"
CREATE TABLE IF NOT EXISTS FournisseursLocal (
    Id TEXT PRIMARY KEY,
    Code TEXT,
    Nom TEXT NOT NULL,
    Telephone TEXT,
    Email TEXT,
    Adresse TEXT,
    Ville TEXT,
    Pays TEXT,
    Actif INTEGER NOT NULL DEFAULT 1,
    UpdatedAtUtc TEXT
);";

    private static string BuildDocumentsVenteLocal() => @"
CREATE TABLE IF NOT EXISTS DocumentsVenteLocal (
    LocalId TEXT PRIMARY KEY,
    ServerId TEXT,
    NumeroLocal TEXT,
    NumeroServeur TEXT,
    TypeDocument INTEGER NOT NULL,
    StatutLocal TEXT NOT NULL,
    StatutServeur TEXT,
    ClientId TEXT,
    DepotId TEXT,
    DateDocument TEXT NOT NULL,
    DateEcheance TEXT,
    SousTotalHt REAL NOT NULL DEFAULT 0,
    TotalTva REAL NOT NULL DEFAULT 0,
    TotalTtc REAL NOT NULL DEFAULT 0,
    Solde REAL NOT NULL DEFAULT 0,
    Commentaire TEXT,
    IsDirty INTEGER NOT NULL DEFAULT 1,
    IsDeleted INTEGER NOT NULL DEFAULT 0,
    SyncState TEXT NOT NULL DEFAULT 'Pending',
    LastSyncUtc TEXT,
    UpdatedAtUtc TEXT NOT NULL
);";

    private static string BuildLignesVenteLocal() => @"
CREATE TABLE IF NOT EXISTS LignesVenteLocal (
    LocalId TEXT PRIMARY KEY,
    DocumentLocalId TEXT NOT NULL,
    ArticleId TEXT NOT NULL,
    Quantite REAL NOT NULL,
    PrixUnitaireHt REAL NOT NULL,
    TauxTva REAL NOT NULL DEFAULT 0,
    Remise REAL NOT NULL DEFAULT 0,
    TotalLigneTtc REAL NOT NULL DEFAULT 0,
    UpdatedAtUtc TEXT NOT NULL
);";

    private static string BuildDocumentsAchatLocal() => @"
CREATE TABLE IF NOT EXISTS DocumentsAchatLocal (
    LocalId TEXT PRIMARY KEY,
    ServerId TEXT,
    NumeroLocal TEXT,
    NumeroServeur TEXT,
    TypeDocument INTEGER NOT NULL,
    StatutLocal TEXT NOT NULL,
    StatutServeur TEXT,
    FournisseurId TEXT,
    DepotId TEXT,
    DateDocument TEXT NOT NULL,
    DateEcheance TEXT,
    SousTotalHt REAL NOT NULL DEFAULT 0,
    TotalTva REAL NOT NULL DEFAULT 0,
    TotalTtc REAL NOT NULL DEFAULT 0,
    Solde REAL NOT NULL DEFAULT 0,
    Commentaire TEXT,
    IsDirty INTEGER NOT NULL DEFAULT 1,
    IsDeleted INTEGER NOT NULL DEFAULT 0,
    SyncState TEXT NOT NULL DEFAULT 'Pending',
    LastSyncUtc TEXT,
    UpdatedAtUtc TEXT NOT NULL
);";

    private static string BuildLignesAchatLocal() => @"
CREATE TABLE IF NOT EXISTS LignesAchatLocal (
    LocalId TEXT PRIMARY KEY,
    DocumentLocalId TEXT NOT NULL,
    ArticleId TEXT NOT NULL,
    Quantite REAL NOT NULL,
    PrixUnitaireHt REAL NOT NULL,
    TauxTva REAL NOT NULL DEFAULT 0,
    Remise REAL NOT NULL DEFAULT 0,
    TotalLigneTtc REAL NOT NULL DEFAULT 0,
    UpdatedAtUtc TEXT NOT NULL
);";

    private static string BuildReglementsLocal() => @"
CREATE TABLE IF NOT EXISTS ReglementsLocal (
    LocalId TEXT PRIMARY KEY,
    ServerId TEXT,
    DocumentType TEXT NOT NULL,
    DocumentServerId TEXT,
    DocumentLocalId TEXT,
    TiersType TEXT NOT NULL,
    TiersId TEXT,
    DateReglement TEXT NOT NULL,
    Montant REAL NOT NULL,
    ModeReglement TEXT,
    Reference TEXT,
    Commentaire TEXT,
    StatutLocal TEXT NOT NULL DEFAULT 'Saisi hors ligne',
    SyncState TEXT NOT NULL DEFAULT 'Pending',
    UpdatedAtUtc TEXT NOT NULL
);";

    private static string BuildAcomptesLocal() => @"
CREATE TABLE IF NOT EXISTS AcomptesLocal (
    LocalId TEXT PRIMARY KEY,
    ServerId TEXT,
    TiersType TEXT NOT NULL,
    TiersId TEXT,
    DateAcompte TEXT NOT NULL,
    Montant REAL NOT NULL,
    ModeReglement TEXT,
    Reference TEXT,
    Commentaire TEXT,
    StatutLocal TEXT NOT NULL DEFAULT 'Saisi hors ligne',
    SyncState TEXT NOT NULL DEFAULT 'Pending',
    UpdatedAtUtc TEXT NOT NULL
);";

    private static string BuildMouvementsLocal() => @"
CREATE TABLE IF NOT EXISTS MouvementsLocal (
    LocalId TEXT PRIMARY KEY,
    ServerId TEXT,
    ArticleId TEXT NOT NULL,
    DepotId TEXT NOT NULL,
    TypeMouvement TEXT NOT NULL,
    Quantite REAL NOT NULL,
    DateMouvement TEXT NOT NULL,
    ReferenceDocument TEXT,
    Commentaire TEXT,
    SyncState TEXT NOT NULL DEFAULT 'Pending',
    UpdatedAtUtc TEXT NOT NULL
);";

    private static string BuildSyncQueue() => @"
CREATE TABLE IF NOT EXISTS SyncQueue (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    EntityType TEXT NOT NULL,
    EntityLocalId TEXT NOT NULL,
    OperationType TEXT NOT NULL,
    PayloadJson TEXT NOT NULL,
    Status TEXT NOT NULL DEFAULT 'Pending',
    RetryCount INTEGER NOT NULL DEFAULT 0,
    LastError TEXT,
    CreatedAtUtc TEXT NOT NULL,
    LastAttemptUtc TEXT
);";

    private static string BuildSyncConflicts() => @"
CREATE TABLE IF NOT EXISTS SyncConflicts (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    EntityType TEXT NOT NULL,
    EntityLocalId TEXT NOT NULL,
    ConflictType TEXT NOT NULL,
    LocalPayloadJson TEXT,
    ServerPayloadJson TEXT,
    ResolutionStatus TEXT NOT NULL DEFAULT 'Open',
    CreatedAtUtc TEXT NOT NULL,
    ResolvedAtUtc TEXT
);";

    private static string BuildIndexes()
    {
        var sb = new StringBuilder();
        sb.AppendLine("CREATE INDEX IF NOT EXISTS IX_ArticlesLocal_Code ON ArticlesLocal(Code);");
        sb.AppendLine("CREATE INDEX IF NOT EXISTS IX_ClientsLocal_Nom ON ClientsLocal(Nom);");
        sb.AppendLine("CREATE INDEX IF NOT EXISTS IX_FournisseursLocal_Nom ON FournisseursLocal(Nom);");
        sb.AppendLine("CREATE INDEX IF NOT EXISTS IX_DocumentsVenteLocal_SyncState ON DocumentsVenteLocal(SyncState);");
        sb.AppendLine("CREATE INDEX IF NOT EXISTS IX_DocumentsAchatLocal_SyncState ON DocumentsAchatLocal(SyncState);");
        sb.AppendLine("CREATE INDEX IF NOT EXISTS IX_ReglementsLocal_SyncState ON ReglementsLocal(SyncState);");
        sb.AppendLine("CREATE INDEX IF NOT EXISTS IX_AcomptesLocal_SyncState ON AcomptesLocal(SyncState);");
        sb.AppendLine("CREATE INDEX IF NOT EXISTS IX_MouvementsLocal_SyncState ON MouvementsLocal(SyncState);");
        sb.AppendLine("CREATE INDEX IF NOT EXISTS IX_SyncQueue_Status ON SyncQueue(Status);");
        return sb.ToString();
    }
}
