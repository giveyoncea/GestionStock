# Fix-CircularRef.ps1
# Corrige l'erreur "Cycle détecté" en nettoyant les caches de build
# Executer depuis le dossier de la solution

$SolutionPath = "C:\Users\Charly\source\repos\GestionStock"

Write-Host "Nettoyage des dossiers obj et bin..." -ForegroundColor Cyan

Get-ChildItem -Path $SolutionPath -Include "obj","bin" -Recurse -Directory |
    Where-Object { $_.FullName -notlike "*\.git*" } |
    ForEach-Object {
        Write-Host "  Suppression : $($_.FullName)" -ForegroundColor Gray
        Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }

Write-Host "Restauration NuGet..." -ForegroundColor Cyan
Set-Location $SolutionPath
& dotnet restore

Write-Host "Build..." -ForegroundColor Cyan
& dotnet build --no-restore

Write-Host "Termine." -ForegroundColor Green
