$ErrorActionPreference = "Stop"

$godotDir = $PSScriptRoot
$embedDir = Join-Path $godotDir "vscode_embedded"
$binDir = Join-Path $embedDir "bin"
$userDataDir = Join-Path $embedDir "user-data-dir"
$extsDir = Join-Path $userDataDir "extensions"
$extDest = Join-Path $extsDir "speige.realm-map-editor-1.0.0"
$completedMarkerPath = Join-Path $embedDir "install_completed.marker"

Write-Host "Ensuring required directories exist..."
New-Item -ItemType Directory -Force -Path $binDir | Out-Null
New-Item -ItemType Directory -Force -Path $userDataDir | Out-Null
New-Item -ItemType Directory -Force -Path $extsDir | Out-Null
New-Item -ItemType Directory -Force -Path $extDest | Out-Null

$oldExtDest = Join-Path $extsDir "realm-map-editor"
if (Test-Path $oldExtDest) {
    Remove-Item -Path $oldExtDest -Recurse -Force
}

$cliPath = Join-Path $binDir "code.exe"
if ((-not (Test-Path $cliPath)) -or ((Get-Item $cliPath).Length -eq 0)) {
    Write-Host "Downloading VS Code CLI..."
    $zipPath = Join-Path $embedDir "vscode-cli.zip"
    $downloadUrl = "https://code.visualstudio.com/sha/download?build=stable&os=cli-win32-x64"
    Invoke-WebRequest -Uri $downloadUrl -OutFile $zipPath
    
    Write-Host "Extracting VS Code CLI..."
    Expand-Archive -Path $zipPath -DestinationPath $binDir -Force
    if (Test-Path $zipPath) {
        Remove-Item -Path $zipPath -Force
    }
    Write-Host "VS Code CLI downloaded and extracted successfully."
} else {
    Write-Host "VS Code CLI verified at $cliPath"
}

$editorDir = Join-Path $embedDir "editor"
$editorExe = Join-Path $editorDir "code.exe"
if ((-not (Test-Path $editorExe)) -or ((Get-Item $editorExe).Length -eq 0)) {
    Write-Host "Downloading VS Code Desktop..."
    $desktopZip = Join-Path $embedDir "vscode-desktop.zip"
    curl.exe -L "https://code.visualstudio.com/sha/download?build=stable&os=win32-x64-archive" -o $desktopZip
    
    Write-Host "Extracting VS Code Desktop..."
    New-Item -ItemType Directory -Force -Path $editorDir | Out-Null
    tar.exe -xf $desktopZip -C $editorDir
    if (Test-Path $desktopZip) {
        Remove-Item -Path $desktopZip -Force
    }
    Write-Host "VS Code Desktop downloaded and extracted successfully."
} else {
    Write-Host "VS Code Desktop verified at $editorDir"
}

$productJsonFiles = Get-ChildItem -Recurse -Filter "product.json" $editorDir
foreach ($pj in $productJsonFiles) {
    $content = Get-Content $pj.FullName -Raw
    if ($content -match 'vscode-cdn\.net') {
        Write-Host "Patching webview CDN endpoint in $($pj.FullName)..."
        $content = $content -replace '"webviewContentExternalBaseUrlTemplate":\s*"https://\{\{uuid\}\}\.vscode-cdn\.net/\{\{quality\}\}/\{\{commit\}\}/out/vs/workbench/contrib/webview/browser/pre/"', '"webviewContentExternalBaseUrlTemplate": "{{commit}}/out/vs/workbench/contrib/webview/browser/pre/"'
        Set-Content -Path $pj.FullName -Value $content -NoNewline
    }
}

Write-Host "Registering editor path with VS Code CLI..."
& $cliPath version use stable --install-dir $editorDir

$parentDir = (Get-Item $PSScriptRoot).Parent.FullName
$extSrcDir = Join-Path $parentDir "Realm.MapEditorExtension"
if (-not (Test-Path $extSrcDir)) {
    $extSrcDir = Join-Path $PSScriptRoot "Realm.MapEditorExtension"
}
if (Test-Path $extSrcDir) {
    Push-Location $extSrcDir
    try {
        if (Get-Command npm -ErrorAction SilentlyContinue) {
            Write-Host "Running npm install..."
            cmd.exe /c "npm install"
            Write-Host "Running npm run compile..."
            cmd.exe /c "npm run compile"
        } else {
            Write-Host "npm not found. Skipping build step and using pre-compiled extension files."
        }
    } catch {
        Write-Warning "Failed to build extension: $_. Using pre-compiled extension files instead."
    } finally {
        Pop-Location
    }

    Write-Host "Copying extension files..."
    if (Test-Path (Join-Path $extSrcDir "package.json")) { Copy-Item -Path (Join-Path $extSrcDir "package.json") -Destination $extDest -Force }
    if (Test-Path (Join-Path $extSrcDir "map_schema.json")) { Copy-Item -Path (Join-Path $extSrcDir "map_schema.json") -Destination $extDest -Force }
    if (Test-Path (Join-Path $extSrcDir "dist")) { Copy-Item -Path (Join-Path $extSrcDir "dist") -Destination $extDest -Recurse -Force }
    if (Test-Path (Join-Path $extSrcDir "media")) { Copy-Item -Path (Join-Path $extSrcDir "media") -Destination $extDest -Recurse -Force }
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
    $extMatch = Get-ChildItem -Path $extsDir -Directory | Where-Object { $_.Name -like "$extId*" -or $_.Name -like "*$extId*" }
    if (-not $extMatch) {
        Write-Host "Installing extension $extId from Marketplace..."
        & $cliPath --extensions-dir $extsDir ext install $extId
    } else {
        Write-Host "Extension $extId already installed."
    }
}

Get-Date -Format "o" | Out-File -FilePath $completedMarkerPath -Encoding utf8
Write-Host "VS Code Embedded and Extension setup completed successfully!"
