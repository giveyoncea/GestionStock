# Deploy-SiteOnly.ps1
param([switch]$SkipBuild)

$SolutionPath   = "C:\Users\Charly\source\repos\GestionStock"
$ApiProject     = "src\GestionStock.API\GestionStock.API.csproj"
$PublishDir     = "C:\Temp\GestionStock-publish"
$ZipPath        = "C:\Temp\GestionStock-publish.zip"
$ResourceGroup  = "GestionStockRG"
$AppServiceName = "gescom-gounks"
$AppUrl         = "https://gescom-gounks-fmahdcd0bxefaaas.canadacentral-01.azurewebsites.net"

function Write-Step([string]$msg) { Write-Host "" ; Write-Host "--- $msg" -ForegroundColor Cyan }
function Write-OK([string]$msg)   { Write-Host "  [OK] $msg" -ForegroundColor Green }
function Write-Info([string]$msg) { Write-Host "       $msg" -ForegroundColor Gray }
function Write-Fail([string]$msg) { Write-Host "  [XX] $msg" -ForegroundColor Red ; exit 1 }
function Filter-Az { param([string[]]$Lines) ; $Lines | Where-Object { ($_ -notmatch 'UserWarning') -and ($_ -notmatch 'Python') } }

$startTime = Get-Date
Write-Host "====================================================" -ForegroundColor Magenta
Write-Host "  GestionStock - Deploiement Site Azure             " -ForegroundColor Magenta
Write-Host "====================================================" -ForegroundColor Magenta
Write-Host ("  Demarre : {0}" -f (Get-Date -Format "dd/MM/yyyy HH:mm:ss"))
$sdkVer = & dotnet --version 2>&1
Write-Host ("  SDK : {0}" -f $sdkVer)

if (-not $SkipBuild) {
    Write-Step "BUILD ET PUBLISH (.NET 9 Release)"

    if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
    New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null

    Write-Info "Nettoyage cache HTTP NuGet..."
    & dotnet nuget locals http-cache --clear 2>&1 | Out-Null

    $apiProj = Join-Path $SolutionPath $ApiProject

    Write-Info "Publish API + Web (tentative 1)..."
    & dotnet publish $apiProj --configuration Release --output $PublishDir --no-self-contained 2>&1 | Out-Null

    if ($LASTEXITCODE -ne 0) {
        Write-Info "Repair workloads + tentative 2..."
        & dotnet workload repair 2>&1 | Out-Null
        & dotnet publish $apiProj --configuration Release --output $PublishDir --no-self-contained
        if ($LASTEXITCODE -ne 0) { Write-Fail "Publish echoue" }
    }

    Write-OK ("Publish OK : {0}" -f $PublishDir)

    if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
    Compress-Archive -Path "$PublishDir\*" -DestinationPath $ZipPath -Force
    $mb = [math]::Round((Get-Item $ZipPath).Length / 1MB, 1)
    Write-OK ("ZIP : {0} ({1} MB)" -f $ZipPath, $mb)
}

Write-Step "DEPLOIEMENT AZURE APP SERVICE"

if (-not (Test-Path $ZipPath)) { Write-Fail "ZIP introuvable - relancez sans -SkipBuild" }

$acctCheck = & az account show 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Info "Connexion Azure CLI..."
    & az login
} else { Write-OK "Azure CLI connecte" }

Write-Info ("Deploiement vers {0}..." -f $AppServiceName)
$out1 = & az webapp deploy --resource-group $ResourceGroup --name $AppServiceName --src-path $ZipPath --type zip --async true 2>&1
Filter-Az $out1

if ($LASTEXITCODE -ne 0) {
    Write-Info "Commande legacy..."
    $out2 = & az webapp deployment source config-zip --resource-group $ResourceGroup --name $AppServiceName --src $ZipPath --output table 2>&1
    Filter-Az $out2
    if ($LASTEXITCODE -ne 0) { Write-Fail "Deploiement echoue" }
}
Write-OK "Deploiement reussi"

Write-Info "Redemarrage App Service..."
$out3 = & az webapp restart --resource-group $ResourceGroup --name $AppServiceName 2>&1
Filter-Az $out3 | Out-Null
Write-OK "App Service redemarre"

Write-Step "VERIFICATION"
Write-Info "Attente demarrage (30s)..."
Start-Sleep -Seconds 30

$healthUri = $AppUrl + "/health"
try {
    $r = Invoke-WebRequest -Uri $healthUri -TimeoutSec 20 -UseBasicParsing
    Write-OK ("Health : HTTP {0}" -f $r.StatusCode)
} catch {
    Write-Host "  [!!] Health inaccessible" -ForegroundColor Yellow
}

$sec = [math]::Round(((Get-Date) - $startTime).TotalSeconds)
Write-Host "====================================================" -ForegroundColor Green
Write-Host ("  Deploye en {0}s" -f $sec) -ForegroundColor Green
Write-Host "====================================================" -ForegroundColor Green
Write-Host ("  URL : {0}" -f $AppUrl)
Write-Host ("  Swagger : {0}/swagger" -f $AppUrl)
Write-Host ""
