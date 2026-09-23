# Official pinned Xray binary. The upstream digest is checked before extraction.
$ErrorActionPreference = 'Stop'
$version = 'v26.3.27'
$expectedHash = 'd004c39288ce9ada487c6f398c7c545f7d749e44bdfdd59dbc9f865afba4e1ad'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$cache = Join-Path $repo '.cache'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$archive = Join-Path $cache "Xray-$version-windows-64.zip"
if (!(Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expectedHash) {
    Invoke-WebRequest -Uri "https://github.com/XTLS/Xray-core/releases/download/$version/Xray-windows-64.zip" -OutFile $archive
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Xray archive checksum mismatch.' }
Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $PSScriptRoot 'release') -Force
