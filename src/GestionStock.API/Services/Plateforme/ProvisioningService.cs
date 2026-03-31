using Microsoft.Data.SqlClient;

namespace GestionStock.API.Services;

/// <summary>
/// Crée et initialise la base de données d'un nouveau tenant.
/// </summary>
public class ProvisioningService
{
    private readonly IConfiguration _config;
    private readonly ILogger<ProvisioningService> _logger;

    public ProvisioningService(IConfiguration config, ILogger<ProvisioningService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<string> ProvisionnerTenantAsync(
        string tenantCode, string adminEmail, string adminPassword,
        string adminNom, string raisonSociale,
        string devise, string symboleDevise,
        int nombreDecimalesMontant, int nombreDecimalesQuantite)
    {
        var dbName = $"GestionStock_{tenantCode.ToUpper()}";
        var masterBase = _config.GetConnectionString("SqlServerBase")!;
        var tenantConn = $"{masterBase};Database={dbName}";

        _logger.LogInformation("Provisioning tenant {Code} → DB {Db}", tenantCode, dbName);

        // 1. Créer la base de données
        await using (var conn = new SqlConnection($"{masterBase};Database=master"))
        {
            await conn.OpenAsync();
            var sql = $"IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name='{dbName}') CREATE DATABASE [{dbName}]";
            await using var cmd = new SqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        }

        // 2. Créer les tables dans la nouvelle DB
        await using (var conn = new SqlConnection(tenantConn))
        {
            await conn.OpenAsync();
            await CreerTablesAsync(conn);
            await SeedEmplacementsAsync(conn);
            await CreerAdminAsync(conn, adminEmail, adminPassword, adminNom, tenantCode);
        }

        // 3. Enregistrer les paramètres de l'entreprise
        await using (var conn = new SqlConnection(tenantConn))
        {
            await conn.OpenAsync();
            await SeedParametresAsync(conn, raisonSociale, adminEmail, devise, symboleDevise,
                nombreDecimalesMontant, nombreDecimalesQuantite);
        }

        return tenantConn;
    }

    private static async Task CreerTablesAsync(SqlConnection conn)
    {
        var tables = new[]
        {
            // ASP.NET Identity
            @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='AspNetRoles' AND xtype='U')
              CREATE TABLE AspNetRoles (Id nvarchar(450) NOT NULL PRIMARY KEY, Name nvarchar(256) NULL,
              NormalizedName nvarchar(256) NULL, ConcurrencyStamp nvarchar(max) NULL)",

            @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='AspNetUsers' AND xtype='U')
              CREATE TABLE AspNetUsers (Id nvarchar(450) NOT NULL PRIMARY KEY,
              UserName nvarchar(256) NULL, NormalizedUserName nvarchar(256) NULL,
              Email nvarchar(256) NULL, NormalizedEmail nvarchar(256) NULL,
              EmailConfirmed bit NOT NULL DEFAULT 0, PasswordHash nvarchar(max) NULL,
              SecurityStamp nvarchar(max) NULL, ConcurrencyStamp nvarchar(max) NULL,
              PhoneNumber nvarchar(max) NULL, PhoneNumberConfirmed bit NOT NULL DEFAULT 0,
              TwoFactorEnabled bit NOT NULL DEFAULT 0, TwoFactorEnabled2 bit NOT NULL DEFAULT 0,
              LockoutEnd datetimeoffset NULL,
              LockoutEnabled bit NOT NULL DEFAULT 1, AccessFailedCount int NOT NULL DEFAULT 0,
              FirstName nvarchar(100) NOT NULL DEFAULT '',
              LastName nvarchar(100) NOT NULL DEFAULT '',
              NomComplet nvarchar(200) NULL, EntrepotAssocie nvarchar(100) NULL,
              EstActif bit NOT NULL DEFAULT 1,
              TenantCode nvarchar(20) NULL,
              CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
              DerniereConnexion datetime2 NULL,
              Role int NOT NULL DEFAULT 0)",

            @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='AspNetUserRoles' AND xtype='U')
              CREATE TABLE AspNetUserRoles (UserId nvarchar(450) NOT NULL, RoleId nvarchar(450) NOT NULL,
              PRIMARY KEY (UserId, RoleId))",

            // Emplacements
            @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Emplacements' AND xtype='U')
              CREATE TABLE Emplacements (Id uniqueidentifier NOT NULL PRIMARY KEY,
              Code nvarchar(50) NOT NULL, Zone nvarchar(50) NOT NULL DEFAULT '',
              Allée nvarchar(20) NULL, Rangée nvarchar(20) NULL, Niveau nvarchar(20) NULL,
              TypeEmplacement int NOT NULL DEFAULT 0, CapacitePalettes int NOT NULL DEFAULT 0,
              EstOccupe bit NOT NULL DEFAULT 0, EstActif bit NOT NULL DEFAULT 1,
              CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(), UpdatedAt datetime2 NULL,
              CreatedBy nvarchar(450) NOT NULL DEFAULT '', UpdatedBy nvarchar(450) NULL)",

            // Articles
            @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Articles' AND xtype='U')
              CREATE TABLE Articles (Id uniqueidentifier NOT NULL PRIMARY KEY,
              Code nvarchar(20) NOT NULL, CodeBarres nvarchar(50) NULL,
              Designation nvarchar(200) NOT NULL, Description nvarchar(1000) NULL,
              Categorie nvarchar(100) NOT NULL DEFAULT '', FamilleArticle nvarchar(100) NULL,
              Unite nvarchar(20) NOT NULL DEFAULT 'PCS', PrixAchat decimal(18,4) NOT NULL DEFAULT 0,
              PrixVente decimal(18,4) NOT NULL DEFAULT 0, SeuilAlerte int NOT NULL DEFAULT 5,
              StockMinimum int NOT NULL DEFAULT 0, StockMaximum int NOT NULL DEFAULT 0,
              SansSuiviStock bit NOT NULL DEFAULT 0,
              GestionLot bit NOT NULL DEFAULT 0, GestionDLUO bit NOT NULL DEFAULT 0,
              FournisseurId uniqueidentifier NULL, Statut int NOT NULL DEFAULT 1,
              CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(), UpdatedAt datetime2 NULL,
              CreatedBy nvarchar(450) NOT NULL DEFAULT '', UpdatedBy nvarchar(450) NULL)",

            // Stocks
            @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Stocks' AND xtype='U')
              CREATE TABLE Stocks (Id uniqueidentifier NOT NULL PRIMARY KEY,
              ArticleId uniqueidentifier NOT NULL, EmplacementId uniqueidentifier NOT NULL,
              QuantiteDisponible int NOT NULL DEFAULT 0, QuantiteReservee int NOT NULL DEFAULT 0,
              PrixUnitaireMoyen decimal(18,4) NOT NULL DEFAULT 0,
              NumeroLot nvarchar(50) NULL, DatePeremption datetime2 NULL,
              CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(), UpdatedAt datetime2 NULL,
              CreatedBy nvarchar(450) NOT NULL DEFAULT '', UpdatedBy nvarchar(450) NULL)",

            // MouvementsStock
            @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='MouvementsStock' AND xtype='U')
              CREATE TABLE MouvementsStock (Id uniqueidentifier NOT NULL PRIMARY KEY,
              ArticleId uniqueidentifier NOT NULL, EmplacementSourceId uniqueidentifier NULL,
              EmplacementDestinationId uniqueidentifier NULL, TypeMouvement int NOT NULL,
              Quantite int NOT NULL,
              PrixUnitaire decimal(18,4) NOT NULL DEFAULT 0,
              ValeurUnitaire AS PrixUnitaire,
              Reference nvarchar(100) NULL, NumeroLot nvarchar(50) NULL,
              DatePeremption datetime2 NULL, Motif nvarchar(500) NULL,
              CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
              DateMouvement AS CreatedAt,
              CreatedBy nvarchar(450) NOT NULL DEFAULT '')",

            // Fournisseurs
            @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Fournisseurs' AND xtype='U')
              CREATE TABLE Fournisseurs (Id uniqueidentifier NOT NULL PRIMARY KEY,
              Code nvarchar(10) NOT NULL, RaisonSociale nvarchar(200) NOT NULL,
              Siret nvarchar(20) NULL, Email nvarchar(256) NOT NULL DEFAULT '',
              Telephone nvarchar(30) NOT NULL DEFAULT '', Adresse nvarchar(300) NULL,
              Ville nvarchar(100) NULL, CodePostal nvarchar(10) NULL, Pays nvarchar(100) NULL,
              DelaiLivraisonJours int NOT NULL DEFAULT 7, EstActif bit NOT NULL DEFAULT 1,
              CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(), UpdatedAt datetime2 NULL,
              CreatedBy nvarchar(450) NOT NULL DEFAULT '', UpdatedBy nvarchar(450) NULL)",

            // CommandesAchat
            @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='CommandesAchat' AND xtype='U')
              CREATE TABLE CommandesAchat (Id uniqueidentifier NOT NULL PRIMARY KEY,
              Numero nvarchar(20) NOT NULL, FournisseurId uniqueidentifier NOT NULL,
              DateCommande datetime2 NOT NULL DEFAULT GETUTCDATE(),
              DateLivraisonPrevue datetime2 NOT NULL, DateLivraisonReelle datetime2 NULL,
              Statut int NOT NULL DEFAULT 1, Notes nvarchar(1000) NULL,
              MontantHT decimal(18,2) NOT NULL DEFAULT 0, MontantTVA decimal(18,2) NOT NULL DEFAULT 0,
              MontantTTC decimal(18,2) NOT NULL DEFAULT 0,
              CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(), UpdatedAt datetime2 NULL,
              CreatedBy nvarchar(450) NOT NULL DEFAULT '', UpdatedBy nvarchar(450) NULL)",

            // LignesCommande
            @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='LignesCommandeAchat' AND xtype='U')
              CREATE TABLE LignesCommandeAchat (Id uniqueidentifier NOT NULL PRIMARY KEY,
              CommandeAchatId uniqueidentifier NOT NULL, ArticleId uniqueidentifier NOT NULL,
              Quantite int NOT NULL DEFAULT 1, PrixUnitaireHT decimal(18,4) NOT NULL DEFAULT 0,
              TauxTVA decimal(5,2) NOT NULL DEFAULT 20, MontantHT decimal(18,2) NOT NULL DEFAULT 0,
              CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
              CreatedBy nvarchar(450) NOT NULL DEFAULT '')",

            // Dépôts
            @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Depots' AND xtype='U')
              CREATE TABLE Depots (Id uniqueidentifier NOT NULL PRIMARY KEY,
              Code nvarchar(20) NOT NULL, Libelle nvarchar(200) NOT NULL,
              Description nvarchar(500) NULL, Adresse nvarchar(300) NOT NULL DEFAULT '',
              CodePostal nvarchar(10) NOT NULL DEFAULT '', Ville nvarchar(100) NOT NULL DEFAULT '',
              Pays nvarchar(100) NOT NULL DEFAULT 'France', Responsable nvarchar(100) NULL,
              Telephone nvarchar(30) NULL, SurfaceM2 decimal(10,2) NOT NULL DEFAULT 0,
              CapacitePalettes int NOT NULL DEFAULT 0, EstPrincipal bit NOT NULL DEFAULT 0,
              EstActif bit NOT NULL DEFAULT 1, TypeDepot int NOT NULL DEFAULT 0,
              CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(), UpdatedAt datetime2 NULL,
              CreatedBy nvarchar(450) NOT NULL DEFAULT '', UpdatedBy nvarchar(450) NULL)",

            // Familles
            @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='FamillesArticles' AND xtype='U')
              CREATE TABLE FamillesArticles (Id uniqueidentifier NOT NULL PRIMARY KEY,
              Code nvarchar(20) NOT NULL, Libelle nvarchar(200) NOT NULL,
              Description nvarchar(500) NULL, ParentId uniqueidentifier NULL,
              Couleur nvarchar(10) NULL, Ordre int NOT NULL DEFAULT 0,
              EstActif bit NOT NULL DEFAULT 1,
              CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(), UpdatedAt datetime2 NULL,
              CreatedBy nvarchar(450) NOT NULL DEFAULT '', UpdatedBy nvarchar(450) NULL,
              CONSTRAINT FK_FamillesArticles_Parent FOREIGN KEY (ParentId)
                REFERENCES FamillesArticles(Id))",

            // Paramètres
            @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Parametres' AND xtype='U')
              CREATE TABLE Parametres (Id int NOT NULL PRIMARY KEY,
              RaisonSociale nvarchar(200) NOT NULL DEFAULT '',
              Siret nvarchar(20) NOT NULL DEFAULT '', NumTVA nvarchar(20) NOT NULL DEFAULT '',
              Telephone nvarchar(30) NOT NULL DEFAULT '', Email nvarchar(256) NOT NULL DEFAULT '',
              SiteWeb nvarchar(200) NOT NULL DEFAULT '', FormeJuridique nvarchar(50) NOT NULL DEFAULT '',
              Adresse nvarchar(300) NOT NULL DEFAULT '', CodePostal nvarchar(10) NOT NULL DEFAULT '',
              Ville nvarchar(100) NOT NULL DEFAULT '', Region nvarchar(100) NOT NULL DEFAULT '',
              Pays nvarchar(100) NOT NULL DEFAULT 'France',
              MethodeValorisation nvarchar(10) NOT NULL DEFAULT 'FEFO',
              GabaritInterface nvarchar(30) NOT NULL DEFAULT 'STANDARD',
              LogoEntreprise nvarchar(max) NOT NULL DEFAULT '',
              FormatImpressionDocuments nvarchar(30) NOT NULL DEFAULT 'STANDARD',
              FormatImpressionRecus nvarchar(30) NOT NULL DEFAULT 'STANDARD',
              FormatPapierDocuments nvarchar(20) NOT NULL DEFAULT 'A4',
              ImprimanteDocumentsDefaut nvarchar(120) NOT NULL DEFAULT '',
              FormatPapierRecus nvarchar(20) NOT NULL DEFAULT 'A5',
              ImprimanteRecusDefaut nvarchar(120) NOT NULL DEFAULT '',
              Devise nvarchar(5) NOT NULL DEFAULT 'EUR',
              SymboleDevise nvarchar(10) NOT NULL DEFAULT 'EUR',
              NombreDecimalesMontant int NOT NULL DEFAULT 2,
              NombreDecimalesQuantite int NOT NULL DEFAULT 3,
              TauxTVA decimal(5,2) NOT NULL DEFAULT 20,
              DelaiAlerteDLUO int NOT NULL DEFAULT 30,
              AlerteMailActif bit NOT NULL DEFAULT 0, AlerteMailDestinataire nvarchar(256) NULL,
              PrefixeCA nvarchar(10) NOT NULL DEFAULT 'CA',
              PrefixeArt nvarchar(10) NOT NULL DEFAULT 'ART',
              PrefixeLot nvarchar(10) NOT NULL DEFAULT 'LOT',
              PrefixeInv nvarchar(10) NOT NULL DEFAULT 'INV',
              Banque nvarchar(100) NOT NULL DEFAULT '', Iban nvarchar(34) NOT NULL DEFAULT '',
              Bic nvarchar(11) NOT NULL DEFAULT '', DelaiPaiement int NOT NULL DEFAULT 30,
              UpdatedAt datetime2 NULL, UpdatedBy nvarchar(450) NULL)"
        };

        foreach (var sql in tables)
        {
            await using var cmd = new SqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task SeedEmplacementsAsync(SqlConnection conn)
    {
        var emplacements = new[]
        {
            ("QUAI-01", "QUAI"), ("ZONE-A-01", "ZONE-A"),
            ("ZONE-B-01", "ZONE-B"), ("ZONE-C-01", "ZONE-C"), ("QUAR-01", "QUARANTAINE")
        };
        foreach (var (code, zone) in emplacements)
        {
            var sql = $@"IF NOT EXISTS (SELECT 1 FROM Emplacements WHERE Code='{code}')
                INSERT INTO Emplacements (Id,Code,Zone,EstActif,CreatedAt,CreatedBy)
                VALUES (NEWID(),'{code}','{zone}',1,GETUTCDATE(),'system')";
            await using var cmd = new SqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task CreerAdminAsync(SqlConnection conn,
        string email, string password, string nomComplet, string tenantCode)
    {
        // Hash password with ASP.NET Identity V3 format
        var passwordHash = HashPassword(password);
        var userId = Guid.NewGuid().ToString();
        var normalizedEmail = email.ToUpperInvariant();

        var sql = $@"
            IF NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE Name='Admin')
                INSERT INTO AspNetRoles (Id,Name,NormalizedName,ConcurrencyStamp)
                VALUES (NEWID(),'Admin','ADMIN',NEWID());

            IF NOT EXISTS (SELECT 1 FROM AspNetUsers WHERE NormalizedEmail=@ne)
            BEGIN
                INSERT INTO AspNetUsers (Id,UserName,NormalizedUserName,Email,NormalizedEmail,
                    EmailConfirmed,PasswordHash,SecurityStamp,ConcurrencyStamp,
                    LockoutEnabled,AccessFailedCount,NomComplet,TenantCode,EstActif,CreatedAt,Role)
                VALUES (@id,@email,@ne,@email,@ne,1,@hash,NEWID(),NEWID(),0,0,@nom,@tenant,1,GETUTCDATE(),1);

                INSERT INTO AspNetUserRoles (UserId,RoleId)
                SELECT @id, Id FROM AspNetRoles WHERE Name='Admin';
            END";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@id", userId);
        cmd.Parameters.AddWithValue("@email", email);
        cmd.Parameters.AddWithValue("@ne", normalizedEmail);
        cmd.Parameters.AddWithValue("@hash", passwordHash);
        cmd.Parameters.AddWithValue("@nom", nomComplet);
        cmd.Parameters.AddWithValue("@tenant", (object?)tenantCode ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SeedParametresAsync(SqlConnection conn,
        string raisonSociale, string email,
        string devise, string symboleDevise,
        int nombreDecimalesMontant, int nombreDecimalesQuantite)
    {
        var sql = @"IF NOT EXISTS (SELECT 1 FROM Parametres WHERE Id=1)
            INSERT INTO Parametres (Id,RaisonSociale,Email,Pays,MethodeValorisation,GabaritInterface,LogoEntreprise,
                FormatImpressionDocuments,FormatImpressionRecus,FormatPapierDocuments,ImprimanteDocumentsDefaut,
                FormatPapierRecus,ImprimanteRecusDefaut,Devise,SymboleDevise,
                NombreDecimalesMontant,NombreDecimalesQuantite,TauxTVA,
                DelaiAlerteDLUO,PrefixeCA,PrefixeArt,PrefixeLot,PrefixeInv,DelaiPaiement)
            VALUES (1,@rs,@email,'France','FEFO','STANDARD','',
                'STANDARD','STANDARD','A4','','A5','',@devise,@symboleDevise,@decMontant,@decQuantite,20,30,'CA','ART','LOT','INV',30)";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@rs", raisonSociale);
        cmd.Parameters.AddWithValue("@email", email);
        cmd.Parameters.AddWithValue("@devise", devise);
        cmd.Parameters.AddWithValue("@symboleDevise", symboleDevise);
        cmd.Parameters.AddWithValue("@decMontant", nombreDecimalesMontant);
        cmd.Parameters.AddWithValue("@decQuantite", nombreDecimalesQuantite);
        await cmd.ExecuteNonQueryAsync();
    }

    // ASP.NET Identity V3 password hasher — Rfc2898DeriveBytes (BCL, no extra package)
    public static string HashPasswordPublic(string password) => HashPassword(password);
    private static string HashPassword(string password)
    {
        var salt = new byte[16];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(salt);
        var pbkdf2 = new System.Security.Cryptography.Rfc2898DeriveBytes(
            password, salt, 10000,
            System.Security.Cryptography.HashAlgorithmName.SHA256);
        var key = pbkdf2.GetBytes(32);
        // ASP.NET Identity V3 format header
        var result = new byte[1 + 4 + 4 + 4 + 16 + 32];
        result[0] = 0x01;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(1), 1);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(5), 10000);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(9), 16);
        salt.CopyTo(result, 13);
        key.CopyTo(result, 29);
        return Convert.ToBase64String(result);
    }
}
