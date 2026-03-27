#Requires -Version 5.1
param(
    [string]$SourcePath     = $PSScriptRoot,
    [switch]$SkipBuild,
    [switch]$SkipAzureLogin
)

$ErrorActionPreference = "Stop"

$RESOURCE_GROUP = "GestionStockRG"
$APP_NAME       = "gescom-gounks"
$APP_URL        = "https://gescom-gounks-fmahdcd0bxefaaas.canadacentral-01.azurewebsites.net"
$SQL_SERVER     = "gestionstock-sql2026.database.windows.net"
$SQL_ADMIN      = "sqladmin"
$SQL_PASS       = "Occupy123*"

$PUBLISH_DIR    = Join-Path $env:TEMP ("GestStock_pub_" + (Get-Date -Format "yyyyMMddHHmmss"))
$DEPLOY_ZIP     = Join-Path $env:TEMP "GestStock_deploy.zip"
$API_CSPROJ     = Join-Path $SourcePath "src\GestionStock.API\GestionStock.API.csproj"

function Write-Step([int]$n, [string]$msg) {
    Write-Host ""
    Write-Host "  [$n] $msg" -ForegroundColor Cyan
}
function Write-OK([string]$msg)   { Write-Host "      OK  $msg" -ForegroundColor Green }
function Write-Info([string]$msg) { Write-Host "      --> $msg" -ForegroundColor Gray }
function Write-Warn([string]$msg) { Write-Host "      WARN $msg" -ForegroundColor Yellow }
function Write-Fail([string]$msg) {
    Write-Host ""
    Write-Host "  ERREUR : $msg" -ForegroundColor Red
    exit 1
}

# Run az command and capture stdout only (suppresses 32-bit Python warning on stderr)
function Invoke-Az([string]$Args) {
    $result = cmd /c "az $Args 2>nul"
    return $result
}

Clear-Host
Write-Host ""
Write-Host "  =======================================================" -ForegroundColor DarkCyan
Write-Host "    GestionStock - Deploiement Azure App Service" -ForegroundColor DarkCyan
Write-Host "    App : $APP_NAME" -ForegroundColor DarkCyan
Write-Host "  =======================================================" -ForegroundColor DarkCyan
Write-Host ""

# -----------------------------------------------------------------------
# STEP 1 - Prerequisites
# -----------------------------------------------------------------------
Write-Step 1 "Verification des prerequis"

$dotnetVer = dotnet --version 2>$null
if (-not $dotnetVer) {
    Write-Fail ".NET SDK introuvable. Installer depuis https://dotnet.microsoft.com/download/dotnet/8.0"
}
$major = [int]($dotnetVer.Split(".")[0])
if ($major -lt 8) { Write-Fail ".NET 8 requis. Version installee : $dotnetVer" }
Write-OK ".NET SDK $dotnetVer"

$azTest = Invoke-Az "--version"
if (-not $azTest) {
    Write-Fail "Azure CLI introuvable. Installer depuis https://aka.ms/installazurecliwindows"
}
Write-OK "Azure CLI detecte"

if (-not (Test-Path $API_CSPROJ)) {
    Write-Fail "Projet API introuvable : $API_CSPROJ"
}
Write-OK "Projet API trouve"

# -----------------------------------------------------------------------
# STEP 2 - Azure login
# -----------------------------------------------------------------------
Write-Step 2 "Connexion Azure"

if ($SkipAzureLogin) {
    $account = Invoke-Az "account show --query user.name -o tsv"
    if (-not $account) {
        Write-Info "Session expiree, relancement az login..."
        cmd /c "az login --only-show-errors 2>nul" | Out-Null
        $account = Invoke-Az "account show --query user.name -o tsv"
        if (-not $account) { Write-Fail "Echec de la connexion Azure." }
    }
    Write-OK "Deja connecte : $account"
} else {
    Write-Info "Ouverture du navigateur pour authentification Azure..."
    cmd /c "az login --only-show-errors 2>nul" | Out-Null
    $account = Invoke-Az "account show --query user.name -o tsv"
    if (-not $account) { Write-Fail "Echec de la connexion Azure." }
    Write-OK "Connecte : $account"
}

Write-Info "Verification de l'App Service..."
$appCheck = Invoke-Az "webapp show --resource-group $RESOURCE_GROUP --name $APP_NAME --query name -o tsv --only-show-errors"
if (-not $appCheck) {
    Write-Fail "App Service '$APP_NAME' introuvable dans '$RESOURCE_GROUP'."
}
Write-OK "App Service confirme : $appCheck"

# -----------------------------------------------------------------------
# STEP 3 - Build
# -----------------------------------------------------------------------
Write-Step 3 "Compilation et publication .NET"

if ($SkipBuild) {
    $PUBLISH_DIR = Join-Path $SourcePath "publish"
    if (-not (Test-Path $PUBLISH_DIR)) { Write-Fail "Dossier publish introuvable. Retirez -SkipBuild." }
    Write-OK "Build ignore - utilisation de : $PUBLISH_DIR"
} else {
    if (Test-Path $PUBLISH_DIR) { Remove-Item $PUBLISH_DIR -Recurse -Force }
    Write-Info "dotnet publish Release / linux-x64..."

    dotnet publish $API_CSPROJ `
        --configuration Release `
        --output $PUBLISH_DIR `
        --nologo `
        --verbosity minimal

    if ($LASTEXITCODE -ne 0) { Write-Fail "Echec de la compilation." }
    Write-OK "Publication reussie"

    $sizeKB = [math]::Round(
        (Get-ChildItem $PUBLISH_DIR -Recurse | Measure-Object -Property Length -Sum).Sum / 1KB
    )
    Write-Info "Taille publish : $($sizeKB.ToString('N0')) KB"
}

# -----------------------------------------------------------------------
# STEP 4 - App Settings
# -----------------------------------------------------------------------
Write-Step 4 "Configuration App Settings Azure"

$connDefault = "Server=$SQL_SERVER;Database=GestionStockDB;User Id=$SQL_ADMIN;Password=$SQL_PASS;MultipleActiveResultSets=true;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
$connMaster  = "Server=$SQL_SERVER;Database=GestionStockMaster;User Id=$SQL_ADMIN;Password=$SQL_PASS;MultipleActiveResultSets=true;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
$connBase    = "Server=$SQL_SERVER;User Id=$SQL_ADMIN;Password=$SQL_PASS;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"

Write-Info "Mise a jour des App Settings..."

$settings = @(
    "ASPNETCORE_ENVIRONMENT=Production",
    "ConnectionStrings__DefaultConnection=$connDefault",
    "ConnectionStrings__MasterConnection=$connMaster",
    "ConnectionStrings__SqlServerBase=$connBase",
    "JwtSettings__SecretKey=GestionStock_SuperSecretKey_2024_AuMoins32Caracteres!",
    "JwtSettings__Issuer=GestionStockAPI",
    "JwtSettings__Audience=GestionStockClients",
    "JwtSettings__ExpirationHeures=8"
)

$settingsArgs = ($settings | ForEach-Object { "`"$_`"" }) -join " "
$cmd = "webapp config appsettings set --resource-group $RESOURCE_GROUP --name $APP_NAME --only-show-errors --settings $settingsArgs"
cmd /c "az $cmd 2>nul" | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Fail "Echec de la mise a jour des App Settings." }
Write-OK "App Settings mis a jour"

Write-Info "Configuration runtime Linux .NET 8..."
cmd /c "az webapp config set --resource-group $RESOURCE_GROUP --name $APP_NAME --linux-fx-version DOTNETCORE|8.0 --only-show-errors 2>nul" | Out-Null
Write-OK "Runtime configure : DOTNETCORE|8.0"

# -----------------------------------------------------------------------
# STEP 5 - Create zip
# -----------------------------------------------------------------------
Write-Step 5 "Creation du zip de deploiement"

if (Test-Path $DEPLOY_ZIP) { Remove-Item $DEPLOY_ZIP -Force }
Write-Info "Compression en cours..."
Compress-Archive -Path "$PUBLISH_DIR\*" -DestinationPath $DEPLOY_ZIP -Force

$zipMB = [math]::Round((Get-Item $DEPLOY_ZIP).Length / 1MB, 1)
Write-OK "Zip cree : $zipMB MB"

# -----------------------------------------------------------------------
# STEP 6 - Deploy
# -----------------------------------------------------------------------
Write-Step 6 "Deploiement ZIP sur Azure"

Write-Info "Upload en cours (1-3 minutes)..."
cmd /c "az webapp deploy --resource-group $RESOURCE_GROUP --name $APP_NAME --src-path `"$DEPLOY_ZIP`" --type zip --only-show-errors 2>nul"
if ($LASTEXITCODE -ne 0) { Write-Fail "Echec du deploiement zip." }
Write-OK "Deploiement termine"

# -----------------------------------------------------------------------
# STEP 7 - Restart and verify
# -----------------------------------------------------------------------
Write-Step 7 "Redemarrage et verification"

Write-Info "Redemarrage de l'application..."
cmd /c "az webapp restart --resource-group $RESOURCE_GROUP --name $APP_NAME --only-show-errors 2>nul" | Out-Null
Write-OK "App Service redemarre"

Write-Info "Attente du demarrage (30 secondes)..."
Start-Sleep -Seconds 30

Write-Info "Test HTTP..."
try {
    $resp = Invoke-WebRequest -Uri "$APP_URL/" -UseBasicParsing -TimeoutSec 30 -ErrorAction SilentlyContinue
    if ($resp.StatusCode -lt 500) {
        Write-OK "Application repond - HTTP $($resp.StatusCode)"
    } else {
        Write-Warn "HTTP $($resp.StatusCode) - verifier les logs"
    }
} catch {
    Write-Warn "Pas de reponse immediate (normal au 1er demarrage - attendre 1 minute)"
}

# Cleanup
if ((-not $SkipBuild) -and (Test-Path $PUBLISH_DIR)) {
    Remove-Item $PUBLISH_DIR -Recurse -Force -ErrorAction SilentlyContinue
}
if (Test-Path $DEPLOY_ZIP) { Remove-Item $DEPLOY_ZIP -Force -ErrorAction SilentlyContinue }

Write-Host ""
Write-Host "  =======================================================" -ForegroundColor Green
Write-Host "    DEPLOIEMENT REUSSI" -ForegroundColor Green
Write-Host "  =======================================================" -ForegroundColor Green
Write-Host ""
Write-Host "  URL   : $APP_URL" -ForegroundColor White
Write-Host "  Admin : admin@gestionstock.com  /  Admin@2024!Stock" -ForegroundColor Gray
Write-Host ""
Write-Host "  Logs : az webapp log tail --resource-group $RESOURCE_GROUP --name $APP_NAME" -ForegroundColor DarkGray
Write-Host ""

$open = Read-Host "  Ouvrir dans le navigateur ? (o/n)"
if ($open -match "^[oOyY]") { Start-Process $APP_URL }
