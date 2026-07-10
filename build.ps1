param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Artifacts = Join-Path $Root 'artifacts\publish'

dotnet run --project (Join-Path $Root 'tools\IconBuilder\IconBuilder.csproj') -c Release -- (Join-Path $Root 'assets')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet publish (Join-Path $Root 'src\BS2Lite\BS2Lite.csproj') -c $Configuration -o $Artifacts
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Copy-Item -LiteralPath (Join-Path $Root 'scripts\管理.ps1') -Destination $Artifacts -Force
Write-Host "构建完成：$Artifacts"
