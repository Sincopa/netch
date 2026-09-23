Set-Location (Split-Path $MyInvocation.MyCommand.Path -Parent)

try {
    Invoke-WebRequest `
        -Uri 'https://www.wintun.net/builds/wintun-0.14.1.zip' `
        -OutFile 'wintun.zip'

    $actualHash = (Get-FileHash 'wintun.zip' -Algorithm SHA256).Hash.ToLowerInvariant()
    $expectedHash = '07c256185d6ee3652e09fa55c0b673e2624b565e02c4b9091c79ca7d2f24ef51'
    if ($actualHash -ne $expectedHash) {
        throw "Wintun archive SHA256 mismatch: $actualHash"
    }
}
catch {
    exit 1
}

7z x 'wintun.zip'
if ( -Not $? ) { exit $lastExitCode }

mv -Force 'wintun\bin\amd64\wintun.dll' '..\release\wintun.dll'

rm -Recurse -Force 'wintun'
rm -Recurse -Force 'wintun.zip'
exit 0
