$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$project = Join-Path $PSScriptRoot 'src/Taitai.Desktop/Taitai.Desktop.csproj'
$destination = Join-Path $PSScriptRoot 'dist/Taitai'

dotnet publish $project -c Release -o $destination --nologo -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
foreach ($name in @('README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $destination
}
Write-Output "Built: $destination"
