param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Artifacts = Join-Path $Root 'artifacts\publish'

if (Test-Path $Artifacts) {
    $resolved = [IO.Path]::GetFullPath($Artifacts)
    $expectedRoot = [IO.Path]::GetFullPath((Join-Path $Root 'artifacts'))
    if (-not $resolved.StartsWith($expectedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝清理意外路径：$resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

dotnet run --project (Join-Path $Root 'tools\IconBuilder\IconBuilder.csproj') -c Release -- (Join-Path $Root 'assets')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet publish (Join-Path $Root 'src\BS2Lite\BS2Lite.csproj') -c $Configuration -o $Artifacts
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "构建完成：$Artifacts"
