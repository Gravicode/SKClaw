#!/usr/bin/env pwsh
# setup.ps1 - SKClaw Setup Script
# Run: .\setup.ps1

$ErrorActionPreference = "Stop"

Write-Host "🦞 SKClaw Setup" -ForegroundColor Cyan
Write-Host "===============" -ForegroundColor Cyan

# Check .NET 9
$dotnetVersion = dotnet --version 2>$null
if (-not $dotnetVersion -or -not $dotnetVersion.StartsWith("9.")) {
    Write-Host "❌ .NET 9 SDK required. Install from: https://dot.net" -ForegroundColor Red
    exit 1
}
Write-Host "✅ .NET $dotnetVersion detected" -ForegroundColor Green

# Copy app.config to all projects that need it
$projects = @(
    "src/SKClaw.CLI",
    "src/SKClaw.Web",
    "src/SKClaw.MCP"
)

foreach ($proj in $projects) {
    $dest = "$proj/app.config"
    if (-not (Test-Path $dest)) {
        Copy-Item "app.config" $dest
        Write-Host "✅ Copied app.config → $dest" -ForegroundColor Green
    } else {
        Write-Host "⚠️  $dest already exists (skipped)" -ForegroundColor Yellow
    }
}

# Restore NuGet packages
Write-Host "`nRestoring NuGet packages..." -ForegroundColor Cyan
dotnet restore SKClaw.sln

# Build solution
Write-Host "`nBuilding solution..." -ForegroundColor Cyan
dotnet build SKClaw.sln -c Debug --no-restore

Write-Host "`n✅ Setup complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor White
Write-Host "1. Edit app.config and set your API keys" -ForegroundColor Gray
Write-Host "2. Run CLI:  cd src/SKClaw.CLI && dotnet run -- chat" -ForegroundColor Gray
Write-Host "3. Run Web:  cd src/SKClaw.Web && dotnet run" -ForegroundColor Gray
Write-Host "4. Docker:   docker-compose up -d" -ForegroundColor Gray
Write-Host ""
Write-Host "Web Chat:    http://localhost:5000/chat" -ForegroundColor Cyan
Write-Host "Admin:       http://localhost:5000/admin" -ForegroundColor Cyan
Write-Host "API:         http://localhost:5000/api" -ForegroundColor Cyan
Write-Host "MCP SSE:     http://localhost:5000/mcp/sse" -ForegroundColor Cyan
