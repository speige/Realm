# install_vscode.ps1
# Script to install VS Code CLI alongside the game and set up the map editor extension.

$ErrorActionPreference = "Stop"

$rootDir = Get-Item $PSScriptRoot
$godotDir = Join-Path $rootDir.FullName "Realm.Godot"
$embedDir = Join-Path $godotDir "vscode_embedded"
$binDir = Join-Path $embedDir "bin"
$userDataDir = Join-Path $embedDir "user-data-dir"
$extsDir = Join-Path $userDataDir "extensions"
$extDest = Join-Path $extsDir "speige.realm-map-editor-1.0.0"
$zipPath = Join-Path $embedDir "vscode-cli.zip"

Write-Host "Creating directories..."
New-Item -ItemType Directory -Force -Path $binDir | Out-Null
New-Item -ItemType Directory -Force -Path $userDataDir | Out-Null
New-Item -ItemType Directory -Force -Path $extsDir | Out-Null
New-Item -ItemType Directory -Force -Path $extDest | Out-Null

$oldExtDest = Join-Path $extsDir "realm-map-editor"
if (Test-Path $oldExtDest) {
    Remove-Item -Path $oldExtDest -Recurse -Force
}

$cliPath = Join-Path $binDir "code.exe"
if (-not (Test-Path $cliPath)) {
    Write-Host "Downloading VS Code CLI..."
    $downloadUrl = "https://code.visualstudio.com/sha/download?build=stable&os=cli-win32-x64"
    Invoke-WebRequest -Uri $downloadUrl -OutFile $zipPath
    
    Write-Host "Extracting VS Code CLI..."
    Expand-Archive -Path $zipPath -DestinationPath $binDir -Force
    Remove-Item -Path $zipPath -Force
    Write-Host "VS Code CLI downloaded and extracted successfully."
} else {
    Write-Host "VS Code CLI already exists at $cliPath"
}

$editorDir = Join-Path $embedDir "editor"
if (-not (Test-Path (Join-Path $editorDir "code.exe"))) {
    Write-Host "Downloading VS Code Desktop..."
    $desktopZip = Join-Path $embedDir "vscode-desktop.zip"
    curl.exe -L "https://code.visualstudio.com/sha/download?build=stable&os=win32-x64-archive" -o $desktopZip
    
    Write-Host "Extracting VS Code Desktop..."
    New-Item -ItemType Directory -Force -Path $editorDir | Out-Null
    tar.exe -xf $desktopZip -C $editorDir
    Remove-Item -Path $desktopZip -Force
    Write-Host "VS Code Desktop downloaded and extracted successfully."
} else {
    Write-Host "VS Code Desktop already exists at $editorDir"
}

Write-Host "Registering editor path with VS Code CLI..."
& $cliPath version use stable --install-dir $editorDir

Write-Host "Building Map Editor extension..."
$extSrcDir = Join-Path $rootDir.FullName "Realm.MapEditorExtension"
Push-Location $extSrcDir
try {
    # Run npm install and compile using cmd to avoid execution policy restrictions on npm.ps1
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
Copy-Item -Path (Join-Path $extSrcDir "package.json") -Destination $extDest -Force
Copy-Item -Path (Join-Path $extSrcDir "map_schema.json") -Destination $extDest -Force
Copy-Item -Path (Join-Path $extSrcDir "dist") -Destination $extDest -Recurse -Force
Copy-Item -Path (Join-Path $extSrcDir "media") -Destination $extDest -Recurse -Force

Write-Host "Installing C# Dev Kit extension from Marketplace..."
& $cliPath --extensions-dir $extsDir ext install ms-dotnettools.csdevkit

Write-Host "Installing GLB Viewer extension from Marketplace..."
& $cliPath --extensions-dir $extsDir ext install OHZIInteractiveStudio.ohzi-vscode-glb-viewer

Write-Host "Installing Todo Tree extension from Marketplace..."
& $cliPath --extensions-dir $extsDir ext install Gruntfuggly.todo-tree

Write-Host "Installing Rainbow JSON extension from Marketplace..."
& $cliPath --extensions-dir $extsDir ext install mechatroner.rainbow-json

Write-Host "Installing NuGet Gallery extension from Marketplace..."
& $cliPath --extensions-dir $extsDir ext install patcx.vscode-nuget-gallery

Write-Host "Installing JSON Crack extension from Marketplace..."
& $cliPath --extensions-dir $extsDir ext install AykutSarac.jsoncrack-vscode

Write-Host "Installing EXR Preview extension from Marketplace..."
& $cliPath --extensions-dir $extsDir ext install mateh.exr-preview

Write-Host "VS Code Embedded and Extension setup completed successfully!"

