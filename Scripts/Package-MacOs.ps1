param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,
    [Parameter(Mandatory = $true)]
    [ValidateSet('osx-arm64', 'osx-x64')]
    [string]$RuntimeIdentifier
)

$ErrorActionPreference = 'Stop'
if ($IsMacOS -eq $false) {
    throw 'macOS packaging requires a macOS host with the Xcode command line tools.'
}
$repositoryDirectory = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$publishPath = (Resolve-Path -LiteralPath $PublishDirectory).Path
$executablePath = Join-Path $publishPath 'Eidolon'
if ((Test-Path -LiteralPath $executablePath -PathType Leaf) -eq $false) {
    throw 'The self-contained single-file Eidolon executable is missing.'
}
if (Test-Path -LiteralPath (Join-Path $publishPath 'Eidolon.dll')) {
    throw 'The publish directory is not a single-file deployment.'
}
$outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($outputPath) | Out-Null
$stagingPath = Join-Path $outputPath ('package-' + $RuntimeIdentifier)
$archivePath = Join-Path $outputPath ('Eidolon-v' + $Version + '-' + $RuntimeIdentifier + '.app.zip')
if (Test-Path -LiteralPath $stagingPath) {
    throw 'The package staging directory already exists; refusing to overwrite it.'
}
if (Test-Path -LiteralPath $archivePath) {
    throw 'The output archive already exists; refusing to overwrite it.'
}
$bundlePath = Join-Path $stagingPath 'Eidolon.app'
$contentsPath = Join-Path $bundlePath 'Contents'
$macOsPath = Join-Path $contentsPath 'MacOS'
$resourcesPath = Join-Path $contentsPath 'Resources'
$iconSetPath = Join-Path $stagingPath 'AppIcon.iconset'
[System.IO.Directory]::CreateDirectory($macOsPath) | Out-Null
[System.IO.Directory]::CreateDirectory($resourcesPath) | Out-Null
[System.IO.Directory]::CreateDirectory($iconSetPath) | Out-Null
Copy-Item -LiteralPath $executablePath -Destination (Join-Path $macOsPath 'Eidolon')
$plistPath = Join-Path $contentsPath 'Info.plist'
Copy-Item -LiteralPath (Join-Path $repositoryDirectory 'Eidolon.App/Assets/macOS/Info.plist') -Destination $plistPath
& /usr/libexec/PlistBuddy -c ('Set :CFBundleShortVersionString ' + $Version) $plistPath
if ($LASTEXITCODE -ne 0) { throw 'Setting the application version failed.' }
& /usr/libexec/PlistBuddy -c ('Set :CFBundleVersion ' + $Version) $plistPath
if ($LASTEXITCODE -ne 0) { throw 'Setting the bundle version failed.' }

$iconPath = Join-Path $repositoryDirectory 'Eidolon.App/Assets/AppIcon.png'
foreach ($iconSize in @(16, 32, 128, 256, 512)) {
    & /usr/bin/sips -z $iconSize $iconSize $iconPath --out (Join-Path $iconSetPath ('icon_' + $iconSize + 'x' + $iconSize + '.png')) | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Creating a macOS icon size failed.' }
    $retinaSize = $iconSize * 2
    & /usr/bin/sips -z $retinaSize $retinaSize $iconPath --out (Join-Path $iconSetPath ('icon_' + $iconSize + 'x' + $iconSize + '@2x.png')) | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Creating a macOS Retina icon size failed.' }
}
& /usr/bin/iconutil --convert icns --output (Join-Path $resourcesPath 'AppIcon.icns') $iconSetPath
if ($LASTEXITCODE -ne 0) { throw 'Creating AppIcon.icns failed.' }
& /bin/chmod 755 (Join-Path $macOsPath 'Eidolon')
if ($LASTEXITCODE -ne 0) { throw 'Setting the executable mode failed.' }

# There is no Developer ID certificate configured. Ad-hoc signing preserves the
# hardened-runtime JIT entitlement but does not provide Apple notarization.
$entitlementsPath = Join-Path $repositoryDirectory 'Eidolon.App/Assets/macOS/entitlements.plist'
& /usr/bin/codesign --force --sign - --options runtime --timestamp=none --entitlements $entitlementsPath (Join-Path $macOsPath 'Eidolon')
if ($LASTEXITCODE -ne 0) { throw 'Signing the application executable failed.' }
& /usr/bin/codesign --force --sign - --options runtime --timestamp=none --entitlements $entitlementsPath $bundlePath
if ($LASTEXITCODE -ne 0) { throw 'Signing the application bundle failed.' }

# Plain ZIP keeps the Unix file modes and includes only the bundle. Resource-fork
# __MACOSX entries and symlinks are deliberately outside the updater contract.
Push-Location -LiteralPath $stagingPath
try {
    & /usr/bin/zip -q -r -X $archivePath 'Eidolon.app'
    if ($LASTEXITCODE -ne 0) { throw 'Creating the application archive failed.' }
} finally {
    Pop-Location
}
Write-Output $archivePath
