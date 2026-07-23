import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';

export class RealmMapEditorProvider implements vscode.CustomTextEditorProvider {
    public static register(context: vscode.ExtensionContext): vscode.Disposable {
        const provider = new RealmMapEditorProvider(context);
        return vscode.window.registerCustomEditorProvider(RealmMapEditorProvider.viewType, provider);
    }

    private static readonly viewType = 'realm.mapEditor';

    constructor(
        private readonly context: vscode.ExtensionContext
    ) {}

    public async resolveCustomTextEditor(
        document: vscode.TextDocument,
        webviewPanel: vscode.WebviewPanel,
        _token: vscode.CancellationToken
    ): Promise<void> {
        webviewPanel.webview.options = {
            enableScripts: true
        };
        webviewPanel.webview.html = this.getHtmlForWebview(webviewPanel.webview);

        const changeDocumentSubscription = vscode.workspace.onDidChangeTextDocument(e => {
            if (e.document.uri.toString() === document.uri.toString()) {
                webviewPanel.webview.postMessage({
                    type: 'update',
                    text: e.document.getText()
                });
            }
        });

        webviewPanel.onDidDispose(() => {
            changeDocumentSubscription.dispose();
        });

        webviewPanel.webview.onDidReceiveMessage(async e => {
            switch (e.type) {
                case 'ready':
                    webviewPanel.webview.postMessage({
                        type: 'update',
                        text: document.getText()
                    });
                    break;
                case 'change':
                    const applied = await this.updateTextDocument(document, e.text);
                    if (!applied) {
                        console.warn('WorkspaceEdit was not applied, resyncing webview');
                        webviewPanel.webview.postMessage({
                            type: 'update',
                            text: document.getText()
                        });
                    }
                    break;
                case 'browseFile':
                    await this.handleBrowseFile(webviewPanel.webview, e.fieldId, e.fieldClass, e.fieldIndex, e.fileTypes, document.uri);
                    break;
                case 'openFile':
                    const absFile = this.resolveGodotPath(e.path, document.uri);
                    if (absFile && fs.existsSync(absFile)) {
                        vscode.commands.executeCommand('vscode.open', vscode.Uri.file(absFile));
                    }
                    break;
                case 'resolvePath':
                    const absPath = this.resolveGodotPath(e.path, document.uri);
                    const webviewUri = absPath ? webviewPanel.webview.asWebviewUri(vscode.Uri.file(absPath)).toString() : '';
                    webviewPanel.webview.postMessage({
                        type: 'resolvePathResult',
                        requestId: e.requestId,
                        uri: webviewUri
                    });
                    break;
                case 'importAsset':
                    await this.handleImportAsset(webviewPanel.webview, e.assetType, e.options, document);
                    break;
                case 'processImportedAsset':
                    if (e.fileName || e.filePath) {
                        await this.processImportedAssetFile(e.fileName || e.filePath, e.fileDataBase64, e.assetType, e.options, document);
                    }
                    break;
            }
        });
    }

    private get lastOpenedDirectory(): string | undefined {
        return this.context.globalState.get<string>('lastOpenedDirectory');
    }

    private set lastOpenedDirectory(dir: string | undefined) {
        this.context.globalState.update('lastOpenedDirectory', dir);
    }

    private async handleBrowseFile(
        webview: vscode.Webview,
        fieldId: string | null,
        fieldClass: string | null,
        fieldIndex: number | null,
        fileTypes?: string[],
        documentUri?: vscode.Uri
    ) {
        // Direct HTML file dialog trigger (avoids vscode.window.showOpenDialog text prompt in web mode)
        webview.postMessage({
            type: 'browseFileFallback',
            fieldId,
            fieldClass,
            fieldIndex,
            accept: fileTypes ? fileTypes.map(ext => '.' + ext.replace(/^\./, '')).join(',') : '*'
        });
    }

    private computeHashHex(buffer: Buffer): string {
        const crypto = require('crypto');
        return crypto.createHash('sha256').update(buffer).digest('hex');
    }

    private async handleImportAsset(
        webview: vscode.Webview,
        assetType: string,
        extraOptions: any,
        document: vscode.TextDocument
    ) {
        let accept = '*';
        if (assetType === 'texture') {
            accept = '.png,.jpg,.jpeg,.bmp,.tga,.webp';
        } else if (assetType === 'glb') {
            accept = '.glb,.gltf';
        } else if (assetType === 'decal' || assetType === 'vfx' || assetType === 'icon') {
            accept = '.png,.jpg,.jpeg,.bmp,.tga,.webp,.svg';
        } else if (assetType === 'audio') {
            accept = '.ogg,.wav,.mp3,.flac,.aac,.m4a';
        }

        webview.postMessage({
            type: 'importAssetFallback',
            assetType,
            extraOptions,
            accept
        });
    }

    private async processImportedAssetFile(
        sourceFileOrName: string,
        fileDataBase64: string | undefined,
        assetType: string,
        extraOptions: any,
        document: vscode.TextDocument
    ) {
        try {
            const documentDir = path.dirname(document.uri.fsPath);
            let targetDir = documentDir;

            let fileBytes: Buffer;
            let fileName = path.basename(sourceFileOrName);
            if (fileDataBase64) {
                fileBytes = Buffer.from(fileDataBase64, 'base64');
            } else {
                fileBytes = fs.readFileSync(sourceFileOrName);
            }

            const metadataText = document.getText();
            let metadata: any = {};
            if (metadataText.trim()) {
                try {
                    metadata = JSON.parse(metadataText);
                } catch {
                    metadata = {};
                }
            }

            if (!metadata.Assets) {
                metadata.Assets = {};
            }

            if (assetType === 'glb') {
                const subCategory = (extraOptions && extraOptions.category) ? extraOptions.category.toLowerCase() : 'props';
                const subDir = path.join(targetDir, 'Assets', 'models', subCategory);
                if (!fs.existsSync(subDir)) fs.mkdirSync(subDir, { recursive: true });
                const baseName = path.basename(fileName, path.extname(fileName)) + '.glb';
                const targetPath = path.join(subDir, baseName);
                fs.writeFileSync(targetPath, fileBytes);
                const blake3 = this.computeHashHex(fileBytes);
                if (!metadata.Assets.glb) metadata.Assets.glb = {};
                if (!metadata.Assets.glb[subCategory]) metadata.Assets.glb[subCategory] = {};
                metadata.Assets.glb[subCategory][baseName] = blake3;
                vscode.window.showInformationMessage(`Imported GLB Model (${subCategory}): ${baseName}`);
            } else if (assetType === 'decal') {
                const subDir = path.join(targetDir, 'Assets', 'decals');
                if (!fs.existsSync(subDir)) fs.mkdirSync(subDir, { recursive: true });
                const baseName = path.basename(fileName, path.extname(fileName)) + '.png';
                const targetPath = path.join(subDir, baseName);
                fs.writeFileSync(targetPath, fileBytes);
                const blake3 = this.computeHashHex(fileBytes);
                if (!metadata.Assets.decals) metadata.Assets.decals = {};
                metadata.Assets.decals[baseName] = blake3;
                vscode.window.showInformationMessage(`Imported Decal: ${baseName}`);
            } else if (assetType === 'icon') {
                const subDir = path.join(targetDir, 'Assets', 'icons');
                if (!fs.existsSync(subDir)) fs.mkdirSync(subDir, { recursive: true });
                const baseName = path.basename(fileName, path.extname(fileName)) + '.png';
                const targetPath = path.join(subDir, baseName);
                fs.writeFileSync(targetPath, fileBytes);
                const blake3 = this.computeHashHex(fileBytes);
                if (!metadata.Assets.icons) metadata.Assets.icons = {};
                metadata.Assets.icons[baseName] = blake3;
                vscode.window.showInformationMessage(`Imported 2D Icon: ${baseName}`);
            } else if (assetType === 'vfx') {
                const subDir = path.join(targetDir, 'Assets', 'vfx');
                if (!fs.existsSync(subDir)) fs.mkdirSync(subDir, { recursive: true });
                const baseName = path.basename(fileName, path.extname(fileName)) + '.png';
                const targetPath = path.join(subDir, baseName);
                fs.writeFileSync(targetPath, fileBytes);
                const blake3 = this.computeHashHex(fileBytes);
                const cols = (extraOptions && extraOptions.columns) ? parseInt(extraOptions.columns, 10) : 4;
                const rows = (extraOptions && extraOptions.rows) ? parseInt(extraOptions.rows, 10) : 4;
                if (!metadata.Assets.vfx_spritesheets) metadata.Assets.vfx_spritesheets = {};
                metadata.Assets.vfx_spritesheets[baseName] = {
                    hash: blake3,
                    columns: cols,
                    rows: rows
                };
                vscode.window.showInformationMessage(`Imported VFX Spritesheet: ${baseName} (${cols}x${rows})`);
            } else if (assetType === 'audio') {
                const audioType = (extraOptions && extraOptions.audioType) ? extraOptions.audioType.toLowerCase() : 'sfx';
                const catKey = audioType === 'music' ? 'music' : 'sfx';
                const subDir = path.join(targetDir, 'Assets', 'audio', catKey);
                if (!fs.existsSync(subDir)) fs.mkdirSync(subDir, { recursive: true });
                const baseName = path.basename(fileName, path.extname(fileName)) + '.ogg';
                const targetPath = path.join(subDir, baseName);
                fs.writeFileSync(targetPath, fileBytes);
                const blake3 = this.computeHashHex(fileBytes);
                if (!metadata.Assets[catKey]) metadata.Assets[catKey] = {};
                metadata.Assets[catKey][baseName] = blake3;
                vscode.window.showInformationMessage(`Imported Audio (${audioType}): ${baseName}`);
            } else if (assetType === 'skybox') {
                const subDir = path.join(targetDir, 'Assets', 'skyboxes');
                if (!fs.existsSync(subDir)) fs.mkdirSync(subDir, { recursive: true });
                const baseName = path.basename(fileName, path.extname(fileName)) + '.png';
                const targetPath = path.join(subDir, baseName);
                fs.writeFileSync(targetPath, fileBytes);
                const blake3 = this.computeHashHex(fileBytes);
                if (!metadata.Assets.skyboxes) metadata.Assets.skyboxes = {};
                metadata.Assets.skyboxes[baseName] = blake3;
                vscode.window.showInformationMessage(`Imported Skybox: ${baseName}`);
            } else if (assetType === 'texture') {
                const cleanBase = path.basename(fileName, path.extname(fileName)).toLowerCase().replace(/[^a-z0-9_]/g, '_');
                let swatchName = cleanBase || 'custom_texture';
                
                if (!metadata.Assets) metadata.Assets = {};
                if (!metadata.Assets.textures) metadata.Assets.textures = {};

                // Ensure unique name if swatchName already exists
                let finalSwatchName = swatchName;
                let counter = 1;
                while (metadata.Assets.textures[finalSwatchName + '.ktx2']) {
                    finalSwatchName = `${swatchName}_${counter}`;
                    counter++;
                }

                const subDir = path.join(targetDir, 'Assets', 'textures');
                if (!fs.existsSync(subDir)) fs.mkdirSync(subDir, { recursive: true });
                const targetRawPng = path.join(subDir, `_temp_import_${finalSwatchName}.png`);
                fs.writeFileSync(targetRawPng, fileBytes);

                const blake3 = this.computeHashHex(fileBytes);
                metadata.Assets.textures[finalSwatchName + '.ktx2'] = blake3;
                vscode.window.showInformationMessage(`Imported Texture (${finalSwatchName}). Godot converts raw texture to PBR KTX2.`);
            }

            // Fix race condition: re-read document text AFTER slow file I/O operations
            // so we don't overwrite user edits made in the UI while the file was copying.
            const freshMetadataText = document.getText();
            let freshMetadata: any = {};
            if (freshMetadataText.trim()) {
                try {
                    freshMetadata = JSON.parse(freshMetadataText);
                } catch {
                    freshMetadata = {};
                }
            }
            if (!freshMetadata.Assets) {
                freshMetadata.Assets = {};
            }

            // Auto-migrate legacy root "textures" if they exist
            if (freshMetadata.textures) {
                if (!freshMetadata.Assets.textures) {
                    freshMetadata.Assets.textures = freshMetadata.textures;
                }
                delete freshMetadata.textures;
            }

            // Merge our newly imported asset into the fresh state
            if (metadata.Assets) {
                for (const cat of Object.keys(metadata.Assets)) {
                    if (!freshMetadata.Assets[cat]) freshMetadata.Assets[cat] = {};
                    for (const item of Object.keys(metadata.Assets[cat])) {
                        if (typeof metadata.Assets[cat][item] === 'object' && metadata.Assets[cat][item] !== null && !Array.isArray(metadata.Assets[cat][item])) {
                            if (!freshMetadata.Assets[cat][item]) freshMetadata.Assets[cat][item] = {};
                            Object.assign(freshMetadata.Assets[cat][item], metadata.Assets[cat][item]);
                        } else {
                            freshMetadata.Assets[cat][item] = metadata.Assets[cat][item];
                        }
                    }
                }
            }

            await this.updateTextDocument(document, JSON.stringify(freshMetadata, null, 2));
        } catch (err: any) {
            vscode.window.showErrorMessage(`Failed to import asset: ${err.message}`);
        }
    }

    private getGodotRelativePath(absolutePath: string): string {
        let currentDir = path.dirname(absolutePath);
        while (true) {
            const projectFile = path.join(currentDir, 'project.godot');
            if (fs.existsSync(projectFile)) {
                let rel = path.relative(currentDir, absolutePath);
                return 'res://' + rel.replace(/\\/g, '/');
            }
            const parent = path.dirname(currentDir);
            if (parent === currentDir) {
                break;
            }
            currentDir = parent;
        }

        const godotFolderName = 'Realm.Godot';
        const parts = absolutePath.split(path.sep);
        const idx = parts.findIndex(p => p.toLowerCase() === godotFolderName.toLowerCase());
        if (idx !== -1) {
            const relativeParts = parts.slice(idx + 1);
            return 'res://' + relativeParts.join('/');
        }

        const workspaceFolders = vscode.workspace.workspaceFolders;
        if (workspaceFolders) {
            for (const folder of workspaceFolders) {
                if (absolutePath.startsWith(folder.uri.fsPath)) {
                    let rel = path.relative(folder.uri.fsPath, absolutePath);
                    return rel.replace(/\\/g, '/');
                }
            }
        }
        return absolutePath.replace(/\\/g, '/');
    }

    private resolveGodotPath(godotPath: string, documentUri: vscode.Uri): string | null {
        if (!godotPath) {
            return null;
        }
        
        let cleanPath = godotPath;
        if (godotPath.startsWith('res://')) {
            cleanPath = godotPath.substring(6);
        }
        
        const docDir = path.dirname(documentUri.fsPath);
        let currentDir = docDir;
        while (true) {
            const projectFile = path.join(currentDir, 'project.godot');
            if (fs.existsSync(projectFile)) {
                const abs = path.join(currentDir, cleanPath);
                if (fs.existsSync(abs)) {
                    return abs;
                }
            }
            const parent = path.dirname(currentDir);
            if (parent === currentDir) {
                break;
            }
            currentDir = parent;
        }
        
        const workspaceFolders = vscode.workspace.workspaceFolders;
        if (workspaceFolders) {
            for (const folder of workspaceFolders) {
                const godotPathOption1 = path.join(folder.uri.fsPath, cleanPath);
                if (fs.existsSync(godotPathOption1)) {
                    return godotPathOption1;
                }
                const godotPathOption2 = path.join(folder.uri.fsPath, 'Realm.Godot', cleanPath);
                if (fs.existsSync(godotPathOption2)) {
                    return godotPathOption2;
                }
            }
        }
        
        const relPath = path.join(docDir, cleanPath);
        if (fs.existsSync(relPath)) {
            return relPath;
        }
        
        return null;
    }

    private async updateTextDocument(document: vscode.TextDocument, text: string): Promise<boolean> {
        const edit = new vscode.WorkspaceEdit();
        const fullRange = new vscode.Range(
            document.positionAt(0),
            document.positionAt(document.getText().length)
        );
        edit.replace(
            document.uri,
            fullRange,
            text
        );
        try {
            const applied = await vscode.workspace.applyEdit(edit);
            if (!applied) {
                console.error('updateTextDocument: applyEdit returned false');
            }
            return applied;
        } catch (err) {
            console.error('updateTextDocument error:', err);
            return false;
        }
    }

    private getHtmlForWebview(webview: vscode.Webview): string {
        const scriptUri = webview.asWebviewUri(vscode.Uri.file(
            path.join(this.context.extensionPath, 'media', 'editor.js')
        ));
        const styleUri = webview.asWebviewUri(vscode.Uri.file(
            path.join(this.context.extensionPath, 'media', 'editor.css')
        ));
        const nonce = this.getNonce();

        return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src ${webview.cspSource} https:; style-src ${webview.cspSource} 'unsafe-inline'; script-src 'nonce-${nonce}';">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <link href="${styleUri}" rel="stylesheet" />
    <title>Realm Map Editor</title>
</head>
<body>
    <div class="app-container">
        <div class="global-header">
            <div class="app-title-group">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="color: var(--accent);"><path d="M12 2L2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5"/></svg>
                <h1>Realm Map Editor</h1>
            </div>
            <div class="global-tabs">
                <button type="button" class="tab-btn active" data-domain="units">👥 Units</button>
                <button type="button" class="tab-btn" data-domain="weapons">⚔️ Weapons</button>
                <button type="button" class="tab-btn" data-domain="abilities">🪄 Abilities</button>
                <button type="button" class="tab-btn" data-domain="upgrades">🛡️ Upgrades</button>
                <button type="button" class="tab-btn" data-domain="items">📦 Items</button>
                <button type="button" class="tab-btn" data-domain="assets">🎨 Assets</button>
                <button type="button" class="tab-btn" data-domain="properties">⚙️ Map Props</button>
            </div>
            <div class="header-right-actions">
                <div id="save-status" class="save-status saved" title="Auto-saved to file">● Saved</div>
                <button type="button" id="toggle-lock-btn" class="btn secondary-btn small-btn" title="Lock Editor (Read-Only Mode)">🔓 Lock</button>
                <button type="button" id="toggle-buttons-btn" class="btn secondary-btn small-btn" title="Toggle Add/Delete Controls">➕ Edit Ops</button>
                <button type="button" id="toggle-debug-btn" class="btn secondary-btn small-btn" title="Toggle Debug JSON View">🐞 Debug</button>
            </div>
        </div>
        <div class="editor-body">
            <div class="sidebar">
                <div class="sidebar-subheader" style="padding-top: 16px;">
                    <h2>Units List</h2>
                    <div class="add-buttons-group" style="display: flex; gap: 4px;">
                        <button id="add-unit-btn" class="btn primary-btn" style="padding: 4px 8px;" title="Add Unit">+ Add</button>
                        <button id="add-unit-5-btn" class="btn secondary-btn small-btn" style="padding: 4px 8px;" title="Add 5 Units">+5</button>
                    </div>
                </div>
                <div class="search-container">
                    <input type="text" id="search-input" placeholder="Search units..." />
                </div>
                <div id="unit-list" class="unit-list"></div>
            </div>
            <div class="main-content">
                <div id="empty-state" class="empty-state">
                    <div class="empty-state-content">
                        <svg width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                            <path d="M12 2L2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5" />
                        </svg>
                        <h3>Select a unit to edit</h3>
                        <p>Or configure Map Properties in the global tabs.</p>
                    </div>
                </div>
            <div id="editor-form" class="editor-form hidden">
                <div class="form-header">
                    <div style="display: flex; justify-content: space-between; align-items: center; width: 100%;">
                        <div>
                            <div class="breadcrumb" id="editor-breadcrumb">Units</div>
                            <h2 id="editor-title">Edit Unit</h2>
                            <span id="editor-subtitle" class="subtitle">Unit ID</span>
                        </div>
                        <div class="header-actions" style="display: flex; gap: 6px;">
                            <button type="button" id="copy-unit-btn" class="btn secondary-btn" title="Copy unit to clipboard">✂️ Copy Unit</button>
                            <button type="button" id="paste-unit-btn" class="btn secondary-btn" title="Paste unit from clipboard">📋 Paste Unit</button>
                            <button type="button" id="duplicate-unit-btn" class="btn secondary-btn">📋 Duplicate Unit</button>
                        </div>
                    </div>
                </div>
                <div class="form-scroll-container">
                    <div class="form-section">
                        <h3>General Information</h3>
                        <div class="form-group">
                            <label for="field-UnitId">Unit ID</label>
                            <input type="text" id="field-UnitId" required />
                        </div>
                        <div class="form-group">
                            <label for="field-Name">Name</label>
                            <input type="text" id="field-Name" required />
                        </div>
                        <div class="form-group">
                            <label for="field-Description">Description</label>
                            <textarea id="field-Description" rows="3" required></textarea>
                        </div>
                        <div class="form-group">
                            <label for="field-ModelPath">Model Path (Optional)</label>
                            <div class="input-with-browse">
                                <input type="text" id="field-ModelPath" />
                                <button type="button" class="btn browse-btn" data-input-id="field-ModelPath" data-file-types="gltf,glb,scn,tscn" title="Browse files">📁</button>
                                <button type="button" class="btn clear-btn" data-input-id="field-ModelPath" title="Clear path">❌</button>
                            </div>
                        </div>
                        <div class="form-group">
                            <label for="field-PortraitModelPath">Portrait Model Path (Optional)</label>
                            <div class="input-with-browse">
                                <input type="text" id="field-PortraitModelPath" />
                                <button type="button" class="btn browse-btn" data-input-id="field-PortraitModelPath" data-file-types="gltf,glb,scn,tscn" title="Browse files">📁</button>
                                <button type="button" class="btn clear-btn" data-input-id="field-PortraitModelPath" title="Clear path">❌</button>
                            </div>
                        </div>
                        <div class="form-group checkbox-group">
                            <input type="checkbox" id="field-IsHero" />
                            <label for="field-IsHero">Is Hero</label>
                        </div>
                    </div>

                    <div class="form-section">
                        <h3>Attributes & Stats</h3>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-MaxHp">Max HP</label>
                                <input type="number" id="field-MaxHp" min="0" step="any" required />
                            </div>
                            <div class="form-group">
                                <label for="field-Damage">Damage</label>
                                <input type="number" id="field-Damage" min="0" step="any" required />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-Range">Range</label>
                                <input type="number" id="field-Range" min="0" step="any" required />
                            </div>
                            <div class="form-group">
                                <label for="field-Armor">Armor</label>
                                <input type="number" id="field-Armor" min="0" step="any" required />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-Speed">Speed</label>
                                <input type="number" id="field-Speed" min="0" step="any" required />
                            </div>
                            <div class="form-group">
                                <label for="field-AttackCooldown">Attack Cooldown</label>
                                <input type="number" id="field-AttackCooldown" min="0" step="any" required />
                            </div>
                        </div>
                        <div class="form-group">
                            <label for="field-ScanRadius">Scan Radius</label>
                            <input type="number" id="field-ScanRadius" min="0" step="any" required />
                        </div>
                    </div>

                    <div class="form-section">
                        <h3>Resource Costs & Production</h3>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-CostGold">Gold Cost</label>
                                <input type="number" id="field-CostGold" min="0" step="any" required />
                            </div>
                            <div class="form-group">
                                <label for="field-CostWood">Wood Cost</label>
                                <input type="number" id="field-CostWood" min="0" step="any" required />
                            </div>
                            <div class="form-group">
                                <label for="field-CostStone">Stone Cost</label>
                                <input type="number" id="field-CostStone" min="0" step="any" required />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-PopCost">Population Cost</label>
                                <input type="number" id="field-PopCost" min="0" step="1" required />
                            </div>
                            <div class="form-group">
                                <label for="field-ProductionTime">Production Time</label>
                                <input type="number" id="field-ProductionTime" min="0" step="any" required />
                            </div>
                        </div>
                    </div>

                    <div class="form-section">
                        <h3>Combat Types & Rewards</h3>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-AttackType">Attack Type</label>
                                <select id="field-AttackType" required>
                                    <option value="melee">Melee</option>
                                    <option value="ranged">Ranged</option>
                                    <option value="none">None</option>
                                </select>
                            </div>
                            <div class="form-group">
                                <label for="field-ArmorType">Armor Type</label>
                                <select id="field-ArmorType" required>
                                    <option value="light">Light</option>
                                    <option value="heavy">Heavy</option>
                                    <option value="building">Building</option>
                                </select>
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-GoldBounty">Gold Bounty</label>
                                <input type="number" id="field-GoldBounty" min="0" step="any" required />
                            </div>
                            <div class="form-group">
                                <label for="field-XpBounty">XP Bounty (Optional)</label>
                                <input type="number" id="field-XpBounty" min="0" step="any" />
                            </div>
                        </div>
                    </div>

                    <div class="form-section">
                        <h3>Lists & Capabilities</h3>
                        <div class="form-group">
                            <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 4px;">
                                <label style="margin-bottom: 0;">Build Options (Optional)</label>
                                <div style="display: flex; gap: 4px;">
                                    <button type="button" class="btn small-btn copy-unit-comp-btn" data-key="BuildOptions" title="Copy Build Options block">📋 Copy</button>
                                    <button type="button" class="btn small-btn paste-unit-comp-btn" data-key="BuildOptions" title="Paste Build Options block">📥 Paste</button>
                                </div>
                            </div>
                            <div id="build-options-container" class="tag-list-container">
                                <div class="tags" id="build-options-tags"></div>
                                <div class="tag-input-row">
                                    <input type="text" id="build-option-input" list="suggest-units" placeholder="Add build option..." />
                                    <button type="button" id="add-build-option-btn" class="btn secondary-btn">+</button>
                                </div>
                            </div>
                        </div>
                        <div class="form-group">
                            <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 4px;">
                                <label style="margin-bottom: 0;">Abilities (Optional)</label>
                                <div style="display: flex; gap: 4px;">
                                    <button type="button" class="btn small-btn copy-unit-comp-btn" data-key="Abilities" title="Copy Abilities block">📋 Copy</button>
                                    <button type="button" class="btn small-btn paste-unit-comp-btn" data-key="Abilities" title="Paste Abilities block">📥 Paste</button>
                                </div>
                            </div>
                            <div id="abilities-container" class="tag-list-container">
                                <div class="tags" id="abilities-tags"></div>
                                <div class="tag-input-row">
                                    <input type="text" id="ability-input" list="suggest-abilities" placeholder="Add ability..." />
                                    <button type="button" id="add-ability-btn" class="btn secondary-btn">+</button>
                                </div>
                            </div>
                        </div>
                        <div class="form-group">
                            <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 4px;">
                                <label style="margin-bottom: 0;">Weapons (Optional)</label>
                                <div style="display: flex; gap: 4px;">
                                    <button type="button" class="btn small-btn copy-unit-comp-btn" data-key="Weapons" title="Copy Weapons block">📋 Copy</button>
                                    <button type="button" class="btn small-btn paste-unit-comp-btn" data-key="Weapons" title="Paste Weapons block">📥 Paste</button>
                                </div>
                            </div>
                            <div id="weapons-container" class="tag-list-container">
                                <div class="tags" id="weapons-tags"></div>
                                <div class="tag-input-row">
                                    <input type="text" id="weapon-input" list="suggest-weapons" placeholder="Add custom weapon ID..." />
                                    <button type="button" id="add-weapon-btn" class="btn secondary-btn">+</button>
                                </div>
                            </div>
                        </div>
                        <div class="form-group">
                            <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 4px;">
                                <label style="margin-bottom: 0;">Starting Items (Optional)</label>
                                <div style="display: flex; gap: 4px;">
                                    <button type="button" class="btn small-btn copy-unit-comp-btn" data-key="StartingItems" title="Copy Starting Items block">📋 Copy</button>
                                    <button type="button" class="btn small-btn paste-unit-comp-btn" data-key="StartingItems" title="Paste Starting Items block">📥 Paste</button>
                                </div>
                            </div>
                            <div id="items-container" class="tag-list-container">
                                <div class="tags" id="items-tags"></div>
                                <div class="tag-input-row">
                                    <input type="text" id="item-input" list="suggest-items" placeholder="Add custom item ID..." />
                                    <button type="button" id="add-item-btn" class="btn secondary-btn">+</button>
                                </div>
                            </div>
                        </div>
                        <div class="form-group">
                            <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 4px;">
                                <label style="margin-bottom: 0;">Tech Upgrades (Optional)</label>
                                <div style="display: flex; gap: 4px;">
                                    <button type="button" class="btn small-btn copy-unit-comp-btn" data-key="Upgrades" title="Copy Tech Upgrades block">📋 Copy</button>
                                    <button type="button" class="btn small-btn paste-unit-comp-btn" data-key="Upgrades" title="Paste Tech Upgrades block">📥 Paste</button>
                                </div>
                            </div>
                            <div id="upgrades-container" class="tag-list-container">
                                <div class="tags" id="upgrades-tags"></div>
                                <div class="tag-input-row">
                                    <input type="text" id="upgrade-input" list="suggest-upgrades" placeholder="Add custom upgrade ID..." />
                                    <button type="button" id="add-upgrade-btn" class="btn secondary-btn">+</button>
                                </div>
                            </div>
                        </div>
                        <div class="form-group">
                            <label>Movement Type</label>
                            <select id="field-MovementType">
                                <option value="ground">Ground</option>
                                <option value="air">Air</option>
                                <option value="amphibious">Amphibious</option>
                                <option value="none">None</option>
                            </select>
                        </div>
                        <div class="form-group">
                            <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 4px;">
                                <label style="margin-bottom: 0;">Status Effects / Buffs (Optional)</label>
                                <div style="display: flex; gap: 4px;">
                                    <button type="button" class="btn small-btn copy-unit-comp-btn" data-key="StatusEffects" title="Copy Status Effects block">📋 Copy</button>
                                    <button type="button" class="btn small-btn paste-unit-comp-btn" data-key="StatusEffects" title="Paste Status Effects block">📥 Paste</button>
                                </div>
                            </div>
                            <div id="statuseffects-container" class="tag-list-container">
                                <div class="tags" id="statuseffects-tags"></div>
                                <div class="tag-input-row">
                                    <input type="text" id="statuseffect-input" placeholder="Add passive buff ID..." />
                                    <button type="button" id="add-statuseffect-btn" class="btn secondary-btn">+</button>
                                </div>
                            </div>
                        </div>
                        <div class="form-group">
                            <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 4px;">
                                <label style="margin-bottom: 0;">Audio Sound Events (Optional)</label>
                                <div style="display: flex; gap: 4px;">
                                    <button type="button" class="btn small-btn copy-unit-comp-btn" data-key="SoundEvents" title="Copy Sound Events block">📋 Copy</button>
                                    <button type="button" class="btn small-btn paste-unit-comp-btn" data-key="SoundEvents" title="Paste Sound Events block">📥 Paste</button>
                                </div>
                            </div>
                            <div id="soundevents-container" class="tag-list-container">
                                <div class="tags" id="soundevents-tags"></div>
                                <div class="tag-input-row">
                                    <input type="text" id="soundevent-input" placeholder="Add audio event..." />
                                    <button type="button" id="add-soundevent-btn" class="btn secondary-btn">+</button>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
            <div id="map-properties-form" class="editor-form hidden">
                <div class="form-header">
                    <div>
                        <div class="breadcrumb" id="map-properties-breadcrumb">Map > Properties</div>
                        <h2>Map Properties</h2>
                        <span class="subtitle">General configuration metadata</span>
                    </div>
                </div>
                <div class="form-scroll-container">
                    <div class="form-section">
                        <h3>General Information</h3>
                        <div class="form-group">
                            <label for="prop-MapName">Map Name</label>
                            <input type="text" id="prop-MapName" />
                        </div>
                        <div class="form-group">
                            <label for="prop-MapDescription">Description</label>
                            <textarea id="prop-MapDescription" rows="3"></textarea>
                        </div>
                        <div class="form-group">
                            <label for="prop-SuggestedPlayers">Suggested Players</label>
                            <input type="text" id="prop-SuggestedPlayers" placeholder="e.g. 2-4 Players" />
                        </div>
                    </div>
                    <div class="form-section">
                        <h3>Visuals & Assets</h3>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="prop-MinimapImage">Minimap Image</label>
                                <div class="input-with-browse">
                                    <input type="text" id="prop-MinimapImage" placeholder="res://Assets/...png" />
                                    <button type="button" class="btn browse-btn" data-input-id="prop-MinimapImage" data-file-types="png,jpg,jpeg,svg,tga,dds" title="Browse files">📁</button>
                                    <button type="button" class="btn clear-btn" data-input-id="prop-MinimapImage" title="Clear path">❌</button>
                                </div>
                            </div>
                            <div class="form-group">
                                <label for="prop-FogOfWarType">Fog of War Style</label>
                                <select id="prop-FogOfWarType">
                                    <option value="visible">Always Visible</option>
                                    <option value="grey">Grey Mask (Shroud)</option>
                                    <option value="black">Black (Unexplored)</option>
                                </select>
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="prop-TerrainBaseHeight">Terrain Base Height</label>
                                <input type="number" id="prop-TerrainBaseHeight" min="0" step="any" />
                            </div>
                            <div class="form-group">
                                <label for="prop-ShadowIntensity">Shadow Intensity</label>
                                <input type="number" id="prop-ShadowIntensity" min="0" step="any" />
                            </div>
                        </div>
                    </div>
                    <div class="form-section">
                        <h3>Dimensions & Limits</h3>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="prop-MapWidth">Map Width</label>
                                <input type="number" id="prop-MapWidth" min="0" step="1" />
                            </div>
                            <div class="form-group">
                                <label for="prop-MapHeight">Map Height</label>
                                <input type="number" id="prop-MapHeight" min="0" step="1" />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="prop-PlayableWidth">Playable Width</label>
                                <input type="number" id="prop-PlayableWidth" min="0" step="1" />
                            </div>
                            <div class="form-group">
                                <label for="prop-PlayableHeight">Playable Height</label>
                                <input type="number" id="prop-PlayableHeight" min="0" step="1" />
                            </div>
                        </div>
                    </div>
                    <div class="form-section">
                        <h3>Loading Screen</h3>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="prop-LoadingImage">Loading Image</label>
                                <div class="input-with-browse">
                                    <input type="text" id="prop-LoadingImage" placeholder="res://Assets/...png" />
                                    <button type="button" class="btn browse-btn" data-input-id="prop-LoadingImage" data-file-types="png,jpg,jpeg,svg,tga,dds" title="Browse files">📁</button>
                                    <button type="button" class="btn clear-btn" data-input-id="prop-LoadingImage" title="Clear path">❌</button>
                                </div>
                            </div>
                            <div class="form-group">
                                <label for="prop-LoadingMusic">Loading Music</label>
                                <div class="input-with-browse">
                                    <input type="text" id="prop-LoadingMusic" placeholder="res://Assets/...ogg" />
                                    <button type="button" class="btn browse-btn" data-input-id="prop-LoadingMusic" data-file-types="ogg,wav,mp3" title="Browse files">📁</button>
                                    <button type="button" class="btn clear-btn" data-input-id="prop-LoadingMusic" title="Clear path">❌</button>
                                </div>
                            </div>
                        </div>
                        <div class="form-group">
                            <label for="prop-LoadingTitle">Loading Title</label>
                            <input type="text" id="prop-LoadingTitle" />
                        </div>
                        <div class="form-group">
                            <label for="prop-LoadingSubtitle">Loading Subtitle</label>
                            <input type="text" id="prop-LoadingSubtitle" />
                        </div>
                        <div class="form-group">
                            <label for="prop-LoadingBodyText">Loading Description / Lore</label>
                            <textarea id="prop-LoadingBodyText" rows="3"></textarea>
                        </div>
                    </div>
                    <div class="form-section">
                        <h3>Lobby Instructions</h3>
                        <div class="form-group">
                            <label for="prop-HowToPlayObjective">Lobby Objective</label>
                            <input type="text" id="prop-HowToPlayObjective" placeholder="e.g. Destroy the enemy town center" />
                        </div>
                        <div class="form-group">
                            <label>Lobby Instructions List (Optional)</label>
                            <div id="instructions-container" class="tag-list-container">
                                <div class="tags" id="instructions-tags"></div>
                                <div class="tag-input-row">
                                    <input type="text" id="instruction-input" placeholder="Add lobby instruction..." />
                                    <button type="button" id="add-instruction-btn" class="btn secondary-btn">+</button>
                                </div>
                            </div>
                        </div>
                    </div>
                    <div class="form-section">
                        <h3>Player Slots</h3>
                        <div id="player-slots-container" class="list-editor-container">
                            <div id="player-slots-list"></div>
                            <button type="button" id="add-player-slot-btn" class="btn secondary-btn">+ Add Player Slot</button>
                        </div>
                    </div>
                    <div class="form-section">
                        <h3>Teams & Alliances</h3>
                        <div id="teams-container" class="list-editor-container">
                            <div id="teams-list"></div>
                            <button type="button" id="add-team-btn" class="btn secondary-btn">+ Add Team</button>
                        </div>
                    </div>
                    <div class="form-section">
                        <h3>Changelog & Versioning</h3>
                        <div class="form-group">
                            <label for="prop-Version">Map Version</label>
                            <input type="text" id="prop-Version" placeholder="e.g. 1.0.0" />
                        </div>
                        <div id="changelog-container" class="list-editor-container">
                            <div id="changelog-list"></div>
                            <button type="button" id="add-changelog-btn" class="btn secondary-btn">+ Add Changelog Entry</button>
                        </div>
                    </div>
                </div>
            </div>
            <div id="custom-weapons-form" class="editor-form hidden">
                <div class="form-header">
                    <div>
                        <div class="breadcrumb">Map > Custom Weapons</div>
                        <h2>Custom Weapons</h2>
                        <span class="subtitle">Combat attack configurations</span>
                    </div>
                </div>
                <div class="form-scroll-container">
                    <div class="form-section">
                        <div id="weapons-list-container" class="list-editor-container">
                            <div id="custom-weapons-list"></div>
                            <div class="add-buttons-row" style="display: flex; gap: 8px;">
                                <button type="button" id="add-custom-weapon-btn" class="btn secondary-btn">+ Add Custom Weapon</button>
                                <button type="button" id="add-custom-weapon-5-btn" class="btn secondary-btn small-btn" title="Add 5 Weapons">+5</button>
                                <button type="button" id="paste-custom-weapon-btn" class="btn secondary-btn" title="Paste Weapon from Clipboard">📋 Paste Weapon</button>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
            <div id="custom-abilities-form" class="editor-form hidden">
                <div class="form-header">
                    <div>
                        <div class="breadcrumb">Map > Custom Abilities</div>
                        <h2>Custom Abilities</h2>
                        <span class="subtitle">Spells and passives catalog</span>
                    </div>
                </div>
                <div class="form-scroll-container">
                    <div class="form-section">
                        <div id="abilities-list-container" class="list-editor-container">
                            <div id="custom-abilities-list"></div>
                            <div class="add-buttons-row" style="display: flex; gap: 8px;">
                                <button type="button" id="add-custom-ability-btn" class="btn secondary-btn">+ Add Custom Ability</button>
                                <button type="button" id="add-custom-ability-5-btn" class="btn secondary-btn small-btn" title="Add 5 Abilities">+5</button>
                                <button type="button" id="paste-custom-ability-btn" class="btn secondary-btn" title="Paste Ability from Clipboard">📋 Paste Ability</button>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
            <div id="custom-upgrades-form" class="editor-form hidden">
                <div class="form-header">
                    <div>
                        <div class="breadcrumb">Map > Custom Upgrades</div>
                        <h2>Custom Upgrades</h2>
                        <span class="subtitle">Researchable tech upgrades</span>
                    </div>
                </div>
                <div class="form-scroll-container">
                    <div class="form-section">
                        <div id="upgrades-list-container" class="list-editor-container">
                            <div id="custom-upgrades-list"></div>
                            <div class="add-buttons-row" style="display: flex; gap: 8px;">
                                <button type="button" id="add-custom-upgrade-btn" class="btn secondary-btn">+ Add Custom Upgrade</button>
                                <button type="button" id="add-custom-upgrade-5-btn" class="btn secondary-btn small-btn" title="Add 5 Upgrades">+5</button>
                                <button type="button" id="paste-custom-upgrade-btn" class="btn secondary-btn" title="Paste Upgrade from Clipboard">📋 Paste Upgrade</button>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
            <div id="custom-items-form" class="editor-form hidden">
                <div class="form-header">
                    <div>
                        <div class="breadcrumb">Map > Custom Items</div>
                        <h2>Custom Items</h2>
                        <span class="subtitle">Inventory item specifications</span>
                    </div>
                </div>
                <div class="form-scroll-container">
                    <div class="form-section">
                        <div id="items-list-container" class="list-editor-container">
                            <div id="custom-items-list"></div>
                            <div class="add-buttons-row" style="display: flex; gap: 8px;">
                                <button type="button" id="add-custom-item-btn" class="btn secondary-btn">+ Add Custom Item</button>
                                <button type="button" id="add-custom-item-5-btn" class="btn secondary-btn small-btn" title="Add 5 Items">+5</button>
                                <button type="button" id="paste-custom-item-btn" class="btn secondary-btn" title="Paste Item from Clipboard">📋 Paste Item</button>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
            
            <div id="custom-assets-form" class="editor-form hidden">
                <div class="form-header">
                    <div>
                        <div class="breadcrumb">Map > Assets Manager</div>
                        <h2>Assets Manager</h2>
                        <span class="subtitle">Import and manage textures, 3D models, decals, VFX, and audio</span>
                    </div>
                </div>
                <div class="form-scroll-container">
                    <div class="form-section">
                        <h3>🎨 Import Terrain Texture</h3>
                        <p class="desc" style="margin-bottom: 12px; color: var(--text-muted);">Import a custom terrain texture image. It will append as a new paint swatch and be converted into PBR KTX2 format with normal & AO maps.</p>
                        <div class="form-row">
                            <div class="form-group">
                                <button type="button" id="btn-import-texture" class="btn primary-btn">📥 Import Custom Texture...</button>
                            </div>
                        </div>
                    </div>

                    <div class="form-section">
                        <h3>📦 Import 3D Model (GLB)</h3>
                        <p class="desc" style="margin-bottom: 12px; color: var(--text-muted);">Import binary GLB 3D models. Subcategory will categorize BLAKE3 hash in metadata.json under Character, Building, Environment, or Props.</p>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="glb-category-select">Category</label>
                                <select id="glb-category-select">
                                    <option value="character">Character</option>
                                    <option value="building">Building</option>
                                    <option value="environment">Environment</option>
                                    <option value="props">Props</option>
                                </select>
                            </div>
                            <div class="form-group" style="display: flex; align-items: flex-end;">
                                <button type="button" id="btn-import-glb" class="btn secondary-btn">📥 Import GLB Model...</button>
                            </div>
                        </div>
                    </div>

                    <div class="form-section">
                        <h3>🌌 Import Skybox Panoramic Image</h3>
                        <p class="desc" style="margin-bottom: 12px; color: var(--text-muted);">Import a 360-degree panoramic HDRI / skybox image (PNG, JPG, EXR, HDR, etc.). Image will convert to PNG format for Godot world environment rendering.</p>
                        <div class="form-row">
                            <button type="button" id="btn-import-skybox" class="btn secondary-btn">📥 Import Skybox Image...</button>
                        </div>
                    </div>

                    <div class="form-section">
                        <h3>🖼️ Import Decal & 2D Icon</h3>
                        <p class="desc" style="margin-bottom: 12px; color: var(--text-muted);">Import decal and UI icon images (PNG, JPG, BMP, etc.). Image will automatically convert to lossless PNG format.</p>
                        <div class="form-row" style="gap: 16px;">
                            <button type="button" id="btn-import-decal" class="btn secondary-btn">📥 Import PNG Decal...</button>
                            <button type="button" id="btn-import-icon" class="btn secondary-btn">📥 Import 2D Icon...</button>
                        </div>
                    </div>

                    <div class="form-section">
                        <h3>💥 Import VFX Spritesheet</h3>
                        <p class="desc" style="margin-bottom: 12px; color: var(--text-muted);">Import animated VFX spritesheet. Specify grid frame counts for columns and rows.</p>
                        <div class="form-row" style="gap: 16px;">
                            <div class="form-group" style="width: 80px;">
                                <label for="vfx-cols-input">Columns</label>
                                <input type="number" id="vfx-cols-input" value="4" min="1" max="64" />
                            </div>
                            <div class="form-group" style="width: 80px;">
                                <label for="vfx-rows-input">Rows</label>
                                <input type="number" id="vfx-rows-input" value="4" min="1" max="64" />
                            </div>
                            <div class="form-group" style="display: flex; align-items: flex-end;">
                                <button type="button" id="btn-import-vfx" class="btn secondary-btn">📥 Import VFX Spritesheet...</button>
                            </div>
                        </div>
                    </div>

                    <div class="form-section">
                        <h3>🎵 Import Audio (Sound Effects / Music)</h3>
                        <p class="desc" style="margin-bottom: 12px; color: var(--text-muted);">Import audio files (MP3, WAV, FLAC, OGG, etc.). Audio will automatically convert to OGG Vorbis format.</p>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="audio-type-select">Audio Type</label>
                                <select id="audio-type-select">
                                    <option value="sfx">Sound Effect (SFX)</option>
                                    <option value="music">Music</option>
                                </select>
                            </div>
                            <div class="form-group" style="display: flex; align-items: flex-end;">
                                <button type="button" id="btn-import-audio" class="btn secondary-btn">📥 Import Audio File...</button>
                            </div>
                        </div>
                    </div>

                    <div class="form-section">
                        <h3>📂 Current Map Assets</h3>
                        <div id="assets-metadata-display" class="tag-list-container" style="padding: 12px; font-family: monospace; font-size: 12px; max-height: 250px; overflow-y: auto;">
                            <em>No assets registered yet.</em>
                        </div>
                    </div>
                </div>
            </div>
            
            <div id="debug-json-container" class="debug-json-container collapsed hidden">
                <div class="debug-json-header">
                    <h3>Debug Data JSON (Read-only)</h3>
                    <div class="debug-json-actions">
                        <button type="button" id="copy-json-btn" class="btn secondary-btn small-btn">Copy JSON</button>
                        <button type="button" id="expand-json-btn" class="btn secondary-btn small-btn">Expand</button>
                    </div>
                </div>
                <div class="debug-json-body">
                    <pre><code id="debug-json-pre"></code></pre>
                </div>
            </div>
        </div>
        </div>
    </div>
    <script nonce="${nonce}" src="${scriptUri}"></script>
</body>
</html>`;
    }

    private getNonce(): string {
        let text = '';
        const possible = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
        for (let i = 0; i < 32; i++) {
            text += possible.charAt(Math.floor(Math.random() * possible.length));
        }
        return text;
    }
}
