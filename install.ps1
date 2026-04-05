# Clustral CLI installer for Windows.
#
# Usage:
#   irm https://raw.githubusercontent.com/Clustral/clustral/main/install.ps1 | iex

$ErrorActionPreference = "Stop"

$repo = "Clustral/clustral"
$asset = "clustral-windows-amd64.exe"
$installDir = "$env:LOCALAPPDATA\Clustral"

Write-Host "Fetching latest release..."
$release = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest"
$tag = $release.tag_name
$url = ($release.assets | Where-Object { $_.name -eq $asset }).browser_download_url

if (-not $url) {
    Write-Error "Could not find $asset in release $tag."
    exit 1
}

Write-Host "Installing clustral $tag..."
Write-Host "  -> $installDir\clustral.exe"

New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Invoke-WebRequest -Uri $url -OutFile "$installDir\clustral.exe"

# Add to user PATH if not already there.
$userPath = [Environment]::GetEnvironmentVariable("PATH", "User")
if ($userPath -notlike "*$installDir*") {
    [Environment]::SetEnvironmentVariable("PATH", "$userPath;$installDir", "User")
    Write-Host "  Added $installDir to PATH (restart your terminal to use 'clustral' directly)."
}

Write-Host ""
Write-Host "clustral installed successfully!"
& "$installDir\clustral.exe" --version
Write-Host ""
Write-Host "Get started:"
Write-Host "  clustral login <your-controlplane-url>"
