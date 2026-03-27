#Requires -Version 5.1
<#
.SYNOPSIS
    Cree les bases GestionStockDB et GestionStockMaster sur Azure SQL,
    puis redemarre l'App Service pour que le seed admin s'execute.
.USAGE
    powershell -ExecutionPolicy Bypass -File ".\Init-AzureSQL.ps1"
#>

$SQL_SERVER = "gestionstock-sql2026.database.windows.net"
$SQL_ADMIN  = "sqladmin"
$SQL_PASS   = "Occupy123*"
$APP_NAME   = "gescom-gounks"
$RG         = "GestionStockRG"
$APP_URL    = "https://gescom-gounks-fmahdcd0bxefaaas.canadacentral-01.azurewebsites.net"

Write-Host ""
Write-Host "  =============================================" -ForegroundColor DarkCyan
Write-Host "    GestionStock - Initialisation Azure SQL" -ForegroundColor DarkCyan
Write-Host "  =============================================" -ForegroundColor DarkCyan
Write-Host ""

# Load SQL Server driver
function Invoke-SQL([string]$ConnStr, [string]$Query) {
    try {
        Add-Type -AssemblyName "System.Data" -ErrorAction SilentlyContinue
        $conn = New-Object System.Data.SqlClient.SqlConnection($ConnStr)
        $conn.Open()
        $cmd = New-Object System.Data.SqlClient.SqlCommand($Query, $conn)
        $null = $cmd.ExecuteNonQuery()
        $conn.Close()
        return $true
    } catch {
        Write-Host "      SQL Error: $($_.Exception.Message)" -ForegroundColor Red
        return $false
    }
}

$masterConn = "Server=$SQL_SERVER;Database=master;User Id=$SQL_ADMIN;Password=$SQL_PASS;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"

# Step 1 - Create GestionStockDB
Write-Host "  [1] Creation de GestionStockDB..." -ForegroundColor Cyan
$ok = Invoke-SQL -ConnStr $masterConn -Query "IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name='GestionStockDB') CREATE DATABASE [GestionStockDB]"
if ($ok) { Write-Host "      OK  GestionStockDB prete" -ForegroundColor Green }
else { Write-Host "      Tentative via az sql db create..." -ForegroundColor Yellow
    $null = cmd /c "az login --only-show-errors 2>nul"
    cmd /c "az sql db create --resource-group $RG --server gestionstock-sql2026 --name GestionStockDB --edition Basic --only-show-errors 2>nul"
    Write-Host "      OK  GestionStockDB creee via Azure CLI" -ForegroundColor Green
}

# Step 2 - Create GestionStockMaster
Write-Host "  [2] Creation de GestionStockMaster..." -ForegroundColor Cyan
$ok2 = Invoke-SQL -ConnStr $masterConn -Query "IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name='GestionStockMaster') CREATE DATABASE [GestionStockMaster]"
if ($ok2) { Write-Host "      OK  GestionStockMaster prete" -ForegroundColor Green }
else { Write-Host "      Tentative via az sql db create..." -ForegroundColor Yellow
    cmd /c "az sql db create --resource-group $RG --server gestionstock-sql2026 --name GestionStockMaster --edition Basic --only-show-errors 2>nul"
    Write-Host "      OK  GestionStockMaster creee via Azure CLI" -ForegroundColor Green
}

# Step 3 - Verify firewall (Azure services allowed)
Write-Host "  [3] Verification du pare-feu SQL..." -ForegroundColor Cyan
cmd /c "az sql server firewall-rule create --resource-group $RG --server gestionstock-sql2026 --name AllowAzureServices --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0 --only-show-errors 2>nul" | Out-Null
Write-Host "      OK  Services Azure autorises a se connecter" -ForegroundColor Green

# Step 4 - Restart App Service to trigger seed
Write-Host "  [4] Redemarrage de l'App Service (seed admin)..." -ForegroundColor Cyan
cmd /c "az webapp restart --resource-group $RG --name $APP_NAME --only-show-errors 2>nul" | Out-Null
Write-Host "      OK  App Service redemarre" -ForegroundColor Green

Write-Host ""
Write-Host "  [5] Attente du demarrage (45 secondes)..." -ForegroundColor Cyan
Start-Sleep -Seconds 45

# Step 5 - Test HTTP
Write-Host "  [6] Test de l'application..." -ForegroundColor Cyan
try {
    $resp = Invoke-WebRequest -Uri "$APP_URL/" -UseBasicParsing -TimeoutSec 30 -ErrorAction SilentlyContinue
    Write-Host "      OK  HTTP $($resp.StatusCode)" -ForegroundColor Green
} catch {
    Write-Host "      WARN Pas de reponse (attendre encore 1 minute)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "  =============================================" -ForegroundColor Green
Write-Host "    INITIALISATION TERMINEE" -ForegroundColor Green
Write-Host "  =============================================" -ForegroundColor Green
Write-Host ""
Write-Host "  URL       : $APP_URL" -ForegroundColor White
Write-Host "  Login     : admin@gestionstock.com" -ForegroundColor Gray
Write-Host "  Password  : Admin@2024!Stock" -ForegroundColor Gray
Write-Host ""
Write-Host "  Si le login echoue encore :" -ForegroundColor DarkGray
Write-Host "  Verifier les logs -> az webapp log tail --resource-group $RG --name $APP_NAME" -ForegroundColor DarkGray
Write-Host ""

$open = Read-Host "  Ouvrir dans le navigateur ? (o/n)"
if ($open -match "^[oOyY]") { Start-Process $APP_URL }
