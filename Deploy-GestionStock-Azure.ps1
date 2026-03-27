# =============================================================================
# Deploy-GestionStock-Azure.ps1
# Deploiement GestionStock vers Azure App Service + SQL Server
#
# Usage :
#   .\Deploy-GestionStock-Azure.ps1                      # Build + Deploy + SQL
#   .\Deploy-GestionStock-Azure.ps1 -SkipBuild           # Deploy + SQL seulement
#   .\Deploy-GestionStock-Azure.ps1 -SkipDeploy          # SQL seulement
#   .\Deploy-GestionStock-Azure.ps1 -SkipBuild -SkipDeploy  # SQL seul
#   .\Deploy-GestionStock-Azure.ps1 -SkipSql             # Build + Deploy sans SQL
# =============================================================================
param(
    [switch]$SkipBuild,
    [switch]$SkipDeploy,
    [switch]$SkipSql
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# --- CONFIGURATION -----------------------------------------------------------
$SolutionPath   = "C:\Users\Charly\source\repos\GestionStock"
$ApiProject     = "src\GestionStock.API\GestionStock.API.csproj"
$WebProject     = "src\GestionStock.Web\GestionStock.Web.csproj"
$PublishDir     = "C:\Temp\GestionStock-publish"
$ZipPath        = "C:\Temp\GestionStock-publish.zip"

$ResourceGroup  = "GestionStockRG"
$AppServiceName = "gescom-gounks"
$AppUrl         = "https://gescom-gounks-fmahdcd0bxefaaas.canadacentral-01.azurewebsites.net"

$SqlServer   = "gestionstock-sql2026.database.windows.net"
$SqlUser     = "sqladmin"
$SqlPassword = "Occupy123*"
$DbDefault   = "GestionStockDB"
$DbMaster    = "GestionStockMaster"

# --- HELPERS -----------------------------------------------------------------
function Write-Step([string]$msg) { Write-Host "`n--- $msg ---" -ForegroundColor Cyan }
function Write-OK([string]$msg)   { Write-Host "  [OK] $msg"  -ForegroundColor Green }
function Write-Warn([string]$msg) { Write-Host "  [!!] $msg"  -ForegroundColor Yellow }
function Write-Fail([string]$msg) { Write-Host "  [XX] $msg"  -ForegroundColor Red }
function Write-Info([string]$msg) { Write-Host "       $msg"  -ForegroundColor White }

function Get-ConnStr([string]$db) {
    return ("Server={0};Database={1};User Id={2};Password={3};" +
            "Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;") `
            -f $SqlServer, $db, $SqlUser, $SqlPassword
}

function Invoke-Sql([string]$database, [string]$sql, [string]$label) {
    $conn = New-Object System.Data.SqlClient.SqlConnection
    $conn.ConnectionString = Get-ConnStr $database
    try {
        $conn.Open()
        $batches = $sql -split '(?m)^\s*GO\s*$'
        foreach ($batch in $batches) {
            $b = $batch.Trim()
            if ($b.Length -eq 0) { continue }
            $cmd = $conn.CreateCommand()
            $cmd.CommandText    = $b
            $cmd.CommandTimeout = 120
            $cmd.ExecuteNonQuery() | Out-Null
        }
        if ($label) { Write-OK "[$database] $label" } else { Write-OK "[$database]" }
    }
    catch { Write-Warn "[$database] $($_.Exception.Message)" }
    finally { if ($conn.State -eq 'Open') { $conn.Close() } }
}

function Invoke-SqlQuery([string]$database, [string]$sql) {
    $conn  = New-Object System.Data.SqlClient.SqlConnection
    $conn.ConnectionString = Get-ConnStr $database
    $table = New-Object System.Data.DataTable
    try {
        $conn.Open()
        (New-Object System.Data.SqlClient.SqlDataAdapter($sql, $conn)).Fill($table) | Out-Null
    }
    catch { }
    finally { if ($conn.State -eq 'Open') { $conn.Close() } }
    return $table
}

function Invoke-SqlScalar([string]$database, [string]$sql) {
    $conn = New-Object System.Data.SqlClient.SqlConnection
    $conn.ConnectionString = Get-ConnStr $database
    try {
        $conn.Open()
        $cmd = $conn.CreateCommand()
        $cmd.CommandText    = $sql
        $cmd.CommandTimeout = 30
        return $cmd.ExecuteScalar()
    }
    catch { return $null }
    finally { if ($conn.State -eq 'Open') { $conn.Close() } }
}

function Invoke-TenantMigration([string]$dbTenant, [string]$tenantCode) {
    Write-Host "     Migration : $dbTenant ($tenantCode)" -ForegroundColor Gray

    $sql1 = @'
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('AspNetUsers') AND name='TwoFactorEnabled2')
    ALTER TABLE AspNetUsers ADD TwoFactorEnabled2 bit NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('AspNetUsers') AND name='EstActif')
    ALTER TABLE AspNetUsers ADD EstActif bit NOT NULL DEFAULT 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('AspNetUsers') AND name='TenantCode')
    ALTER TABLE AspNetUsers ADD TenantCode nvarchar(20) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('AspNetUsers') AND name='FirstName')
    ALTER TABLE AspNetUsers ADD FirstName nvarchar(100) NOT NULL DEFAULT '';
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('AspNetUsers') AND name='LastName')
    ALTER TABLE AspNetUsers ADD LastName nvarchar(100) NOT NULL DEFAULT '';
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('AspNetUsers') AND name='CreatedAt')
    ALTER TABLE AspNetUsers ADD CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE();
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('AspNetUsers') AND name='DerniereConnexion')
    ALTER TABLE AspNetUsers ADD DerniereConnexion datetime2 NULL;
'@
    Invoke-Sql -database $dbTenant -sql $sql1 -label "AspNetUsers"

    $sql2 = "UPDATE AspNetUsers SET TenantCode = '$tenantCode' WHERE TenantCode IS NULL OR TenantCode != '$tenantCode';"
    Invoke-Sql -database $dbTenant -sql $sql2 -label "TenantCode=$tenantCode"

    $sql3 = @'
IF OBJECT_ID('StockArticles') IS NOT NULL AND OBJECT_ID('Stocks') IS NULL
    EXEC sp_rename 'StockArticles', 'Stocks';
'@
    Invoke-Sql -database $dbTenant -sql $sql3 -label "Stocks"

    $sql4 = @'
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('MouvementsStock') AND name='DateMouvement')
    ALTER TABLE MouvementsStock ADD DateMouvement AS CreatedAt;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('MouvementsStock') AND name='ValeurUnitaire')
    ALTER TABLE MouvementsStock ADD ValeurUnitaire AS PrixUnitaire;
'@
    Invoke-Sql -database $dbTenant -sql $sql4 -label "MouvementsStock"

    $sql5 = @'
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('LignesCommandeAchat') AND name='CommandeAchatId')
   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('LignesCommandeAchat') AND name='CommandeId')
    EXEC sp_rename 'LignesCommandeAchat.CommandeAchatId', 'CommandeId', 'COLUMN';
'@
    Invoke-Sql -database $dbTenant -sql $sql5 -label "LignesCommandeAchat"
}

# --- BANNIERE ----------------------------------------------------------------
$startTime = Get-Date
Write-Host ""
Write-Host "====================================================" -ForegroundColor Magenta
Write-Host "  GestionStock - Deploiement Azure" -ForegroundColor Magenta
Write-Host "====================================================" -ForegroundColor Magenta
Write-Host "  App Service : $AppServiceName"
Write-Host "  SQL Server  : $SqlServer"
Write-Host "  Demarre     : $(Get-Date -Format 'dd/MM/yyyy HH:mm:ss')"
$modes = @()
if (-not $SkipBuild)  { $modes += "Build" }
if (-not $SkipDeploy) { $modes += "Deploy" }
if (-not $SkipSql)    { $modes += "SQL" }
Write-Host "  Mode        : $($modes -join ' + ')"

# --- ETAPE 1 : BUILD ---------------------------------------------------------
if (-not $SkipBuild) {
    Write-Step "BUILD ET PUBLISH (.NET 9 Release)"

    if (-not (Test-Path $SolutionPath)) {
        Write-Fail "Solution introuvable : $SolutionPath"; exit 1
    }

    # Nettoyage
    if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
    New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null

    # Nettoyage cache HTTP NuGet uniquement (pas les packages - evite lock VS)
    Write-Host "  → Nettoyage cache HTTP NuGet..." -ForegroundColor Gray
    & dotnet nuget locals http-cache --clear 2>&1 | Out-Null
    Write-OK "Cache HTTP vide"

    # Afficher la version SDK active
    $sdkVer = (& dotnet --version 2>&1 | Out-String).Trim()
    Write-Host "  SDK .NET actif : $sdkVer" -ForegroundColor Gray

    # --- BUILD 1 : Projets backend (sans Web) --------------------------------
    Write-Host "  → Build projets backend..." -ForegroundColor Gray
    $infraProj = Join-Path $SolutionPath "src\GestionStock.Infrastructure\GestionStock.Infrastructure.csproj"
    & dotnet build $infraProj --configuration Release --no-restore 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        & dotnet restore (Join-Path $SolutionPath "GestionStock.sln")
        & dotnet build $infraProj --configuration Release 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Fail "Build Infrastructure echoue"; exit 1 }
    }
    Write-OK "Backend compile"

    # --- BUILD 2 : Projet Web (Blazor WASM) - tentatives multiples -----------
    Write-Host "  → Build Blazor WebAssembly..." -ForegroundColor Gray
    $webProj = Join-Path $SolutionPath $WebProject
    
    # Tentative 1 : direct
    & dotnet build $webProj --configuration Release 2>&1 | Out-Null
    
    if ($LASTEXITCODE -ne 0) {
        Write-Warn "Tentative 1 echouee - installation workload wasm-tools..."
        & dotnet workload install wasm-tools 2>&1 | Out-Null
        & dotnet build $webProj --configuration Release 2>&1 | Out-Null
    }
    
    if ($LASTEXITCODE -ne 0) {
        Write-Warn "Tentative 2 echouee - workload update complet..."
        & dotnet workload update 2>&1 | Out-Null
        & dotnet build $webProj --configuration Release 2>&1 | Out-Null
    }

    if ($LASTEXITCODE -ne 0) {
        Write-Fail "Build Blazor echoue apres toutes les tentatives."
        Write-Host ""
        Write-Host "  SOLUTION MANUELLE :" -ForegroundColor Yellow
        Write-Host "  1. Ouvrir Visual Studio" -ForegroundColor Yellow
        Write-Host "  2. Build → Publish → GestionStock.API" -ForegroundColor Yellow
        Write-Host "  3. Choisir le profil Azure App Service" -ForegroundColor Yellow
        Write-Host "  Ou relancer avec -SkipBuild apres avoir publie manuellement." -ForegroundColor Yellow
        Write-Host ""
        Write-Host "  Commande de diagnostic :" -ForegroundColor Gray
        Write-Host "  dotnet --list-sdks" -ForegroundColor Gray
        Write-Host "  dotnet workload list" -ForegroundColor Gray
        exit 1
    }
    Write-OK "Blazor WebAssembly compile"

    # --- PUBLISH API (inclut Web via ProjectReference) -----------------------
    Write-Host "  → Publish API Release..." -ForegroundColor Gray
    $apiProj = Join-Path $SolutionPath $ApiProject
    & dotnet publish $apiProj `
        --configuration Release `
        --output $PublishDir `
        --no-build

    if ($LASTEXITCODE -ne 0) {
        Write-Warn "Publish sans --no-build..."
        & dotnet publish $apiProj `
            --configuration Release `
            --output $PublishDir
        if ($LASTEXITCODE -ne 0) { Write-Fail "Publish echoue"; exit 1 }
    }
    Write-OK "Publish OK : $PublishDir"

    # ZIP
    if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
    Compress-Archive -Path "$PublishDir\*" -DestinationPath $ZipPath -Force
    $mb = [math]::Round((Get-Item $ZipPath).Length / 1MB, 1)
    Write-OK "ZIP : $ZipPath ($mb MB)"
}

# --- ETAPE 2 : DEPLOY --------------------------------------------------------
if (-not $SkipDeploy) {
    Write-Step "DEPLOIEMENT AZURE APP SERVICE"

    $acctCheck = & az account show 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Warn "Pas de session Azure CLI - connexion..."
        & az login
    } else { Write-OK "Azure CLI connecte" }

    if (-not (Test-Path $ZipPath)) {
        Write-Fail "ZIP introuvable : $ZipPath (lancez sans -SkipBuild)"; exit 1
    }

    Write-Host "  → Deploiement vers $AppServiceName..." -ForegroundColor Gray
    & az webapp deployment source config-zip `
        --resource-group $ResourceGroup `
        --name $AppServiceName `
        --src $ZipPath `
        --output table

    if ($LASTEXITCODE -ne 0) { Write-Fail "Deploiement echoue"; exit 1 }
    Write-OK "Deploiement reussi"

    Write-Host "  → Redemarrage..." -ForegroundColor Gray
    & az webapp restart --resource-group $ResourceGroup --name $AppServiceName 2>&1 | Out-Null
    Write-OK "App Service redemarre"
}

# --- ETAPE 3 : SQL -----------------------------------------------------------
if (-not $SkipSql) {
    Write-Step "MIGRATIONS SQL AZURE"

    Write-Host "  → GestionStockDB : colonnes Identity..." -ForegroundColor Gray
    $sqlIdentity = @'
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('AspNetUsers') AND name='TenantCode')
    ALTER TABLE AspNetUsers ADD TenantCode nvarchar(20) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('AspNetUsers') AND name='EstActif')
    ALTER TABLE AspNetUsers ADD EstActif bit NOT NULL DEFAULT 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('AspNetUsers') AND name='FirstName')
    ALTER TABLE AspNetUsers ADD FirstName nvarchar(100) NOT NULL DEFAULT '';
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('AspNetUsers') AND name='LastName')
    ALTER TABLE AspNetUsers ADD LastName nvarchar(100) NOT NULL DEFAULT '';
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('AspNetUsers') AND name='CreatedAt')
    ALTER TABLE AspNetUsers ADD CreatedAt datetime2 NOT NULL DEFAULT GETUTCDATE();
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('AspNetUsers') AND name='DerniereConnexion')
    ALTER TABLE AspNetUsers ADD DerniereConnexion datetime2 NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('AspNetUsers') AND name='TwoFactorEnabled2')
    ALTER TABLE AspNetUsers ADD TwoFactorEnabled2 bit NOT NULL DEFAULT 0;
'@
    Invoke-Sql -database $DbDefault -sql $sqlIdentity -label "colonnes Identity"

    $sqlTenant = @'
UPDATE AspNetUsers SET TenantCode = 'GOUN'
WHERE Email = 'charlesagounke@gmail.com'
  AND (TenantCode IS NULL OR TenantCode <> 'GOUN');
UPDATE AspNetUsers SET TenantCode = NULL
WHERE Email = 'admin@gestionstock.com';
'@
    Invoke-Sql -database $DbDefault -sql $sqlTenant -label "TenantCode"

    Write-Host "  → GestionStockMaster : table Tenants..." -ForegroundColor Gray
    $sqlTenants = @'
IF NOT EXISTS (SELECT 1 FROM sysobjects WHERE name='Tenants' AND xtype='U')
CREATE TABLE Tenants (
    Id            uniqueidentifier NOT NULL PRIMARY KEY DEFAULT NEWID(),
    Code          nvarchar(20)     NOT NULL UNIQUE,
    RaisonSociale nvarchar(200)    NOT NULL,
    Email         nvarchar(256)    NOT NULL,
    AdminNom      nvarchar(200)    NOT NULL,
    BaseDeDonnees nvarchar(100)    NOT NULL,
    EstActif      bit              NOT NULL DEFAULT 1,
    DateCreation  datetime2        NOT NULL DEFAULT GETUTCDATE()
);
IF NOT EXISTS (SELECT 1 FROM Tenants WHERE Code = 'GOUN')
    INSERT INTO Tenants (Code, RaisonSociale, Email, AdminNom, BaseDeDonnees)
    VALUES ('GOUN', 'Charles Agounke SARL', 'charlesagounke@gmail.com',
            'Charles Agounke', 'GestionStock_GOUN');
'@
    Invoke-Sql -database $DbMaster -sql $sqlTenants -label "Tenants + GOUN"

    Write-Host "  → Migration des bases tenant..." -ForegroundColor Gray
    $tenantsTable = Invoke-SqlQuery -database $DbMaster `
        -sql "SELECT Code, BaseDeDonnees FROM Tenants WHERE EstActif = 1 ORDER BY Code"

    if ($tenantsTable.Rows.Count -eq 0) {
        Write-Warn "Aucun tenant actif"
    } else {
        foreach ($row in $tenantsTable.Rows) {
            $code = $row["Code"]
            $db   = $row["BaseDeDonnees"]
            $dbExists = Invoke-SqlScalar -database "master" `
                -sql "SELECT COUNT(1) FROM sys.databases WHERE name = '$db'"
            if ($null -eq $dbExists -or [int]$dbExists -eq 0) {
                Write-Warn "Base $db introuvable - ignoree"
                continue
            }
            Invoke-TenantMigration -dbTenant $db -tenantCode $code
        }
        Write-OK "$($tenantsTable.Rows.Count) base(s) tenant migreee(s)"
    }

    Write-Host ""
    Write-Host "  Tenants actifs :" -ForegroundColor Gray
    $t = Invoke-SqlQuery -database $DbMaster `
        -sql "SELECT Code, RaisonSociale, Email, BaseDeDonnees FROM Tenants WHERE EstActif=1 ORDER BY DateCreation"
    foreach ($r in $t.Rows) {
        Write-Info "[$($r['Code'])] $($r['RaisonSociale']) - $($r['Email']) - $($r['BaseDeDonnees'])"
    }

    Write-Host ""
    Write-Host "  Utilisateurs GestionStockDB :" -ForegroundColor Gray
    $u = Invoke-SqlQuery -database $DbDefault `
        -sql "SELECT Email, ISNULL(TenantCode,'(super-admin)') AS TC FROM AspNetUsers ORDER BY Email"
    foreach ($r in $u.Rows) {
        Write-Info "$($r['Email'])  ->  $($r['TC'])"
    }
}

# --- ETAPE 4 : HEALTH CHECK --------------------------------------------------
Write-Step "VERIFICATION SANTE"

if (-not $SkipDeploy) {
    Write-Host "  → Attente demarrage App Service (40s)..." -ForegroundColor Gray
    Start-Sleep -Seconds 40
}

try {
    $r1 = Invoke-WebRequest -Uri "$AppUrl/health" -TimeoutSec 30 -UseBasicParsing
    Write-OK "Health : HTTP $($r1.StatusCode)"
} catch { Write-Warn "Health inaccessible : $($_.Exception.Message)" }

try {
    $r2 = Invoke-WebRequest -Uri "$AppUrl/api/auth/ping" -TimeoutSec 15 -UseBasicParsing
    Write-OK "API ping : HTTP $($r2.StatusCode)"
} catch { Write-Warn "API ping inaccessible" }

# --- RESUME ------------------------------------------------------------------
$elapsed = (Get-Date) - $startTime
Write-Host ""
Write-Host "====================================================" -ForegroundColor Green
Write-Host ("  Termine en {0}s" -f [math]::Round($elapsed.TotalSeconds)) -ForegroundColor Green
Write-Host "====================================================" -ForegroundColor Green
Write-Host "  URL     : $AppUrl"
Write-Host "  Swagger : $AppUrl/swagger"
Write-Host ""
Write-Host "  Comptes :"
Write-Host "    admin@gestionstock.com   -> super-admin (GestionStockDB)"
Write-Host "    charlesagounke@gmail.com -> GOUN (GestionStock_GOUN)"
Write-Host ""
