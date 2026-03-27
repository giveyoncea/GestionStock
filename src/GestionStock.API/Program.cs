using GestionStock.API.Extensions;
using GestionStock.API.Middleware;
using GestionStock.Domain.Entities;
using GestionStock.Domain.Enums;
using GestionStock.Infrastructure.Data;
using GestionStock.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("logs/gestionstock-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30)
    .CreateLogger();

try
{
    Log.Information("GestionStock â€“ DÃ©marrage");

    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    builder.Services
        .AddDatabase(builder.Configuration)
        .AddIdentityAndJwt(builder.Configuration)
        .AddApplicationServices()
        .AddSwagger();

    builder.Services.AddRazorPages();
    builder.Services.AddControllers()
        .AddJsonOptions(opts =>
            opts.JsonSerializerOptions.PropertyNamingPolicy =
                System.Text.Json.JsonNamingPolicy.CamelCase);

    builder.Services.AddHealthChecks()
        .AddCheck("api", () =>
            Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy());

    var app = builder.Build();

    // â”€â”€â”€ MIGRATIONS & SEED â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    using (var scope = app.Services.CreateScope())
    {
        try
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // CrÃ©er la base si elle n'existe pas
            var created = await db.Database.EnsureCreatedAsync();
            Log.Information(created ? "Base de donnÃ©es crÃ©Ã©e." : "Base de donnÃ©es existante.");

            // S'assurer que la table Parametres existe (ajoutÃ©e aprÃ¨s la crÃ©ation initiale)
            // RecrÃ©er la table sans IDENTITY si elle existe avec IDENTITY
            await db.Database.ExecuteSqlRawAsync(@"
                IF EXISTS (SELECT * FROM sysobjects WHERE name='Parametres' AND xtype='U')
                BEGIN
                    IF EXISTS (SELECT * FROM sys.columns WHERE object_id=OBJECT_ID('Parametres') AND name='Id' AND is_identity=1)
                    DROP TABLE Parametres
                END
            ");
            await db.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Parametres' AND xtype='U')
                CREATE TABLE Parametres (
                    Id int NOT NULL PRIMARY KEY,
                    RaisonSociale nvarchar(200) NOT NULL DEFAULT '',
                    Siret nvarchar(20) NOT NULL DEFAULT '',
                    NumTVA nvarchar(50) NOT NULL DEFAULT '',
                    Telephone nvarchar(50) NOT NULL DEFAULT '',
                    Email nvarchar(150) NOT NULL DEFAULT '',
                    SiteWeb nvarchar(200) NOT NULL DEFAULT '',
                    FormeJuridique nvarchar(50) NOT NULL DEFAULT '',
                    Adresse nvarchar(300) NOT NULL DEFAULT '',
                    CodePostal nvarchar(10) NOT NULL DEFAULT '',
                    Ville nvarchar(100) NOT NULL DEFAULT '',
                    Region nvarchar(100) NOT NULL DEFAULT '',
                    Pays nvarchar(100) NOT NULL DEFAULT 'France',
                    EntrepotNom nvarchar(100) NOT NULL DEFAULT 'Entrepot central',
                    EntrepotCode nvarchar(20) NOT NULL DEFAULT 'CENTRAL',
                    EntrepotAdresse nvarchar(300) NOT NULL DEFAULT '',
                    EntrepotSurface decimal(10,2) NOT NULL DEFAULT 0,
                    EntrepotCapacite int NOT NULL DEFAULT 0,
                    MethodeValorisation nvarchar(10) NOT NULL DEFAULT 'FEFO',
                    GabaritInterface nvarchar(30) NOT NULL DEFAULT 'STANDARD',
                    LogoEntreprise nvarchar(max) NOT NULL DEFAULT '',
                    FormatImpressionDocuments nvarchar(30) NOT NULL DEFAULT 'STANDARD',
                    FormatImpressionRecus nvarchar(30) NOT NULL DEFAULT 'STANDARD',
                    FormatPapierDocuments nvarchar(20) NOT NULL DEFAULT 'A4',
                    ImprimanteDocumentsDefaut nvarchar(120) NOT NULL DEFAULT '',
                    FormatPapierRecus nvarchar(20) NOT NULL DEFAULT 'A5',
                    ImprimanteRecusDefaut nvarchar(120) NOT NULL DEFAULT '',
                    Devise nvarchar(10) NOT NULL DEFAULT 'EUR',
                    SymboleDevise nvarchar(10) NOT NULL DEFAULT 'EUR',
                    NombreDecimalesMontant int NOT NULL DEFAULT 2,
                    NombreDecimalesQuantite int NOT NULL DEFAULT 3,
                    TauxTVA decimal(5,2) NOT NULL DEFAULT 20,
                    DelaiAlerteDLUO int NOT NULL DEFAULT 30,
                    GestionLotDefaut bit NOT NULL DEFAULT 0,
                    AlerteMailActif bit NOT NULL DEFAULT 0,
                    InventaireAnnuelObligatoire bit NOT NULL DEFAULT 1,
                    EmailAlerte nvarchar(150) NOT NULL DEFAULT '',
                    PrefixeCA nvarchar(10) NOT NULL DEFAULT 'CA',
                    PrefixeArt nvarchar(10) NOT NULL DEFAULT 'ART',
                    PrefixeLot nvarchar(10) NOT NULL DEFAULT 'LOT',
                    PrefixeInv nvarchar(10) NOT NULL DEFAULT 'INV',
                    Banque nvarchar(100) NOT NULL DEFAULT '',
                    Iban nvarchar(50) NOT NULL DEFAULT '',
                    Bic nvarchar(20) NOT NULL DEFAULT '',
                    DelaiPaiement int NOT NULL DEFAULT 30,
                    UpdatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
                    UpdatedBy nvarchar(450) NOT NULL DEFAULT ''
                )");
            Log.Information("Table Parametres vÃ©rifiÃ©e.");

            // Base Master : table Tenants
            var masterConnStr = builder.Configuration.GetConnectionString("MasterConnection");
            if (!string.IsNullOrEmpty(masterConnStr))
            {
                try
                {
                    var sqlBaseStr = builder.Configuration.GetConnectionString("SqlServerBase")!;
                    await using var mc = new Microsoft.Data.SqlClient.SqlConnection($"{sqlBaseStr};Database=master");
                    await mc.OpenAsync();
                    await using var mc2 = new Microsoft.Data.SqlClient.SqlCommand(
                        "IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name='GestionStockMaster') CREATE DATABASE [GestionStockMaster]", mc);
                    await mc2.ExecuteNonQueryAsync();
                    await using var mc3 = new Microsoft.Data.SqlClient.SqlConnection(masterConnStr);
                    await mc3.OpenAsync();
                    await using var mc4 = new Microsoft.Data.SqlClient.SqlCommand(@"
                        IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Tenants' AND xtype='U')
                        CREATE TABLE Tenants (
                            Id uniqueidentifier NOT NULL PRIMARY KEY DEFAULT NEWID(),
                            Code nvarchar(20) NOT NULL UNIQUE,
                            RaisonSociale nvarchar(200) NOT NULL,
                            Email nvarchar(256) NOT NULL,
                            AdminNom nvarchar(200) NOT NULL,
                            BaseDeDonnees nvarchar(100) NOT NULL,
                            EstActif bit NOT NULL DEFAULT 1,
                            DateCreation datetime2 NOT NULL DEFAULT GETUTCDATE()
                        )", mc3);
                    await mc4.ExecuteNonQueryAsync();
                    Log.Information("Base GestionStockMaster et table Tenants OK.");
                }
                catch (Exception ex) { Log.Warning(ex, "Impossible de crÃ©er la base master"); }
            }

            await db.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Depots' AND xtype='U')
                CREATE TABLE Depots (
                    Id uniqueidentifier NOT NULL PRIMARY KEY,
                    Code nvarchar(20) NOT NULL,
                    Libelle nvarchar(200) NOT NULL,
                    Description nvarchar(500) NULL,
                    Adresse nvarchar(300) NOT NULL DEFAULT '',
                    CodePostal nvarchar(10) NOT NULL DEFAULT '',
                    Ville nvarchar(100) NOT NULL DEFAULT '',
                    Pays nvarchar(100) NOT NULL DEFAULT 'France',
                    Responsable nvarchar(100) NULL,
                    Telephone nvarchar(30) NULL,
                    SurfaceM2 decimal(10,2) NOT NULL DEFAULT 0,
                    CapacitePalettes int NOT NULL DEFAULT 0,
                    EstPrincipal bit NOT NULL DEFAULT 0,
                    EstActif bit NOT NULL DEFAULT 1,
                    TypeDepot int NOT NULL DEFAULT 0,
                    CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
                    UpdatedAt datetime2 NULL,
                    CreatedBy nvarchar(450) NOT NULL DEFAULT '',
                    UpdatedBy nvarchar(450) NULL
                )");

            await db.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='FamillesArticles' AND xtype='U')
                CREATE TABLE FamillesArticles (
                    Id uniqueidentifier NOT NULL PRIMARY KEY,
                    Code nvarchar(20) NOT NULL,
                    Libelle nvarchar(200) NOT NULL,
                    Description nvarchar(500) NULL,
                    ParentId uniqueidentifier NULL,
                    Couleur nvarchar(10) NULL,
                    Ordre int NOT NULL DEFAULT 0,
                    EstActif bit NOT NULL DEFAULT 1,
                    CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE(),
                    UpdatedAt datetime2 NULL,
                    CreatedBy nvarchar(450) NOT NULL DEFAULT '',
                    UpdatedBy nvarchar(450) NULL,
                    CONSTRAINT FK_FamillesArticles_Parent FOREIGN KEY (ParentId)
                        REFERENCES FamillesArticles(Id)
                )");
            // Ajouter TypeDepot si la colonne manque (table existante sans elle)
            await db.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (SELECT 1 FROM sys.columns
                               WHERE object_id = OBJECT_ID('Depots')
                               AND name = 'TypeDepot')
                ALTER TABLE Depots ADD TypeDepot int NOT NULL DEFAULT 0");
            await db.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (SELECT 1 FROM sys.columns
                               WHERE object_id = OBJECT_ID('Parametres')
                               AND name = 'GabaritInterface')
                ALTER TABLE Parametres ADD GabaritInterface nvarchar(30) NOT NULL DEFAULT 'STANDARD'");
            await db.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (SELECT 1 FROM sys.columns
                               WHERE object_id = OBJECT_ID('Parametres')
                               AND name = 'LogoEntreprise')
                ALTER TABLE Parametres ADD LogoEntreprise nvarchar(max) NOT NULL DEFAULT ''");
            await db.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (SELECT 1 FROM sys.columns
                               WHERE object_id = OBJECT_ID('Parametres')
                               AND name = 'FormatImpressionDocuments')
                ALTER TABLE Parametres ADD FormatImpressionDocuments nvarchar(30) NOT NULL DEFAULT 'STANDARD'");
            await db.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (SELECT 1 FROM sys.columns
                               WHERE object_id = OBJECT_ID('Parametres')
                               AND name = 'FormatImpressionRecus')
                ALTER TABLE Parametres ADD FormatImpressionRecus nvarchar(30) NOT NULL DEFAULT 'STANDARD'");
            await db.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (SELECT 1 FROM sys.columns
                               WHERE object_id = OBJECT_ID('Parametres')
                               AND name = 'FormatPapierDocuments')
                ALTER TABLE Parametres ADD FormatPapierDocuments nvarchar(20) NOT NULL DEFAULT 'A4'");
            await db.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (SELECT 1 FROM sys.columns
                               WHERE object_id = OBJECT_ID('Parametres')
                               AND name = 'ImprimanteDocumentsDefaut')
                ALTER TABLE Parametres ADD ImprimanteDocumentsDefaut nvarchar(120) NOT NULL DEFAULT ''");
            await db.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (SELECT 1 FROM sys.columns
                               WHERE object_id = OBJECT_ID('Parametres')
                               AND name = 'FormatPapierRecus')
                ALTER TABLE Parametres ADD FormatPapierRecus nvarchar(20) NOT NULL DEFAULT 'A5'");
            await db.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (SELECT 1 FROM sys.columns
                               WHERE object_id = OBJECT_ID('Parametres')
                               AND name = 'ImprimanteRecusDefaut')
                ALTER TABLE Parametres ADD ImprimanteRecusDefaut nvarchar(120) NOT NULL DEFAULT ''");
            await db.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (SELECT 1 FROM sys.columns
                               WHERE object_id = OBJECT_ID('Parametres')
                               AND name = 'SymboleDevise')
                ALTER TABLE Parametres ADD SymboleDevise nvarchar(10) NOT NULL DEFAULT 'EUR'");
            await db.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (SELECT 1 FROM sys.columns
                               WHERE object_id = OBJECT_ID('Parametres')
                               AND name = 'NombreDecimalesMontant')
                ALTER TABLE Parametres ADD NombreDecimalesMontant int NOT NULL DEFAULT 2");
            await db.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (SELECT 1 FROM sys.columns
                               WHERE object_id = OBJECT_ID('Parametres')
                               AND name = 'NombreDecimalesQuantite')
                ALTER TABLE Parametres ADD NombreDecimalesQuantite int NOT NULL DEFAULT 3");
            await db.Database.ExecuteSqlRawAsync(@"
                UPDATE Parametres
                SET GabaritInterface = CASE
                    WHEN NULLIF(LTRIM(RTRIM(GabaritInterface)), '') IS NULL THEN 'STANDARD'
                    WHEN UPPER(LTRIM(RTRIM(GabaritInterface))) = 'MARBRE_BLEU' THEN 'MARBRE_BLEU'
                    ELSE 'STANDARD'
                END");
            await db.Database.ExecuteSqlRawAsync(@"
                UPDATE Parametres
                SET FormatImpressionDocuments = CASE
                        WHEN UPPER(LTRIM(RTRIM(FormatImpressionDocuments))) IN ('STANDARD','PROFESSIONNEL','COMPACT')
                            THEN UPPER(LTRIM(RTRIM(FormatImpressionDocuments)))
                        ELSE 'STANDARD'
                    END,
                    FormatImpressionRecus = CASE
                        WHEN UPPER(LTRIM(RTRIM(FormatImpressionRecus))) IN ('STANDARD','PROFESSIONNEL','COMPACT')
                            THEN UPPER(LTRIM(RTRIM(FormatImpressionRecus)))
                        ELSE 'STANDARD'
                    END,
                    FormatPapierDocuments = CASE
                        WHEN UPPER(LTRIM(RTRIM(FormatPapierDocuments))) IN ('A5','80MM')
                            THEN UPPER(LTRIM(RTRIM(FormatPapierDocuments)))
                        ELSE 'A4'
                    END,
                    FormatPapierRecus = CASE
                        WHEN UPPER(LTRIM(RTRIM(FormatPapierRecus))) IN ('A4','2X_A5_A4','80MM')
                            THEN UPPER(LTRIM(RTRIM(FormatPapierRecus)))
                        ELSE 'A5'
                    END");
            await db.Database.ExecuteSqlRawAsync(@"
                UPDATE Parametres
                SET SymboleDevise = CASE
                    WHEN NULLIF(LTRIM(RTRIM(SymboleDevise)), '') IS NULL THEN Devise
                    ELSE SymboleDevise
                END");
            Log.Information("Tables Depots et FamillesArticles OK.");

            await SeedEmplacementsAsync(db);
            await SeedRolesAndAdminAsync(scope.ServiceProvider);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Erreur lors du dÃ©marrage : {Message}", ex.Message);
        }
    }

    // â”€â”€â”€ PIPELINE â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    app.UseMiddleware<ExceptionHandlingMiddleware>();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "GestionStock API v1");
            c.RoutePrefix = "swagger";
        });
        app.UseWebAssemblyDebugging();
    }

    app.UseBlazorFrameworkFiles();
    app.UseStaticFiles();
    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseSerilogRequestLogging();

    app.MapControllers();
    app.MapHealthChecks("/health");
    app.MapFallbackToFile("index.html");

    Log.Information("DÃ©marrÃ© sur {Urls}", string.Join(", ",
        app.Urls.DefaultIfEmpty("http://localhost:5000")));
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "ArrÃªt fatal : {Message}", ex.Message);
}
finally
{
    Log.CloseAndFlush();
}

// â”€â”€â”€ SEED EMPLACEMENTS â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
static async Task SeedEmplacementsAsync(AppDbContext db)
{
    if (await db.Emplacements.AnyAsync()) return;

    db.Emplacements.AddRange(
        Emplacement.Creer("QUAI-01",   "Quai de rÃ©ception",  "QUAI",   TypeEmplacement.Quai,     "system"),
        Emplacement.Creer("ZONE-A-01", "Zone A â€“ AllÃ©e 1",   "ZONE-A", TypeEmplacement.Standard, "system"),
        Emplacement.Creer("ZONE-B-01", "Zone B â€“ AllÃ©e 1",   "ZONE-B", TypeEmplacement.Standard, "system"),
        Emplacement.Creer("ZONE-C-01", "Zone C â€“ Picking",   "ZONE-C", TypeEmplacement.Picking,  "system"),
        Emplacement.Creer("QUAR-01",   "Quarantaine",         "QUAR",   TypeEmplacement.Quarantaine, "system")
    );
    await db.SaveChangesAsync();
    Log.Information("Emplacements crÃ©Ã©s.");
}

// â”€â”€â”€ SEED RÃ”LES & ADMIN â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
static async Task SeedRolesAndAdminAsync(IServiceProvider services)
{
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
    var config     = services.GetRequiredService<IConfiguration>();

    // RÃ´les
    string[] roles = ["Admin", "Magasinier", "Acheteur", "Superviseur", "Lecteur"];
    foreach (var role in roles)
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));

    // Compte admin par dÃ©faut
    var email    = config["DefaultAdmin:Email"]    ?? "admin@gestionstock.com";
    var password = config["DefaultAdmin:Password"] ?? "Admin@2024!Stock";

    if (await userManager.FindByEmailAsync(email) is null)
    {
        var user = new ApplicationUser
        {
            UserName       = email,
            Email          = email,
            FirstName      = "Administrateur",
            LastName       = "SystÃ¨me",
            Role           = GestionStock.Domain.Enums.RoleUtilisateur.Admin,
            EmailConfirmed = true,
            EstActif       = true
        };

        var result = await userManager.CreateAsync(user, password);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(user, "Admin");
            Log.Information("âœ“ Compte admin crÃ©Ã© : {Email} / {Password}", email, password);
        }
        else
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            Log.Warning("âœ— CrÃ©ation admin Ã©chouÃ©e : {Errors}", errors);
        }
    }
    else
    {
        Log.Information("Compte admin existant: {Email}", email);
    }
}

