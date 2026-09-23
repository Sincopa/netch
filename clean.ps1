# Remove generated project outputs only. Installed copies and user data are preserved.
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$projectPaths = @('src/Netch', 'src/native/Redirector', 'src/native/RouteHelper', 'tests/Netch.Tests', 'tests/RedirectorTester')
$generatedPaths = @('.vs', 'TestResults', 'src/Netch.WebUI/dist')
foreach ($projectPath in $projectPaths) {
    $generatedPaths += "$projectPath/bin", "$projectPath/obj"
}
foreach ($relativePath in $generatedPaths) {
    $targetPath = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $relativePath))
    if (!$targetPath.StartsWith($repositoryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean a path outside the repository: $targetPath"
    }
    if (Test-Path -LiteralPath $targetPath) { Remove-Item -LiteralPath $targetPath -Recurse -Force }
}
