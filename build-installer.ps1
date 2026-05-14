<#
    Builds a self-contained Release publish and runs Inno Setup to produce
    dist\DesktopPet-Setup-<version>.exe

    Requires:
      - .NET 9 SDK on PATH (for dotnet)
      - Inno Setup 6 installed (https://jrsoftware.org/isdl.php)

    Usage:
      pwsh -File build-installer.ps1
#>

$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    Write-Host "==> dotnet publish (Release, win-x64, self-contained)..." -ForegroundColor Cyan
    dotnet publish -c Release -r win-x64 --self-contained true -nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

    $iscc = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1

    if (-not $iscc) {
        throw "Inno Setup 6 not found. Install from https://jrsoftware.org/isdl.php (or 'winget install JRSoftware.InnoSetup')."
    }

    Write-Host "==> Compiling installer with $iscc..." -ForegroundColor Cyan
    & $iscc installer.iss
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed (exit $LASTEXITCODE)" }

    $output = Get-ChildItem -Path dist -Filter 'DesktopPet-Setup-*.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($output) {
        $sizeMB = [math]::Round($output.Length / 1MB, 1)
        Write-Host "==> Installer: $($output.FullName) ($sizeMB MB)" -ForegroundColor Green
    }
} finally {
    Pop-Location
}
