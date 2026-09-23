$ErrorActionPreference = 'Stop'
$version = 'v2.7.0'
$hash = 'c5d46e9452f6c9cc7c15ab9158d6d6a0169ceecd6bca019ce476b49337d2be43'
$cache = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../.cache'))
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$zip = Join-Path $cache "tun2socks-$version.zip"
if (!(Test-Path -LiteralPath $zip) -or (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $hash) {
    Invoke-WebRequest "https://github.com/xjasonlyu/tun2socks/releases/download/$version/tun2socks-windows-amd64.zip" -OutFile $zip
}
if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $hash) { throw 'tun2socks archive checksum mismatch.' }
Expand-Archive -LiteralPath $zip -DestinationPath (Join-Path $PSScriptRoot 'release') -Force
