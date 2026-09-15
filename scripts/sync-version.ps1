$ErrorActionPreference = "Stop"

$rootDir = Split-Path -Parent $PSScriptRoot
$versionJsonPath = Join-Path $rootDir "version.json"

if (-not (Test-Path $versionJsonPath)) {
    throw "version.json not found at $versionJsonPath"
}

$versionData = [System.IO.File]::ReadAllText($versionJsonPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
$version = $versionData.version
$fileVersion = if ($versionData.fileVersion) { $versionData.fileVersion } else { "0.0.1.0" }
$productVersion = if ($versionData.productVersion) { $versionData.productVersion } else { $version }
$extVersion = if ($versionData.extensionVersion) { $versionData.extensionVersion } else { $version.Split('-')[0] }
$infoVersion = if ($versionData.informationalVersion) { $versionData.informationalVersion } else { $version.Replace('-', '_') }

Write-Host "Syncing version $version (Ext: $extVersion, File: $fileVersion, Product: $productVersion)..."

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

$propsPath = Join-Path $rootDir "Directory.Build.props"
$propsContent = @"
<Project>
  <PropertyGroup>
    <Version>$version</Version>
    <FileVersion>$fileVersion</FileVersion>
    <InformationalVersion>$infoVersion</InformationalVersion>
  </PropertyGroup>
</Project>
"@
[System.IO.File]::WriteAllText($propsPath, $propsContent, $utf8NoBom)
Write-Host "Updated Directory.Build.props"

$extPkgPath = Join-Path $rootDir "Realm.MapEditorExtension\package.json"
if (Test-Path $extPkgPath) {
    $pkg = [System.IO.File]::ReadAllText($extPkgPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    $pkg.version = $extVersion
    $pkgText = $pkg | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText($extPkgPath, $pkgText, $utf8NoBom)
    Write-Host "Updated $extPkgPath"
}

$distPkgPath = Join-Path $rootDir "Realm.Godot\vscode_extensions_dist\speige.realm-map-editor\package.json"
if (Test-Path $distPkgPath) {
    $distPkg = [System.IO.File]::ReadAllText($distPkgPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    $distPkg.version = $extVersion
    $distPkgText = $distPkg | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText($distPkgPath, $distPkgText, $utf8NoBom)
    Write-Host "Updated $distPkgPath"
}

$exportCfgPath = Join-Path $rootDir "Realm.Godot\export_presets.cfg"
if (Test-Path $exportCfgPath) {
    $cfgContent = [System.IO.File]::ReadAllText($exportCfgPath, [System.Text.Encoding]::UTF8)
    $cfgContent = $cfgContent -replace 'application/file_version=".*?"', "application/file_version=`"$fileVersion`""
    $cfgContent = $cfgContent -replace 'application/product_version=".*?"', "application/product_version=`"$productVersion`""
    [System.IO.File]::WriteAllText($exportCfgPath, $cfgContent, $utf8NoBom)
    Write-Host "Updated $exportCfgPath"
}

Write-Host "Version sync completed successfully."
