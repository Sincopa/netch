param (
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [ValidateNotNullOrEmpty()][string]$OutputPath = 'artifacts\Netch',
    [bool]$SelfContained = $True,
    [bool]$PublishSingleFile = $True,
    [bool]$PublishReadyToRun = $False,
    [switch]$SkipNativeBuild,
    # Keep data/, logging/, and mode/Custom from an existing OutputPath after packaging.
    # Default is a clean distribution package with no user settings or subscriptions.
    [switch]$PreserveUserData
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$packageRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputPath))
$artifactRoot = Join-Path $repositoryRoot 'artifacts'
$releaseName = [IO.Path]::GetFileName($packageRoot)
$isArtifact = $packageRoot.StartsWith($artifactRoot + '\', [StringComparison]::OrdinalIgnoreCase)
$isRelease = ([IO.Path]::GetDirectoryName($packageRoot) -eq $repositoryRoot) -and ($releaseName -match '^release(?:-[a-zA-Z0-9_-]+)?$')
if (!$isArtifact -and !$isRelease) {
    throw 'OutputPath must be a subdirectory of artifacts or a root release/release-* directory.'
}

function Invoke-BuildStep {
    param([scriptblock]$Action)
    & $Action
    if ($LASTEXITCODE -ne 0) { throw "Build step failed with exit code $LASTEXITCODE." }
}

function Save-UserData {
    param([string]$SourceRoot, [string]$BackupRoot)
    $paths = @(
        @{ Relative = 'data'; Kind = 'Directory' },
        @{ Relative = 'logging'; Kind = 'Directory' },
        @{ Relative = 'mode\Custom'; Kind = 'Directory' }
    )
    $saved = @()
    foreach ($entry in $paths) {
        $source = Join-Path $SourceRoot $entry.Relative
        if (!(Test-Path -LiteralPath $source)) { continue }
        $destination = Join-Path $BackupRoot $entry.Relative
        $destinationParent = Split-Path -Parent $destination
        New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination -Recurse -Force
        $saved += $entry.Relative
    }
    return $saved
}

function Restore-UserData {
    param([string]$BackupRoot, [string]$DestinationRoot, [string[]]$RelativePaths)
    foreach ($relative in $RelativePaths) {
        $source = Join-Path $BackupRoot $relative
        if (!(Test-Path -LiteralPath $source)) { continue }
        $destination = Join-Path $DestinationRoot $relative
        $destinationParent = Split-Path -Parent $destination
        New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
        if (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Recurse -Force }
        Copy-Item -LiteralPath $source -Destination $destination -Recurse -Force
    }
}

Push-Location $repositoryRoot
try {
    # Publish and assemble into staging dirs first, then replace OutputPath atomically.
    $publishStaging = Join-Path $repositoryRoot "artifacts\publish-$Configuration"
    $packageStaging = Join-Path $repositoryRoot "artifacts\package-$Configuration"
    if ($packageRoot -eq $publishStaging -or $packageRoot -eq $packageStaging) {
        throw 'The publish/package staging directories cannot be the package output.'
    }

    Invoke-BuildStep { dotnet publish src/Netch/Netch.csproj -c $Configuration -r win-x64 -p:Platform=x64 "-p:SelfContained=$SelfContained" "-p:PublishSingleFile=$PublishSingleFile" "-p:PublishReadyToRun=$PublishReadyToRun" "-p:IncludeNativeLibrariesForSelfExtract=$SelfContained" -o $publishStaging }

    if (!$SkipNativeBuild) {
        if (!(Test-Path -LiteralPath 'vendor\release')) { Invoke-BuildStep { & ./vendor/build.ps1 } }
        Invoke-BuildStep { msbuild src/native/Redirector/Redirector.vcxproj "-property:Configuration=$Configuration" -property:Platform=x64 }
        Invoke-BuildStep { msbuild src/native/RouteHelper/RouteHelper.vcxproj "-property:Configuration=$Configuration" -property:Platform=x64 }
    }

    $nativeFiles = @(
        "src\native\Redirector\bin\$Configuration\nfapi.dll",
        "src\native\Redirector\bin\$Configuration\Redirector.bin",
        "src\native\RouteHelper\bin\$Configuration\RouteHelper.bin"
    )
    foreach ($nativeFile in $nativeFiles) {
        if (!(Test-Path -LiteralPath $nativeFile)) { throw "Missing native output: $nativeFile. Build native components first." }
    }
    if (!(Test-Path -LiteralPath 'vendor\release')) { throw 'Missing vendor/release. Build external cores first.' }
    if (!(Test-Path -LiteralPath 'vendor/xray/release/xray.exe')) { & ./vendor/xray/build.ps1 }
    if (!(Test-Path -LiteralPath 'vendor/tun2socks/release/tun2socks-windows-amd64.exe')) { & ./vendor/tun2socks/build.ps1 }
    foreach ($coreFile in @('xray.exe', 'geoip.dat', 'geosite.dat', 'LICENSE')) {
        if (!(Test-Path -LiteralPath (Join-Path 'vendor/xray/release' $coreFile))) { throw "Missing Xray runtime file: $coreFile" }
    }

    if (Test-Path -LiteralPath $packageStaging) { Remove-Item -LiteralPath $packageStaging -Recurse -Force }
    New-Item -ItemType Directory -Path $packageStaging -Force | Out-Null
    $packageBin = Join-Path $packageStaging 'bin'
    New-Item -ItemType Directory -Path $packageBin -Force | Out-Null
    Copy-Item -Path (Join-Path $publishStaging '*') -Destination $packageStaging -Recurse -Force
    foreach ($directory in @('i18n', 'mode')) {
        Copy-Item -LiteralPath (Join-Path 'assets/runtime' $directory) -Destination $packageStaging -Recurse -Force
    }
    foreach ($asset in @('stun.txt', 'nfdriver.sys', 'aiodns.conf', 'README.md')) {
        Copy-Item -LiteralPath (Join-Path 'assets/runtime' $asset) -Destination $packageBin -Force
    }
    foreach ($nativeFile in $nativeFiles) { Copy-Item -LiteralPath $nativeFile -Destination $packageBin -Force }
    Get-ChildItem -LiteralPath 'vendor\release' -File | Where-Object { $_.Extension -in '.bin', '.dll', '.exe' } | Copy-Item -Destination $packageBin -Force
    foreach ($coreFile in @('xray.exe', 'geoip.dat', 'geosite.dat')) {
        Copy-Item -LiteralPath (Join-Path 'vendor/xray/release' $coreFile) -Destination $packageBin -Force
    }
    Copy-Item -LiteralPath 'vendor/xray/release/LICENSE' -Destination (Join-Path $packageBin 'LICENSE-Xray.txt') -Force
    Copy-Item -LiteralPath 'vendor/tun2socks/release/tun2socks-windows-amd64.exe' -Destination (Join-Path $packageBin 'tun2socks.exe') -Force
    Copy-Item -LiteralPath 'vendor/tun2socks/LICENSE' -Destination (Join-Path $packageBin 'LICENSE-tun2socks.txt') -Force
    $geoDatabase = Join-Path $packageBin 'GeoLite2-Country.mmdb'
    if (Test-Path -LiteralPath 'assets\runtime\GeoLite2-Country.mmdb') {
        Copy-Item -LiteralPath 'assets\runtime\GeoLite2-Country.mmdb' -Destination $geoDatabase -Force
    } elseif (!(Test-Path -LiteralPath $geoDatabase)) {
        Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/Loyalsoldier/geoip/release/Country.mmdb' -OutFile $geoDatabase
    }

    $userDataBackup = Join-Path $repositoryRoot "artifacts\user-data-backup-$Configuration"
    $preservedPaths = @()
    if ($PreserveUserData -and (Test-Path -LiteralPath $packageRoot)) {
        if (Test-Path -LiteralPath $userDataBackup) { Remove-Item -LiteralPath $userDataBackup -Recurse -Force }
        New-Item -ItemType Directory -Path $userDataBackup -Force | Out-Null
        $preservedPaths = @(Save-UserData -SourceRoot $packageRoot -BackupRoot $userDataBackup)
    }

    if (Test-Path -LiteralPath $packageRoot) { Remove-Item -LiteralPath $packageRoot -Recurse -Force }
    Move-Item -LiteralPath $packageStaging -Destination $packageRoot

    if ($preservedPaths.Count -gt 0) {
        Restore-UserData -BackupRoot $userDataBackup -DestinationRoot $packageRoot -RelativePaths $preservedPaths
        Write-Output ("Preserved user data: " + ($preservedPaths -join ', '))
    }
    if (Test-Path -LiteralPath $userDataBackup) { Remove-Item -LiteralPath $userDataBackup -Recurse -Force }

    Write-Output "Packaged Netch: $packageRoot"
} finally { Pop-Location }
