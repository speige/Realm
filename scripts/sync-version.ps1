param(
    [string]$VersionNumber = ""
)

$ErrorActionPreference = "Stop"

$rootDir = Split-Path -Parent $PSScriptRoot
$versionJsonPath = Join-Path $rootDir "version.json"

if (-not (Test-Path $versionJsonPath)) {
    throw "version.json not found at $versionJsonPath"
}

$versionData = [System.IO.File]::ReadAllText($versionJsonPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json

if ($VersionNumber -and $VersionNumber.Trim() -ne "") {
    $rawInput = $VersionNumber.Trim()
    
    $cleanSemVer = $rawInput -replace '^v', ''
    $cleanSemVer = ($cleanSemVer -split '[-_]')[0]
    $semVerParts = @($cleanSemVer.Split('.'))
    while ($semVerParts.Count -lt 3) {
        $semVerParts += "0"
    }
    $extVersion = "$($semVerParts[0]).$($semVerParts[1]).$($semVerParts[2])"
    $fileVersion = "$extVersion.0"
    
    $productVersion = $rawInput -replace '^v', ''
    if ($rawInput -match '^v?\d+(?:\.\d+)*[-_](.+)$') {
        $prerelease = $Matches[1]
        $infoVersion = "$($extVersion)_$($prerelease)"
        $version = "$extVersion-$($prerelease.Replace('_', '-'))"
    } else {
        $infoVersion = $productVersion
        $version = $extVersion
    }
    
    $versionData.version = $version
    $versionData.fileVersion = $fileVersion
    $versionData.productVersion = $productVersion
    $versionData.informationalVersion = $infoVersion
    $versionData.extensionVersion = $extVersion
    
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    $updatedJsonText = $versionData | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText($versionJsonPath, $updatedJsonText, $utf8NoBom)
    Write-Host "Updated version.json with VersionNumber: $VersionNumber"
} else {
    $rawVersion = if ($versionData.version) { $versionData.version } else { "0.0.1" }
    $cleanSemVer = $rawVersion -replace '^v', ''
    $cleanSemVer = ($cleanSemVer -split '[-_]')[0]
    $semVerParts = @($cleanSemVer.Split('.'))
    while ($semVerParts.Count -lt 3) {
        $semVerParts += "0"
    }
    $extVersion = if ($versionData.extensionVersion) { $versionData.extensionVersion } else { "$($semVerParts[0]).$($semVerParts[1]).$($semVerParts[2])" }
    $fileVersion = if ($versionData.fileVersion) { $versionData.fileVersion } else { "$extVersion.0" }
    $productVersion = if ($versionData.productVersion) { $versionData.productVersion } else { $rawVersion }
    $infoVersion = if ($versionData.informationalVersion) { $versionData.informationalVersion } else { $productVersion.Replace('-', '_') }
    
    if ($rawVersion -match '^v?\d+(?:\.\d+)*[-_](.+)$') {
        $prerelease = $Matches[1]
        $version = "$extVersion-$($prerelease.Replace('_', '-'))"
    } else {
        $version = $extVersion
    }
}

Write-Host "Syncing version $version (Ext: $extVersion, File: $fileVersion, Product: $productVersion, Info: $infoVersion)..."

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

if ($env:GITHUB_ENV) {
    Add-Content -Path $env:GITHUB_ENV -Value "VERSION_NUMBER=$infoVersion"
    Add-Content -Path $env:GITHUB_ENV -Value "RAW_VERSION=$version"
}
if ($env:GITHUB_OUTPUT) {
    Add-Content -Path $env:GITHUB_OUTPUT -Value "version_number=$infoVersion"
    Add-Content -Path $env:GITHUB_OUTPUT -Value "raw_version=$version"
}

Write-Host "Version sync completed successfully."
