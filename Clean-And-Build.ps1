# GestionStock - Nettoyage complet et reconstruction
# Exécuter: clic droit → "Exécuter avec PowerShell"
# OU depuis le terminal: .\Clean-And-Build.ps1

$root = $PSScriptRoot
Write-Host "=== GestionStock Clean & Rebuild ===" -ForegroundColor Cyan
Write-Host "Dossier: $root" -ForegroundColor Gray
Write-Host ""

# 1. Fermer Visual Studio si ouvert (optionnel)
# Stop-Process -Name devenv -ErrorAction SilentlyContinue

# 2. Supprimer tous les dossiers bin et obj
Write-Host "Suppression des dossiers bin/ et obj/..." -ForegroundColor Yellow
$deleted = 0
Get-ChildItem -Path $root -Recurse -Directory |
    Where-Object { $_.Name -eq "bin" -or $_.Name -eq "obj" } |
    ForEach-Object {
        Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "  ✓ $($_.FullName)" -ForegroundColor Gray
        $deleted++
    }
Write-Host "$deleted dossiers supprimés." -ForegroundColor Green
Write-Host ""

# 3. Restaurer les packages NuGet
Write-Host "Restauration des packages NuGet..." -ForegroundColor Yellow
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnet) {
    & dotnet restore "$root\GestionStock.sln" --verbosity quiet
    Write-Host "Packages restaurés." -ForegroundColor Green
} else {
    Write-Host "dotnet CLI non trouvé - restauration via Visual Studio nécessaire." -ForegroundColor Red
}
Write-Host ""

Write-Host "=== PRÊT ===" -ForegroundColor Green
Write-Host "Ouvrez Visual Studio et faites:" -ForegroundColor Cyan
Write-Host "  Build → Regénérer la solution (Ctrl+Shift+B)" -ForegroundColor White
Write-Host ""
Read-Host "Appuyez sur Entrée pour fermer"
