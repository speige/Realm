param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$godotDir = $PSScriptRoot
$appDataDir = Join-Path $env:APPDATA "Godot\app_userdata\Realm.Godot"
$embedDir = Join-Path $appDataDir "vscode"
$binDir = Join-Path $embedDir "bin"
$userDataDir = Join-Path $embedDir "user-data-dir"
$extsDir = Join-Path $userDataDir "extensions"
$editorDir = Join-Path $embedDir "editor"
$versionFile = Join-Path $embedDir "installed_vscode_version.json"
$completedMarkerPath = Join-Path $embedDir "install_completed.marker"

Write-Host "Target editor installation directory: $appDataDir"
New-Item -ItemType Directory -Force -Path $binDir | Out-Null
New-Item -ItemType Directory -Force -Path $userDataDir | Out-Null
New-Item -ItemType Directory -Force -Path $extsDir | Out-Null
New-Item -ItemType Directory -Force -Path $editorDir | Out-Null

$oldExtDest = Join-Path $extsDir "realm-map-editor"
if (Test-Path $oldExtDest) {
    Remove-Item -Path $oldExtDest -Recurse -Force
}

$wasiVersion = "30"
$wasiSdkBaseDir = Join-Path $appDataDir "wasi_sdk"
$wasiTargetDir = Join-Path $wasiSdkBaseDir "wasi-sdk-$wasiVersion"
$wasiClang = Join-Path $wasiTargetDir "bin\clang.exe"

$shouldInstallWasi = $Force -or (-not (Test-Path $wasiClang)) -or ((Get-Item $wasiClang).Length -eq 0)

if ($shouldInstallWasi) {
    Write-Host "Downloading WASI SDK $wasiVersion..."
    New-Item -ItemType Directory -Force -Path $wasiTargetDir | Out-Null
    $wasiTar = Join-Path $wasiSdkBaseDir "wasi-sdk-$wasiVersion.tar.gz"
    $wasiUrl = "https://github.com/WebAssembly/wasi-sdk/releases/download/wasi-sdk-$wasiVersion/wasi-sdk-$wasiVersion.0-x86_64-windows.tar.gz"
    curl.exe -L $wasiUrl -o $wasiTar

    Write-Host "Extracting WASI SDK $wasiVersion..."
    tar.exe -xf $wasiTar -C $wasiTargetDir --strip-components=1
    if (Test-Path $wasiTar) {
        Remove-Item -Path $wasiTar -Force
    }
    Write-Host "WASI SDK $wasiVersion installed to $wasiTargetDir successfully."
} else {
    Write-Host "WASI SDK verified at $wasiTargetDir"
}

$cliPath = Join-Path $binDir "code.exe"
$editorExe = Join-Path $editorDir "code.exe"

$criticalFileMissing = (-not (Test-Path $cliPath)) -or ((Get-Item $cliPath).Length -eq 0) -or (-not (Test-Path $editorExe)) -or ((Get-Item $editorExe).Length -eq 0)

$remoteStableName = ""
$remoteStableSha = ""
$remoteCliUrl = ""
$remoteDesktopUrl = ""
$shaQueryFailed = $false

Write-Host "Checking for VS Code stable releases..."
try {
    $shaResponse = Invoke-RestMethod -Uri "https://code.visualstudio.com/sha" -TimeoutSec 5 -ErrorAction Stop
    $desktopProduct = $shaResponse.products | Where-Object { $_.build -eq 'stable' -and $_.platform.os -eq 'win32-x64-archive' } | Select-Object -First 1
    $cliProduct = $shaResponse.products | Where-Object { $_.build -eq 'stable' -and $_.platform.os -eq 'cli-win32-x64' } | Select-Object -First 1

    if ($desktopProduct) {
        $remoteStableName = $desktopProduct.name
        $remoteStableSha = $desktopProduct.version
        $remoteDesktopUrl = $desktopProduct.url
    }
    if ($cliProduct) {
        $remoteCliUrl = $cliProduct.url
    }
} catch {
    Write-Host "Failed to check https://code.visualstudio.com/sha ($($_.Exception.Message))."
    $shaQueryFailed = $true
}

$shouldInstallVSCode = $false

if ($Force) {
    Write-Host "Force re-install requested. Re-installing VS Code..."
    $shouldInstallVSCode = $true
} elseif ($criticalFileMissing) {
    Write-Host "Auto-repair detected: Critical VS Code files missing or corrupt. Installing VS Code..."
    $shouldInstallVSCode = $true
} elseif ($shaQueryFailed) {
    Write-Host "Skipping auto-detection because /sha check was unreachable and critical files exist."
    $shouldInstallVSCode = $false
} else {
    $installedVersionData = $null
    if (Test-Path $versionFile) {
        try {
            $installedVersionData = Get-Content $versionFile -Raw | ConvertFrom-Json
        } catch {}
    }

    if ($installedVersionData -and $installedVersionData.name -and $installedVersionData.version) {
        $installedName = $installedVersionData.name
        $installedSha = $installedVersionData.version

        try {
            $vRemote = [version]$remoteStableName
            $vInstalled = [version]$installedName
            if ($vRemote -gt $vInstalled -or ($vRemote -eq $vInstalled -and $remoteStableSha -ne $installedSha)) {
                Write-Host "Newer VS Code stable version detected (installed: $installedName, remote: $remoteStableName). Updating..."
                $shouldInstallVSCode = $true
            } else {
                Write-Host "VS Code ($installedName) is up to date."
            }
        } catch {
            if ($remoteStableSha -ne $installedSha) {
                Write-Host "VS Code version mismatch. Updating..."
                $shouldInstallVSCode = $true
            } else {
                Write-Host "VS Code is up to date."
            }
        }
    } else {
        $exeVersion = (Get-Item $editorExe).VersionInfo.ProductVersion
        if ($exeVersion) {
            try {
                $vRemote = [version]$remoteStableName
                $vInstalled = [version]$exeVersion
                if ($vRemote -gt $vInstalled) {
                    Write-Host "Newer VS Code stable version detected ($vRemote > $vInstalled). Updating..."
                    $shouldInstallVSCode = $true
                } else {
                    Write-Host "VS Code ($exeVersion) is up to date."
                    $meta = @{
                        name = $remoteStableName
                        version = $remoteStableSha
                        installed_utc = (Get-Date).ToUniversalTime().ToString("o")
                    }
                    $metaText = $meta | ConvertTo-Json
                    [System.IO.File]::WriteAllText($versionFile, $metaText, [System.Text.Encoding]::UTF8)
                }
            } catch {
                Write-Host "VS Code verified at $editorDir"
            }
        }
    }
}

if ($shouldInstallVSCode) {
    Get-Process | Where-Object { 
        try { $_.Path -and $_.Path.StartsWith($embedDir) } catch { $false } 
    } | Stop-Process -Force -ErrorAction SilentlyContinue

    Write-Host "Downloading VS Code CLI..."
    $cliZip = Join-Path $embedDir "vscode-cli.zip"
    $cliDownloadUrl = if ($remoteCliUrl) { $remoteCliUrl } else { "https://code.visualstudio.com/sha/download?build=stable&os=cli-win32-x64" }
    curl.exe -L $cliDownloadUrl -o $cliZip

    Write-Host "Extracting VS Code CLI..."
    Expand-Archive -Path $cliZip -DestinationPath $binDir -Force
    if (Test-Path $cliZip) {
        Remove-Item -Path $cliZip -Force
    }
    Write-Host "VS Code CLI installed successfully."

    Write-Host "Downloading VS Code Desktop..."
    $desktopZip = Join-Path $embedDir "vscode-desktop.zip"
    $desktopDownloadUrl = if ($remoteDesktopUrl) { $remoteDesktopUrl } else { "https://code.visualstudio.com/sha/download?build=stable&os=win32-x64-archive" }
    curl.exe -L $desktopDownloadUrl -o $desktopZip

    Write-Host "Extracting VS Code Desktop..."
    tar.exe -xf $desktopZip -C $editorDir
    if (Test-Path $desktopZip) {
        Remove-Item -Path $desktopZip -Force
    }
    Write-Host "VS Code Desktop installed successfully."

    $productJsonFiles = Get-ChildItem -Recurse -Filter "product.json" $editorDir
    foreach ($pj in $productJsonFiles) {
        $content = Get-Content $pj.FullName -Raw
        if ($content -match 'vscode-cdn\.net') {
            Write-Host "Patching webview CDN endpoint in $($pj.FullName)..."
            $content = $content -replace '"webviewContentExternalBaseUrlTemplate":\s*"https://\{\{uuid\}\}\.vscode-cdn\.net/\{\{quality\}\}/\{\{commit\}\}/out/vs/workbench/contrib/webview/browser/pre/"', '"webviewContentExternalBaseUrlTemplate": "{{commit}}/out/vs/workbench/contrib/webview/browser/pre/"'
            Set-Content -Path $pj.FullName -Value $content -NoNewline
        }
    }

    if ($remoteStableName -and $remoteStableSha) {
        $meta = @{
            name = $remoteStableName
            version = $remoteStableSha
            installed_utc = (Get-Date).ToUniversalTime().ToString("o")
        }
        $metaText = $meta | ConvertTo-Json
        [System.IO.File]::WriteAllText($versionFile, $metaText, [System.Text.Encoding]::UTF8)
    } else {
        $prodVer = (Get-Item $editorExe).VersionInfo.ProductVersion
        $meta = @{
            name = if ($prodVer) { $prodVer } else { "stable" }
            version = "unknown"
            installed_utc = (Get-Date).ToUniversalTime().ToString("o")
        }
        $metaText = $meta | ConvertTo-Json
        [System.IO.File]::WriteAllText($versionFile, $metaText, [System.Text.Encoding]::UTF8)
    }
}

Write-Host "Registering editor path with VS Code CLI..."
& $cliPath version use stable --install-dir $editorDir

$extSrc = Join-Path $godotDir "vscode_extensions_dist\speige.realm-map-editor"
if (-not (Test-Path $extSrc)) {
    $altSrc = Join-Path $godotDir "..\Realm.MapEditorExtension"
    if (Test-Path $altSrc) {
        $extSrc = $altSrc
    }
}

$extVersion = "0.0.1"
$extPkgJson = Join-Path $extSrc "package.json"
if (Test-Path $extPkgJson) {
    try {
        $pkgObj = Get-Content $extPkgJson -Raw | ConvertFrom-Json
        if ($pkgObj.version) {
            $extVersion = $pkgObj.version
        }
    } catch {}
}

$extDest = Join-Path $extsDir "speige.realm-map-editor-$extVersion"
New-Item -ItemType Directory -Force -Path $extDest | Out-Null

$shouldInstallExt = $Force -or (-not (Test-Path (Join-Path $extDest "package.json")))
if ($shouldInstallExt -and $extSrc -and (Test-Path $extSrc)) {
    Write-Host "Installing Realm Map Editor extension version $extVersion to $extDest..."
    Copy-Item -Path (Join-Path $extSrc "*") -Destination $extDest -Recurse -Force
} else {
    Write-Host "Realm Map Editor extension ($extVersion) verified at $extDest"
}

$requiredExtensions = @(
    "ms-dotnettools.csdevkit",
    "OHZIInteractiveStudio.ohzi-vscode-glb-viewer",
    "Gruntfuggly.todo-tree",
    "mechatroner.rainbow-json",
    "patcx.vscode-nuget-gallery",
    "AykutSarac.jsoncrack-vscode",
    "akondratiuk1-dev.texture-viewer"
)

foreach ($extId in $requiredExtensions) {
    $extMatch = Get-ChildItem -Path $extsDir -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "$extId*" -or $_.Name -like "*$extId*" }
    if ((-not $extMatch) -or $Force) {
        Write-Host "Installing extension $extId from Marketplace..."
        & $cliPath --extensions-dir $extsDir ext install $extId
    } else {
        Write-Host "Extension $extId already installed."
    }
}

Get-Date -Format "o" | Out-File -FilePath $completedMarkerPath -Encoding utf8
Write-Host "VS Code Embedded and Extension setup completed successfully!"
