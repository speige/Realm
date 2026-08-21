import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import parseExrModule from 'parse-exr';
const parseExr: (buffer: ArrayBuffer) => any = (parseExrModule as any).default || parseExrModule;

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
                        await this.processImportedAssetFile(webviewPanel.webview, e.fileName || e.filePath, e.fileDataBase64, e.assetType, e.options, document);
                    }
                    break;
                case 'migrateAsset':
                    await this.handleMigrateAsset(e.fromCategory, e.fromSubCategory, e.key, e.toCategory, e.toSubCategory, document);
                    break;
                case 'deleteAsset':
                    await this.handleDeleteAsset(e.category, e.subCategory, e.key, document);
                    break;
                case 'pruneUnusedAssets':
                    await this.handlePruneUnusedAssets(webviewPanel.webview, document);
                    break;
                case 'pruneDomain':
                    await this.handlePruneDomain(webviewPanel.webview, e.domain, document);
                    break;
                case 'openDevTools':
                    vscode.commands.executeCommand('workbench.action.webview.openDeveloperTools');
                    break;
            }
        });
    }

    private async handleDeleteAsset(
        category: string,
        subCategory: string | undefined,
        key: string,
        document: vscode.TextDocument
    ) {
        if (!key) return;
        const targetDir = path.dirname(document.uri.fsPath);
        let relPath = '';

        if (category === 'glb' && subCategory) {
            relPath = path.join('Assets', 'models', subCategory, key);
        } else if (category === 'animations') {
            relPath = path.join('Assets', 'animations', key);
        } else if (category === 'decals') {
            relPath = path.join('Assets', 'decals', key);
        } else if (category === 'icons') {
            relPath = path.join('Assets', 'icons', key);
        } else if (category === 'vfx_spritesheets') {
            relPath = path.join('Assets', 'vfx', key);
        } else if (category === 'skyboxes') {
            relPath = path.join('Assets', 'skyboxes', key);
        } else if (category === 'textures') {
            relPath = path.join('Assets', 'textures', key);
        } else if (category === 'sfx') {
            relPath = path.join('Assets', 'audio', 'sfx', key);
        } else if (category === 'music') {
            relPath = path.join('Assets', 'audio', 'music', key);
        } else {
            relPath = path.join('Assets', category, key);
        }

        const fullPath = path.join(targetDir, relPath);
        if (fs.existsSync(fullPath)) {
            try {
                fs.unlinkSync(fullPath);
                vscode.window.showInformationMessage(`Deleted asset file: ${relPath}`);
            } catch (err: any) {
                console.error(`Failed to delete asset file ${fullPath}:`, err);
                vscode.window.showErrorMessage(`Failed to delete asset file ${relPath}: ${err.message}`);
            }
        }
    }

    private async handlePruneUnusedAssets(webview: vscode.Webview, document: vscode.TextDocument) {
        try {
            let metadata: any;
            try {
                metadata = JSON.parse(document.getText());
            } catch (err) {
                vscode.window.showErrorMessage('Cannot prune assets: metadata.json is invalid JSON.');
                return;
            }

            const targetDir = path.dirname(document.uri.fsPath);
            const referencedAssets = new Set<string>();

            function addRef(val: any) {
                if (!val || typeof val !== 'string') return;
                const trimmed = val.trim().toLowerCase();
                if (!trimmed) return;
                referencedAssets.add(trimmed);
                const normalized = trimmed.replace(/\\/g, '/');
                referencedAssets.add(normalized);
                const clean = normalized.replace(/^(res:\/\/|user:\/\/|assets\/)/i, '');
                referencedAssets.add(clean);
                const baseName = path.basename(normalized);
                referencedAssets.add(baseName);
                const withoutExt = baseName.replace(/\.[^/.]+$/, '');
                if (withoutExt) referencedAssets.add(withoutExt);
            }

            function scanCsScripts(dir: string) {
                if (!fs.existsSync(dir)) return;
                try {
                    const entries = fs.readdirSync(dir, { withFileTypes: true });
                    for (const entry of entries) {
                        if (entry.isDirectory()) {
                            if (['bin', 'obj', '.godot', '.git', 'lib', 'node_modules'].includes(entry.name.toLowerCase())) {
                                continue;
                            }
                            scanCsScripts(path.join(dir, entry.name));
                        } else if (entry.isFile() && entry.name.toLowerCase().endsWith('.cs')) {
                            try {
                                const content = fs.readFileSync(path.join(dir, entry.name), 'utf8');
                                const strRegex = /"([^"\\]*(?:\\.[^"\\]*)*)"/g;
                                let match;
                                while ((match = strRegex.exec(content)) !== null) {
                                    const val = match[1];
                                    if (val && val.length < 200) {
                                        addRef(val);
                                    }
                                }
                            } catch {
                            }
                        }
                    }
                } catch {
                }
            }

            function collectEntityRefs(entityList: any[]) {
                if (!Array.isArray(entityList)) return;
                for (const item of entityList) {
                    if (!item || typeof item !== 'object') continue;
                    addRef(item.UnitId);
                    addRef(item.PropId);
                    addRef(item.Name);
                    addRef(item.ModelPath);
                    addRef(item.PortraitModelPath);
                    addRef(item.IconPath);
                    addRef(item.Icon);
                    addRef(item.DropModelPath);
                    addRef(item.MissileModelPath);
                    addRef(item.ProjectileModelPath);
                    addRef(item.ProjectileModel);
                    addRef(item.EffectModel);
                    addRef(item.VfxPath);
                    addRef(item.Vfx);
                    addRef(item.Spritesheet);
                    addRef(item.SoundEvent);
                    addRef(item.FireSound);
                    addRef(item.HitSound);
                    addRef(item.ImpactSound);
                    addRef(item.CastSound);
                    addRef(item.Sound);

                    if (item.Animations && typeof item.Animations === 'object') {
                        for (const animKey of Object.keys(item.Animations)) {
                            const animVal = item.Animations[animKey];
                            if (Array.isArray(animVal)) {
                                animVal.forEach(a => addRef(a));
                            } else if (typeof animVal === 'string') {
                                addRef(animVal);
                            }
                        }
                    }

                    if (Array.isArray(item.SoundEvents)) {
                        item.SoundEvents.forEach(s => addRef(s));
                    }
                }
            }

            collectEntityRefs(metadata.CustomUnits);
            collectEntityRefs(metadata.CustomBuildings);
            collectEntityRefs(metadata.CustomResources);
            collectEntityRefs(metadata.CustomProps);
            collectEntityRefs(metadata.CustomWeapons);
            collectEntityRefs(metadata.CustomAbilities);
            collectEntityRefs(metadata.CustomUpgrades);
            collectEntityRefs(metadata.CustomItems);

            if (metadata.MapProperties && typeof metadata.MapProperties === 'object') {
                const mp = metadata.MapProperties;
                addRef(mp.MinimapImage);
                addRef(mp.LoadingImage);
                addRef(mp.LoadingMusic);
                addRef(mp.BackgroundMusic);
                addRef(mp.SkyboxPath);
                addRef(mp.SkyboxImage);
                addRef(mp.Skybox);
                addRef(mp.EnvironmentSkybox);
            }

            const terrainPath = path.join(targetDir, 'terrain.json');
            if (fs.existsSync(terrainPath)) {
                try {
                    const terrainData = JSON.parse(fs.readFileSync(terrainPath, 'utf8'));
                    addRef(terrainData.SkyboxPath);
                    if (Array.isArray(terrainData.Units)) {
                        terrainData.Units.forEach((u: any) => {
                            if (u) {
                                addRef(u.UnitId);
                                addRef(u.Name);
                                addRef(u.ModelPath);
                            }
                        });
                    }
                    if (Array.isArray(terrainData.Props)) {
                        terrainData.Props.forEach((p: any) => {
                            if (p) {
                                addRef(p.PropId);
                                addRef(p.Name);
                                addRef(p.ModelPath);
                            }
                        });
                    }
                    if (Array.isArray(terrainData.Decals)) {
                        terrainData.Decals.forEach((d: any) => {
                            if (d) {
                                addRef(d.DecalId);
                                addRef(d.Name);
                            }
                        });
                    }
                } catch {
                }
            }

            // Scan any C# map scripts in workspace
            scanCsScripts(targetDir);

            function isReferenced(fileName: string, subCategory?: string): boolean {
                if (!fileName) return false;
                const trimmed = fileName.trim().toLowerCase();
                if (referencedAssets.has(trimmed)) return true;
                const norm = trimmed.replace(/\\/g, '/');
                if (referencedAssets.has(norm)) return true;
                const baseName = path.basename(norm);
                if (referencedAssets.has(baseName)) return true;
                const withoutExt = baseName.replace(/\.[^/.]+$/, '');
                if (withoutExt && referencedAssets.has(withoutExt)) return true;
                if (subCategory) {
                    const subCatLower = subCategory.toLowerCase();
                    const subNorm = `${subCatLower}/${baseName}`;
                    if (referencedAssets.has(subNorm)) return true;
                    const modelNorm = `models/${subCatLower}/${baseName}`;
                    if (referencedAssets.has(modelNorm)) return true;
                    const assetNorm = `assets/models/${subCatLower}/${baseName}`;
                    if (referencedAssets.has(assetNorm)) return true;
                }
                return false;
            }

            function decodeFloat16(val: number): number {
                const s = (val & 0x8000) >> 15;
                const e = (val & 0x7C00) >> 10;
                const f = val & 0x03FF;
                if (e === 0) {
                    return (s ? -1 : 1) * Math.pow(2, -14) * (f / 1024);
                } else if (e === 0x1F) {
                    return f ? NaN : ((s ? -1 : 1) * Infinity);
                }
                return (s ? -1 : 1) * Math.pow(2, e - 15) * (1 + f / 1024);
            }

            function getExrFloatChannels(exr: any): { width: number; height: number; r: Float32Array; g: Float32Array; b: Float32Array; a: Float32Array } {
                const width = exr.width;
                const height = exr.height;
                const numPixels = width * height;
                const r = new Float32Array(numPixels);
                const g = new Float32Array(numPixels);
                const b = new Float32Array(numPixels);
                const a = new Float32Array(numPixels);

                const isF32 = exr.data instanceof Float32Array;
                const raw = exr.data;

                for (let i = 0; i < numPixels; i++) {
                    const offset = i * 4;
                    if (isF32) {
                        r[i] = raw[offset + 0];
                        g[i] = raw[offset + 1];
                        b[i] = raw[offset + 2];
                        a[i] = raw[offset + 3];
                    } else {
                        r[i] = decodeFloat16(raw[offset + 0]);
                        g[i] = decodeFloat16(raw[offset + 1]);
                        b[i] = decodeFloat16(raw[offset + 2]);
                        a[i] = decodeFloat16(raw[offset + 3]);
                    }
                }

                return { width, height, r, g, b, a };
            }

            function writeUncompressedExr(width: number, height: number, rArr: Float32Array, gArr: Float32Array, bArr: Float32Array, aArr: Float32Array): Buffer {
                const headerParts: Buffer[] = [];

                const magicVer = Buffer.alloc(8);
                magicVer.writeUInt32LE(0x01312f76, 0);
                magicVer.writeUInt32LE(2, 4);
                headerParts.push(magicVer);

                function writeAttr(name: string, type: string, size: number, valBuf: Buffer): Buffer {
                    const nameBuf = Buffer.from(name + '\0', 'utf8');
                    const typeBuf = Buffer.from(type + '\0', 'utf8');
                    const sizeBuf = Buffer.alloc(4);
                    sizeBuf.writeInt32LE(size, 0);
                    return Buffer.concat([nameBuf, typeBuf, sizeBuf, valBuf]);
                }

                const channelNames = ['A', 'B', 'G', 'R'];
                const chEntries: Buffer[] = [];
                for (const ch of channelNames) {
                    const chNameBuf = Buffer.from(ch + '\0', 'utf8');
                    const chDesc = Buffer.alloc(16);
                    chDesc.writeInt32LE(2, 0); // pixelType = 2 (FLOAT)
                    chDesc.writeUInt8(0, 4);   // pLinear
                    chDesc.writeUInt8(0, 5);   // reserved 0
                    chDesc.writeUInt8(0, 6);   // reserved 1
                    chDesc.writeUInt8(0, 7);   // reserved 2
                    chDesc.writeInt32LE(1, 8); // xSampling = 1
                    chDesc.writeInt32LE(1, 12);// ySampling = 1
                    chEntries.push(Buffer.concat([chNameBuf, chDesc]));
                }
                chEntries.push(Buffer.from([0]));
                const chListBuf = Buffer.concat(chEntries);
                headerParts.push(writeAttr('channels', 'chlist', chListBuf.length, chListBuf));

                headerParts.push(writeAttr('compression', 'compression', 1, Buffer.from([0])));

                const dwBuf = Buffer.alloc(16);
                dwBuf.writeInt32LE(0, 0);
                dwBuf.writeInt32LE(0, 4);
                dwBuf.writeInt32LE(width - 1, 8);
                dwBuf.writeInt32LE(height - 1, 12);
                headerParts.push(writeAttr('dataWindow', 'box2i', 16, dwBuf));
                headerParts.push(writeAttr('displayWindow', 'box2i', 16, dwBuf));
                headerParts.push(writeAttr('lineOrder', 'lineOrder', 1, Buffer.from([0])));

                const parBuf = Buffer.alloc(4);
                parBuf.writeFloatLE(1.0, 0);
                headerParts.push(writeAttr('pixelAspectRatio', 'float', 4, parBuf));

                const swcBuf = Buffer.alloc(8);
                swcBuf.writeFloatLE(0.0, 0);
                swcBuf.writeFloatLE(0.0, 4);
                headerParts.push(writeAttr('screenWindowCenter', 'v2f', 8, swcBuf));
                headerParts.push(writeAttr('screenWindowWidth', 'float', 4, parBuf));

                headerParts.push(Buffer.from([0]));

                const headerBuf = Buffer.concat(headerParts);
                const scanlineTableOffset = headerBuf.length;
                const scanlineTableSize = height * 8;
                const firstScanlineOffset = scanlineTableOffset + scanlineTableSize;
                const scanlineDataSize = width * 4 * 4;
                const scanlineTotalSize = 8 + scanlineDataSize;

                const scanlineTable = Buffer.alloc(scanlineTableSize);
                for (let y = 0; y < height; y++) {
                    scanlineTable.writeBigUInt64LE(BigInt(firstScanlineOffset + y * scanlineTotalSize), y * 8);
                }

                const channelArrays: { [key: string]: Float32Array } = {
                    A: aArr,
                    B: bArr,
                    G: gArr,
                    R: rArr
                };

                const scanlines: Buffer[] = [];
                for (let y = 0; y < height; y++) {
                    const slHead = Buffer.alloc(8);
                    slHead.writeInt32LE(y, 0);
                    slHead.writeInt32LE(scanlineDataSize, 4);

                    const chBuffers: Buffer[] = [];
                    for (const ch of channelNames) {
                        const arr = channelArrays[ch];
                        const f32 = new Float32Array(width);
                        for (let x = 0; x < width; x++) {
                            f32[x] = arr[y * width + x];
                        }
                        chBuffers.push(Buffer.from(f32.buffer, f32.byteOffset, f32.byteLength));
                    }
                    scanlines.push(Buffer.concat([slHead, ...chBuffers]));
                }

                return Buffer.concat([headerBuf, scanlineTable, ...scanlines]);
            }

            let prunedAssetsCount = 0;
            let deletedFilesCount = 0;

            const assetsObj = metadata.Assets;
            if (assetsObj && typeof assetsObj === 'object') {
                if (assetsObj.glb && typeof assetsObj.glb === 'object') {
                    for (const subCat of Object.keys(assetsObj.glb)) {
                        const subObj = assetsObj.glb[subCat];
                        if (subObj && typeof subObj === 'object') {
                            for (const fileName of Object.keys(subObj)) {
                                if (!isReferenced(fileName, subCat)) {
                                    delete subObj[fileName];
                                    prunedAssetsCount++;
                                    if (metadata.ModelIgnorePlayerColor && metadata.ModelIgnorePlayerColor[fileName]) {
                                        delete metadata.ModelIgnorePlayerColor[fileName];
                                    }
                                    if (metadata.ModelObstacleRadii && metadata.ModelObstacleRadii[fileName]) {
                                        delete metadata.ModelObstacleRadii[fileName];
                                    }
                                    const filePath = path.join(targetDir, 'Assets', 'models', subCat, fileName);
                                    if (fs.existsSync(filePath)) {
                                        try {
                                            fs.unlinkSync(filePath);
                                            deletedFilesCount++;
                                        } catch (err: any) {
                                            console.error(`Failed to delete ${filePath}:`, err);
                                        }
                                    }
                                }
                            }
                            if (Object.keys(subObj).length === 0) {
                                delete assetsObj.glb[subCat];
                            }
                        }
                    }
                    if (Object.keys(assetsObj.glb).length === 0) {
                        delete assetsObj.glb;
                    }
                }

                // Process terrain textures with EXR splat inspection & index remapping
                const usedTextureIndices = new Set<number>();
                let splatFilesPresent = false;

                const splatIndicesPath = path.join(targetDir, 'terrain_splat_indices.exr');
                const splatWeightsPath = path.join(targetDir, 'terrain_splat_weights.exr');
                const cliffIndicesPath = path.join(targetDir, 'terrain_cliff_splat_indices.exr');
                const cliffWeightsPath = path.join(targetDir, 'terrain_cliff_splat_weights.exr');

                function scanSplatExrs(indicesFile: string, weightsFile: string) {
                    if (fs.existsSync(indicesFile) && fs.existsSync(weightsFile)) {
                        try {
                            splatFilesPresent = true;
                            const idxBuf = fs.readFileSync(indicesFile);
                            const wgtBuf = fs.readFileSync(weightsFile);
                            const idxExr = parseExr(idxBuf.buffer.slice(idxBuf.byteOffset, idxBuf.byteOffset + idxBuf.byteLength));
                            const wgtExr = parseExr(wgtBuf.buffer.slice(wgtBuf.byteOffset, wgtBuf.byteOffset + wgtBuf.byteLength));
                            const idxFloats = getExrFloatChannels(idxExr);
                            const wgtFloats = getExrFloatChannels(wgtExr);

                            const total = idxFloats.width * idxFloats.height;
                            for (let i = 0; i < total; i++) {
                                if (wgtFloats.r[i] > 0.001) usedTextureIndices.add(Math.round(idxFloats.r[i]));
                                if (wgtFloats.g[i] > 0.001) usedTextureIndices.add(Math.round(idxFloats.g[i]));
                                if (wgtFloats.b[i] > 0.001) usedTextureIndices.add(Math.round(idxFloats.b[i]));
                                if (wgtFloats.a[i] > 0.001) usedTextureIndices.add(Math.round(idxFloats.a[i]));
                            }

                            return { idxFloats, wgtFloats };
                        } catch (e: any) {
                            console.error(`Failed to parse splat EXR files ${indicesFile}/${weightsFile}:`, e);
                        }
                    }
                    return null;
                }

                const groundSplats = scanSplatExrs(splatIndicesPath, splatWeightsPath);
                const cliffSplats = scanSplatExrs(cliffIndicesPath, cliffWeightsPath);

                if (assetsObj.textures && typeof assetsObj.textures === 'object') {
                    const texturesObj = assetsObj.textures;
                    const origKeys = Object.keys(texturesObj);
                    const newTexturesObj: { [key: string]: any } = {};
                    const indexRemap: { [oldIdx: number]: number } = {};
                    let hasShift = false;

                    for (let oldIdx = 0; oldIdx < origKeys.length; oldIdx++) {
                        const fileName = origKeys[oldIdx];
                        const isUsedByExr = splatFilesPresent ? usedTextureIndices.has(oldIdx) : false;
                        const isUsedByRef = isReferenced(fileName, 'textures');

                        if (isUsedByExr || isUsedByRef) {
                            const newIdx = Object.keys(newTexturesObj).length;
                            newTexturesObj[fileName] = texturesObj[fileName];
                            indexRemap[oldIdx] = newIdx;
                            if (newIdx !== oldIdx) {
                                hasShift = true;
                            }
                        } else {
                            prunedAssetsCount++;
                            indexRemap[oldIdx] = 0;
                            hasShift = true;
                            const filePath = path.join(targetDir, 'Assets', 'textures', fileName);
                            if (fs.existsSync(filePath)) {
                                try {
                                    fs.unlinkSync(filePath);
                                    deletedFilesCount++;
                                } catch (err: any) {
                                    console.error(`Failed to delete ${filePath}:`, err);
                                }
                            }
                        }
                    }

                    if (hasShift) {
                        if (groundSplats) {
                            const { idxFloats } = groundSplats;
                            const total = idxFloats.width * idxFloats.height;
                            for (let i = 0; i < total; i++) {
                                const oldR = Math.round(idxFloats.r[i]);
                                if (indexRemap[oldR] !== undefined) idxFloats.r[i] = indexRemap[oldR];

                                const oldG = Math.round(idxFloats.g[i]);
                                if (indexRemap[oldG] !== undefined) idxFloats.g[i] = indexRemap[oldG];

                                const oldB = Math.round(idxFloats.b[i]);
                                if (indexRemap[oldB] !== undefined) idxFloats.b[i] = indexRemap[oldB];

                                const oldA = Math.round(idxFloats.a[i]);
                                if (indexRemap[oldA] !== undefined) idxFloats.a[i] = indexRemap[oldA];
                            }
                            const outBuf = writeUncompressedExr(
                                idxFloats.width,
                                idxFloats.height,
                                idxFloats.r,
                                idxFloats.g,
                                idxFloats.b,
                                idxFloats.a
                            );
                            fs.writeFileSync(splatIndicesPath, outBuf);
                        }

                        if (cliffSplats) {
                            const { idxFloats } = cliffSplats;
                            const total = idxFloats.width * idxFloats.height;
                            for (let i = 0; i < total; i++) {
                                const oldR = Math.round(idxFloats.r[i]);
                                if (indexRemap[oldR] !== undefined) idxFloats.r[i] = indexRemap[oldR];

                                const oldG = Math.round(idxFloats.g[i]);
                                if (indexRemap[oldG] !== undefined) idxFloats.g[i] = indexRemap[oldG];

                                const oldB = Math.round(idxFloats.b[i]);
                                if (indexRemap[oldB] !== undefined) idxFloats.b[i] = indexRemap[oldB];

                                const oldA = Math.round(idxFloats.a[i]);
                                if (indexRemap[oldA] !== undefined) idxFloats.a[i] = indexRemap[oldA];
                            }
                            const outBuf = writeUncompressedExr(
                                idxFloats.width,
                                idxFloats.height,
                                idxFloats.r,
                                idxFloats.g,
                                idxFloats.b,
                                idxFloats.a
                            );
                            fs.writeFileSync(cliffIndicesPath, outBuf);
                        }
                    }

                    assetsObj.textures = newTexturesObj;
                    if (Object.keys(assetsObj.textures).length === 0) {
                        delete assetsObj.textures;
                    }
                }

                const flatCategories: { [key: string]: string } = {
                    animations: path.join('Assets', 'animations'),
                    decals: path.join('Assets', 'decals'),
                    icons: path.join('Assets', 'icons'),
                    vfx_spritesheets: path.join('Assets', 'vfx'),
                    vfx: path.join('Assets', 'vfx'),
                    skyboxes: path.join('Assets', 'skyboxes'),
                    skybox: path.join('Assets', 'skyboxes'),
                    sfx: path.join('Assets', 'audio', 'sfx'),
                    music: path.join('Assets', 'audio', 'music')
                };

                for (const cat of Object.keys(flatCategories)) {
                    if (assetsObj[cat] && typeof assetsObj[cat] === 'object') {
                        const catObj = assetsObj[cat];
                        for (const fileName of Object.keys(catObj)) {
                            if (!isReferenced(fileName)) {
                                delete catObj[fileName];
                                prunedAssetsCount++;
                                const relDir = flatCategories[cat];
                                const filePath = path.join(targetDir, relDir, fileName);
                                if (fs.existsSync(filePath)) {
                                    try {
                                        fs.unlinkSync(filePath);
                                        deletedFilesCount++;
                                    } catch (err: any) {
                                        console.error(`Failed to delete ${filePath}:`, err);
                                    }
                                }
                            }
                        }
                        if (Object.keys(catObj).length === 0) {
                            delete assetsObj[cat];
                        }
                    }
                }
            }

            const assetDirChecks: { subDir: string; category?: string }[] = [
                { subDir: path.join('Assets', 'models', 'units'), category: 'units' },
                { subDir: path.join('Assets', 'models', 'buildings'), category: 'buildings' },
                { subDir: path.join('Assets', 'models', 'resources'), category: 'resources' },
                { subDir: path.join('Assets', 'models', 'props'), category: 'props' },
                { subDir: path.join('Assets', 'animations') },
                { subDir: path.join('Assets', 'decals') },
                { subDir: path.join('Assets', 'icons') },
                { subDir: path.join('Assets', 'vfx') },
                { subDir: path.join('Assets', 'skyboxes') },
                { subDir: path.join('Assets', 'textures'), category: 'textures' },
                { subDir: path.join('Assets', 'audio', 'sfx') },
                { subDir: path.join('Assets', 'audio', 'music') }
            ];

            for (const { subDir, category } of assetDirChecks) {
                const fullDir = path.join(targetDir, subDir);
                if (fs.existsSync(fullDir)) {
                    try {
                        const entries = fs.readdirSync(fullDir, { withFileTypes: true });
                        for (const entry of entries) {
                            if (entry.isFile()) {
                                const fileName = entry.name;
                                const isKeptTexture = category === 'textures' && assetsObj && assetsObj.textures && assetsObj.textures[fileName];
                                if (!isKeptTexture && !isReferenced(fileName, category)) {
                                    const fullFilePath = path.join(fullDir, fileName);
                                    try {
                                        fs.unlinkSync(fullFilePath);
                                        deletedFilesCount++;
                                    } catch (err) {
                                        console.error(`Failed to delete ${fullFilePath}:`, err);
                                    }
                                }
                            }
                        }
                    } catch {
                    }
                }
            }

            const newText = JSON.stringify(metadata, null, 2);
            await this.updateTextDocument(document, newText);
            webview.postMessage({
                type: 'update',
                text: newText
            });

            if (prunedAssetsCount > 0 || deletedFilesCount > 0) {
                vscode.window.showInformationMessage(`Pruned ${prunedAssetsCount} unreferenced asset metadata entry(s) and deleted ${deletedFilesCount} unreferenced file(s).`);
            } else {
                vscode.window.showInformationMessage('No unreferenced assets found. Everything is currently in use.');
            }
        } catch (err: any) {
            vscode.window.showErrorMessage(`Failed to prune unused assets: ${err.message}`);
        }
    }

    private async handlePruneDomain(webview: vscode.Webview, domain: string, document: vscode.TextDocument) {
        try {
            let metadata: any;
            try {
                metadata = JSON.parse(document.getText());
            } catch (err) {
                vscode.window.showErrorMessage(`Cannot prune ${domain}: metadata.json is invalid JSON.`);
                return;
            }

            const targetDir = path.dirname(document.uri.fsPath);
            const terrainPath = path.join(targetDir, 'terrain.json');
            if (!fs.existsSync(terrainPath)) {
                vscode.window.showWarningMessage(`Cannot prune ${domain}: terrain.json not found in map folder.`);
                return;
            }

            let terrainData: any;
            try {
                terrainData = JSON.parse(fs.readFileSync(terrainPath, 'utf8'));
            } catch (err: any) {
                vscode.window.showErrorMessage(`Cannot prune ${domain}: failed to parse terrain.json: ${err.message}`);
                return;
            }

            const placedIds = new Set<string>();

            function addIdentifier(val: any) {
                if (!val || typeof val !== 'string') return;
                const trimmed = val.trim().toLowerCase();
                if (!trimmed) return;
                placedIds.add(trimmed);
                const normalized = trimmed.replace(/\\/g, '/');
                placedIds.add(normalized);
                const clean = normalized.replace(/^(res:\/\/|user:\/\/|assets\/)/i, '');
                placedIds.add(clean);
                const baseName = path.basename(normalized);
                placedIds.add(baseName);
                const withoutExt = baseName.replace(/\.[^/.]+$/, '');
                if (withoutExt) placedIds.add(withoutExt);
            }

            function scanCsScripts(dir: string) {
                if (!fs.existsSync(dir)) return;
                try {
                    const entries = fs.readdirSync(dir, { withFileTypes: true });
                    for (const entry of entries) {
                        if (entry.isDirectory()) {
                            if (['bin', 'obj', '.godot', '.git', 'lib', 'node_modules'].includes(entry.name.toLowerCase())) {
                                continue;
                            }
                            scanCsScripts(path.join(dir, entry.name));
                        } else if (entry.isFile() && entry.name.toLowerCase().endsWith('.cs')) {
                            try {
                                const content = fs.readFileSync(path.join(dir, entry.name), 'utf8');
                                const strRegex = /"([^"\\]*(?:\\.[^"\\]*)*)"/g;
                                let match;
                                while ((match = strRegex.exec(content)) !== null) {
                                    const val = match[1];
                                    if (val && val.length < 200) {
                                        addIdentifier(val);
                                    }
                                }
                            } catch {
                            }
                        }
                    }
                } catch {
                }
            }

            // 1. Gather all placed IDs from terrain.json (Units, Props, Decals)
            if (Array.isArray(terrainData.Units)) {
                terrainData.Units.forEach((u: any) => {
                    if (u) {
                        addIdentifier(u.UnitId);
                        addIdentifier(u.Name);
                        addIdentifier(u.ModelPath);
                    }
                });
            }
            if (Array.isArray(terrainData.Props)) {
                terrainData.Props.forEach((p: any) => {
                    if (p) {
                        addIdentifier(p.PropId);
                        addIdentifier(p.Name);
                        addIdentifier(p.ModelPath);
                    }
                });
            }
            if (Array.isArray(terrainData.Decals)) {
                terrainData.Decals.forEach((d: any) => {
                    if (d) {
                        addIdentifier(d.DecalId);
                        addIdentifier(d.Name);
                    }
                });
            }

            // 2. Gather all string literals from C# scripts in the map workspace
            scanCsScripts(targetDir);

            // 3. Helper to test if an entity or item matches any placed identifier
            function isEntityReferenced(item: any): boolean {
                if (!item || typeof item !== 'object') return false;
                const candidates = [
                    item.UnitId,
                    item.PropId,
                    item.WeaponId,
                    item.AbilityId,
                    item.UpgradeId,
                    item.ItemId,
                    item.Name,
                    item.ModelPath,
                    item.DropModelPath,
                    item.PortraitModelPath,
                    item.MissileModelPath,
                    item.ProjectileModelPath,
                    item.ProjectileModel,
                    item.EffectModel
                ];
                for (const c of candidates) {
                    if (c && typeof c === 'string') {
                        const trimmed = c.trim().toLowerCase();
                        if (placedIds.has(trimmed)) return true;
                        const normalized = trimmed.replace(/\\/g, '/');
                        if (placedIds.has(normalized)) return true;
                        const clean = normalized.replace(/^(res:\/\/|user:\/\/|assets\/)/i, '');
                        if (placedIds.has(clean)) return true;
                        const baseName = path.basename(normalized);
                        if (placedIds.has(baseName)) return true;
                        const withoutExt = baseName.replace(/\.[^/.]+$/, '');
                        if (withoutExt && placedIds.has(withoutExt)) return true;
                    }
                }
                return false;
            }

            // 4. Transitive expansion: add build options, weapons, abilities, upgrades, items of referenced entities
            let expanded = true;
            let loopCount = 0;
            while (expanded && loopCount < 50) {
                expanded = false;
                loopCount++;
                const prevSize = placedIds.size;

                const allEntities = [
                    ...(metadata.CustomUnits || []),
                    ...(metadata.CustomBuildings || []),
                    ...(metadata.CustomResources || []),
                    ...(metadata.CustomProps || [])
                ];

                for (const entity of allEntities) {
                    if (isEntityReferenced(entity)) {
                        addIdentifier(entity.UnitId);
                        addIdentifier(entity.Name);
                        addIdentifier(entity.ModelPath);

                        if (Array.isArray(entity.BuildOptions)) {
                            entity.BuildOptions.forEach((opt: string) => addIdentifier(opt));
                        }
                        if (Array.isArray(entity.Weapons)) {
                            entity.Weapons.forEach((w: string) => addIdentifier(w));
                        }
                        if (Array.isArray(entity.Abilities)) {
                            entity.Abilities.forEach((a: string) => addIdentifier(a));
                        }
                        if (Array.isArray(entity.Upgrades)) {
                            entity.Upgrades.forEach((u: string) => addIdentifier(u));
                        }
                        if (Array.isArray(entity.StartingItems)) {
                            entity.StartingItems.forEach((i: string) => addIdentifier(i));
                        }
                        if (Array.isArray(entity.Items)) {
                            entity.Items.forEach((i: string) => addIdentifier(i));
                        }
                    }
                }

                if (Array.isArray(metadata.CustomAbilities)) {
                    for (const abi of metadata.CustomAbilities) {
                        if (isEntityReferenced(abi)) {
                            addIdentifier(abi.AbilityId);
                            addIdentifier(abi.Name);
                            if (abi.SummonedUnitId) addIdentifier(abi.SummonedUnitId);
                            if (Array.isArray(abi.GrantedWeapons)) abi.GrantedWeapons.forEach((w: string) => addIdentifier(w));
                        }
                    }
                }

                if (Array.isArray(metadata.CustomUpgrades)) {
                    for (const up of metadata.CustomUpgrades) {
                        if (isEntityReferenced(up)) {
                            addIdentifier(up.UpgradeId);
                            addIdentifier(up.Name);
                            if (Array.isArray(up.GrantedWeapons)) up.GrantedWeapons.forEach((w: string) => addIdentifier(w));
                            if (Array.isArray(up.AffectedUnitIds)) up.AffectedUnitIds.forEach((uid: string) => addIdentifier(uid));
                        }
                    }
                }

                if (Array.isArray(metadata.CustomItems)) {
                    for (const itm of metadata.CustomItems) {
                        if (isEntityReferenced(itm)) {
                            addIdentifier(itm.ItemId);
                            addIdentifier(itm.Name);
                            if (Array.isArray(itm.Abilities)) itm.Abilities.forEach((a: string) => addIdentifier(a));
                            if (Array.isArray(itm.GrantedWeapons)) itm.GrantedWeapons.forEach((w: string) => addIdentifier(w));
                        }
                    }
                }

                if (placedIds.size > prevSize) {
                    expanded = true;
                }
            }

            // 5. Filter target domain
            let initialCount = 0;
            let finalCount = 0;

            if (domain === 'units') {
                initialCount = (metadata.CustomUnits || []).length;
                metadata.CustomUnits = (metadata.CustomUnits || []).filter((u: any) => isEntityReferenced(u));
                finalCount = metadata.CustomUnits.length;
            } else if (domain === 'buildings') {
                initialCount = (metadata.CustomBuildings || []).length;
                metadata.CustomBuildings = (metadata.CustomBuildings || []).filter((b: any) => isEntityReferenced(b));
                finalCount = metadata.CustomBuildings.length;
            } else if (domain === 'resources') {
                initialCount = (metadata.CustomResources || []).length;
                metadata.CustomResources = (metadata.CustomResources || []).filter((r: any) => isEntityReferenced(r));
                finalCount = metadata.CustomResources.length;
            } else if (domain === 'props') {
                initialCount = (metadata.CustomProps || []).length;
                metadata.CustomProps = (metadata.CustomProps || []).filter((p: any) => isEntityReferenced(p));
                finalCount = metadata.CustomProps.length;
            } else if (domain === 'weapons') {
                initialCount = (metadata.CustomWeapons || []).length;
                metadata.CustomWeapons = (metadata.CustomWeapons || []).filter((w: any) => isEntityReferenced(w));
                finalCount = metadata.CustomWeapons.length;
            } else if (domain === 'abilities') {
                initialCount = (metadata.CustomAbilities || []).length;
                metadata.CustomAbilities = (metadata.CustomAbilities || []).filter((a: any) => isEntityReferenced(a));
                finalCount = metadata.CustomAbilities.length;
            } else if (domain === 'upgrades') {
                initialCount = (metadata.CustomUpgrades || []).length;
                metadata.CustomUpgrades = (metadata.CustomUpgrades || []).filter((u: any) => isEntityReferenced(u));
                finalCount = metadata.CustomUpgrades.length;
            } else if (domain === 'items') {
                initialCount = (metadata.CustomItems || []).length;
                metadata.CustomItems = (metadata.CustomItems || []).filter((i: any) => isEntityReferenced(i));
                finalCount = metadata.CustomItems.length;
            }

            const removedCount = initialCount - finalCount;
            const newText = JSON.stringify(metadata, null, 2);
            await this.updateTextDocument(document, newText);
            webview.postMessage({
                type: 'update',
                text: newText
            });

            if (removedCount > 0) {
                vscode.window.showInformationMessage(`Pruned ${removedCount} unplaced item(s) from ${domain}.`);
            } else {
                vscode.window.showInformationMessage(`No unplaced items found in ${domain}. All items are placed or referenced on terrain.`);
            }
        } catch (err: any) {
            vscode.window.showErrorMessage(`Failed to prune ${domain}: ${err.message}`);
        }
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

    private parseGltfJson(fileBytes: Buffer): any {
        try {
            if (fileBytes.length >= 20 && fileBytes.readUInt32LE(0) === 0x46546C67) {
                let currentOffset = 12;
                while (currentOffset + 8 <= fileBytes.length) {
                    const chunkLength = fileBytes.readUInt32LE(currentOffset);
                    const chunkType = fileBytes.readUInt32LE(currentOffset + 4);
                    if (chunkType === 0x4E4F534A) {
                        const jsonBuffer = fileBytes.subarray(currentOffset + 8, currentOffset + 8 + chunkLength);
                        return JSON.parse(jsonBuffer.toString('utf8'));
                    }
                    currentOffset += 8 + chunkLength;
                }
            } else {
                const text = fileBytes.toString('utf8').trim();
                if (text.startsWith('{')) {
                    return JSON.parse(text);
                }
            }
        } catch (e) {
            console.warn('Failed to parse glTF JSON from model:', e);
        }
        return null;
    }

    private getGlbFaceCount(fileBytes: Buffer): number {
        try {
            const gltfJson = this.parseGltfJson(fileBytes);
            if (!gltfJson || !Array.isArray(gltfJson.meshes)) {
                return 0;
            }

            const accessors = Array.isArray(gltfJson.accessors) ? gltfJson.accessors : [];
            let totalFaces = 0;

            for (const mesh of gltfJson.meshes) {
                if (!mesh || !Array.isArray(mesh.primitives)) continue;
                for (const prim of mesh.primitives) {
                    if (!prim) continue;
                    const mode = prim.mode !== undefined ? prim.mode : 4;

                    let elementCount = 0;
                    if (prim.indices !== undefined && accessors[prim.indices]) {
                        elementCount = accessors[prim.indices].count || 0;
                    } else if (prim.attributes && prim.attributes.POSITION !== undefined && accessors[prim.attributes.POSITION]) {
                        elementCount = accessors[prim.attributes.POSITION].count || 0;
                    }

                    if (mode === 4) {
                        totalFaces += Math.floor(elementCount / 3);
                    } else if (mode === 5 || mode === 6) {
                        totalFaces += Math.max(0, elementCount - 2);
                    }
                }
            }

            return totalFaces;
        } catch (e) {
            console.warn('Failed to parse face count from 3D model:', e);
            return 0;
        }
    }

    public findPath(relativePath: string, contextPath?: vscode.Uri | string): string | null {
        if (!relativePath) {
            return null;
        }

        let cleanPath = relativePath.trim().replace(/^res:\/\//i, '');
        cleanPath = cleanPath.replace(/^[/\\]+/, '');

        const searchRoots: string[] = [];

        const addSearchRootAndAncestors = (startPath: string, maxDepth: number = 12) => {
            try {
                let currentDir = fs.existsSync(startPath) && fs.statSync(startPath).isDirectory()
                    ? startPath
                    : path.dirname(startPath);

                for (let i = 0; i < maxDepth; i++) {
                    searchRoots.push(currentDir);
                    const parent = path.dirname(currentDir);
                    if (!parent || parent === currentDir) {
                        break;
                    }
                    currentDir = parent;
                }
            } catch {}
        };

        if (contextPath) {
            const initialPath = typeof contextPath === 'string' ? contextPath : contextPath.fsPath;
            addSearchRootAndAncestors(initialPath);
        }

        const workspaceFolders = vscode.workspace.workspaceFolders;
        if (workspaceFolders) {
            for (const folder of workspaceFolders) {
                addSearchRootAndAncestors(folder.uri.fsPath);
            }
        }

        addSearchRootAndAncestors(__dirname);

        const uniqueRoots = Array.from(new Set(searchRoots));
        const strippedCleanPath = cleanPath.replace(/^(ThirdPartyBinaries|Realm\.Godot)[/\\]/i, '');

        for (const root of uniqueRoots) {
            const candidatePaths = [
                path.join(root, cleanPath),
                path.join(root, 'ThirdPartyBinaries', strippedCleanPath),
                path.join(root, 'ThirdPartyBinaries', cleanPath),
                path.join(root, 'Realm.Godot', cleanPath),
                path.join(root, 'Realm.Godot', 'ThirdPartyBinaries', strippedCleanPath),
                path.join(root, 'Realm.Godot', 'ThirdPartyBinaries', cleanPath),
                path.join(root, 'bin', 'Debug', 'net10.0', cleanPath),
                path.join(root, 'bin', 'Release', 'net10.0', cleanPath),
                path.join(root, 'bin', 'Debug', 'net10.0', 'ThirdPartyBinaries', strippedCleanPath),
                path.join(root, 'bin', 'Release', 'net10.0', 'ThirdPartyBinaries', strippedCleanPath),
                path.join(root, strippedCleanPath)
            ];

            for (const candidate of candidatePaths) {
                if (fs.existsSync(candidate)) {
                    return candidate;
                }
            }
        }

        return null;
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
        } else if (assetType === 'animation') {
            accept = '.ranim,.glb,.gltf,.fbx';
        }

        webview.postMessage({
            type: 'importAssetFallback',
            assetType,
            extraOptions,
            accept
        });
    }

    private async processImportedAssetFile(
        webview: vscode.Webview,
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
                const originalFileBytes = fileBytes;
                let subCategory = (extraOptions && extraOptions.category) ? extraOptions.category.toLowerCase() : 'props';
                const originalFileName = path.basename(fileName, path.extname(fileName)) + '.glb';
                const reqId = 'model_opt_' + Math.random().toString(36).substring(2, 9);
                const fileBase64 = fileBytes.toString('base64');

                const maxTextureResolution = (extraOptions && extraOptions.maxTextureResolution !== undefined) ? extraOptions.maxTextureResolution : 1024;
                const creaseAngleDegrees = (extraOptions && extraOptions.creaseAngleDegrees !== undefined) ? extraOptions.creaseAngleDegrees : 45.0;
                const allowedPixelError = (extraOptions && extraOptions.allowedPixelError !== undefined) ? extraOptions.allowedPixelError : 1.5;
                const forceReDecimate = !!(extraOptions && (extraOptions.forceReDecimate || extraOptions.force_redecimate));
                const useUastc = !!(extraOptions && (extraOptions.useUastc || extraOptions.use_uastc));

                const optimizationResult = await new Promise<{
                    success: boolean;
                    optimizedBase64?: string;
                    originalTriangles?: number;
                    optimizedTriangles?: number;
                    lodTriangleCounts?: number[];
                    reductionRatio?: number;
                    decimationSkipped?: boolean;
                    texturesProcessed?: number;
                    chosenTextureResolution?: number;
                    error?: string;
                }>(async (resolve) => {
                    const http = require('http');
                    const ports = [8092, 8093];
                    let resolved = false;

                    const postData = JSON.stringify({
                        action: 'optimizeModel',
                        requestId: reqId,
                        rawBase64: fileBase64,
                        fileName: fileName,
                        maxTextureResolution,
                        creaseAngleDegrees,
                        allowedPixelError,
                        forceReDecimate,
                        useUastc
                    });

                    for (const port of ports) {
                        if (resolved) break;
                        try {
                            await new Promise<void>((nextPort) => {
                                const req = http.request({
                                    hostname: '127.0.0.1',
                                    port: port,
                                    path: '/api/',
                                    method: 'POST',
                                    headers: {
                                        'Content-Type': 'application/json',
                                        'Content-Length': Buffer.byteLength(postData)
                                    },
                                    timeout: 25000
                                }, (res: any) => {
                                    let data = '';
                                    res.on('data', (chunk: any) => { data += chunk; });
                                    res.on('end', () => {
                                        if (!resolved) {
                                            try {
                                                const parsed = JSON.parse(data);
                                                resolved = true;
                                                resolve(parsed);
                                            } catch {
                                                nextPort();
                                            }
                                        }
                                    });
                                });
                                req.on('error', () => {
                                    nextPort();
                                });
                                req.write(postData);
                                req.end();
                            });
                        } catch {
                        }
                    }

                    if (!resolved) {
                        fallbackWebviewIpc();
                    }

                    function fallbackWebviewIpc() {
                        const timeout = setTimeout(() => {
                            webviewSubscription.dispose();
                            if (!resolved) {
                                resolved = true;
                                resolve({ success: false, error: 'Optimization timed out. Ensure Godot Editor is open and running.' });
                            }
                        }, 25000);

                        const webviewSubscription = webview.onDidReceiveMessage((msg: any) => {
                            if ((msg.action === 'optimizeModelResult' || msg.type === 'optimizeModelResult') && msg.requestId === reqId) {
                                clearTimeout(timeout);
                                webviewSubscription.dispose();
                                if (!resolved) {
                                    resolved = true;
                                    resolve(msg);
                                }
                            }
                        });

                        webview.postMessage({
                            type: 'godotIpc',
                            action: 'optimizeModel',
                            requestId: reqId,
                            rawBase64: fileBase64,
                            fileName: fileName,
                            maxTextureResolution,
                            creaseAngleDegrees,
                            allowedPixelError,
                            forceReDecimate,
                            useUastc
                        });
                    }
                });

                if (optimizationResult.success && optimizationResult.optimizedBase64) {
                    fileBytes = Buffer.from(optimizationResult.optimizedBase64, 'base64');
                    if (optimizationResult.decimationSkipped) {
                        vscode.window.showInformationMessage(`Imported GLB (${subCategory}): ${originalFileName} (Decimation skipped: already optimized in Realm).`);
                    } else {
                        const origTri = optimizationResult.originalTriangles || 0;
                        const optTri = optimizationResult.optimizedTriangles || 0;
                        const pct = (optimizationResult.reductionRatio !== undefined ? (optimizationResult.reductionRatio * 100).toFixed(1) : '100');
                        const lods = optimizationResult.lodTriangleCounts && optimizationResult.lodTriangleCounts.length > 0
                            ? ` [LODs: ${optimizationResult.lodTriangleCounts.join('/')}]`
                            : '';
                        vscode.window.showInformationMessage(`Imported & Optimized GLB (${subCategory}): ${originalFileName} [${origTri} -> ${optTri} tris${lods}, ${pct}% ratio]`);
                    }
                } else {
                    fileBytes = originalFileBytes;
                    vscode.window.showInformationMessage(`Imported GLB Model (${subCategory}): ${originalFileName}`);
                }

                const subDir = path.join(targetDir, 'Assets', 'models', subCategory);
                if (!fs.existsSync(subDir)) fs.mkdirSync(subDir, { recursive: true });
                const baseName = originalFileName;
                const targetPath = path.join(subDir, baseName);
                fs.writeFileSync(targetPath, fileBytes);
                const blake3 = this.computeHashHex(fileBytes);
                const ignorePlayerColor = !!(extraOptions && (extraOptions.ignorePlayerColor || extraOptions.ignore_player_color));
                if (!metadata.Assets.glb) metadata.Assets.glb = {};
                if (!metadata.Assets.glb[subCategory]) metadata.Assets.glb[subCategory] = {};
                metadata.Assets.glb[subCategory][baseName] = {
                    hash: blake3,
                    default_asset_type: subCategory,
                    generate_normals: true,
                    ...(ignorePlayerColor ? { ignore_player_color: true } : {})
                };

                if (!metadata.ModelIgnorePlayerColor) metadata.ModelIgnorePlayerColor = {};
                if (ignorePlayerColor) {
                    metadata.ModelIgnorePlayerColor[baseName] = true;
                }

                const unitId = path.basename(fileName, path.extname(fileName));
                const targetArrayKey = subCategory === 'units' ? 'CustomUnits' :
                                       subCategory === 'buildings' ? 'CustomBuildings' :
                                       subCategory === 'resources' ? 'CustomResources' : 'CustomProps';

                if (!metadata[targetArrayKey] || !Array.isArray(metadata[targetArrayKey])) {
                    metadata[targetArrayKey] = [];
                }
                const exists = metadata[targetArrayKey].some((u: any) => u && u.UnitId === unitId);
                if (!exists) {
                    let defaultPathing = 8;
                    if (subCategory === 'units') defaultPathing = 9;
                    else if (subCategory === 'buildings') defaultPathing = 32;
                    else if (subCategory === 'resources' || subCategory === 'props') defaultPathing = 255;

                    metadata[targetArrayKey].push({
                        UnitId: unitId,
                        Name: unitId,
                        Description: '',
                        PathingType: defaultPathing,
                        ModelPath: baseName,
                        RecalculateNormals: true,
                        ...(ignorePlayerColor ? { IgnorePlayerColor: true } : {})
                    });
                }

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
            } else if (assetType === 'animation') {
                const subDir = path.join(targetDir, 'Assets', 'animations');
                if (!fs.existsSync(subDir)) fs.mkdirSync(subDir, { recursive: true });

                const ext = path.extname(fileName).toLowerCase();

                if (ext === '.ranim') {
                    const cleanBase = path.basename(fileName, path.extname(fileName)).toLowerCase().replace(/[^a-z0-9_]/g, '_');
                    let targetFileName = `${cleanBase}.ranim`;
                    let targetPath = path.join(subDir, targetFileName);
                    const newHash = this.computeHashHex(fileBytes);

                    if (fs.existsSync(targetPath)) {
                        const existingHash = this.computeHashHex(fs.readFileSync(targetPath));
                        if (existingHash.toLowerCase() === newHash.toLowerCase()) {
                            if (!metadata.Assets.animations) metadata.Assets.animations = {};
                            metadata.Assets.animations[targetFileName] = newHash;
                            vscode.window.showInformationMessage(`Animation (${targetFileName}) is already imported (identical BLAKE3 hash).`);
                        } else {
                            let counter = 1;
                            while (fs.existsSync(path.join(subDir, `${cleanBase}_${counter}.ranim`))) {
                                const varPath = path.join(subDir, `${cleanBase}_${counter}.ranim`);
                                const varHash = this.computeHashHex(fs.readFileSync(varPath));
                                if (varHash.toLowerCase() === newHash.toLowerCase()) {
                                    targetFileName = `${cleanBase}_${counter}.ranim`;
                                    break;
                                }
                                counter++;
                            }
                            if (!fs.existsSync(path.join(subDir, `${cleanBase}_${counter}.ranim`))) {
                                targetFileName = `${cleanBase}_${counter}.ranim`;
                                targetPath = path.join(subDir, targetFileName);
                                fs.writeFileSync(targetPath, fileBytes);
                            }
                            if (!metadata.Assets.animations) metadata.Assets.animations = {};
                            metadata.Assets.animations[targetFileName] = newHash;
                            vscode.window.showInformationMessage(`Imported Animation: ${targetFileName}`);
                        }
                    } else {
                        fs.writeFileSync(targetPath, fileBytes);
                        if (!metadata.Assets.animations) metadata.Assets.animations = {};
                        metadata.Assets.animations[targetFileName] = newHash;
                        vscode.window.showInformationMessage(`Imported Animation: ${targetFileName}`);
                    }
                } else if (ext === '.fbx' || ext === '.glb' || ext === '.gltf') {
                    const reqId = 'anim_conv_' + Math.random().toString(36).substring(2, 9);
                    const fileBase64 = fileBytes.toString('base64');

                    const conversionResult = await new Promise<{ success: boolean; extractedFiles?: Array<{ fileName: string; hash: string; animName: string }>; error?: string }>(async (resolve) => {
                        const http = require('http');
                        const ports = [8092, 8093];
                        let resolved = false;

                        const postData = JSON.stringify({
                            action: 'processRawAnimation',
                            requestId: reqId,
                            rawBase64: fileBase64,
                            fileName: fileName,
                            outputAnimsDir: subDir
                        });

                        for (const port of ports) {
                            if (resolved) break;
                            try {
                                await new Promise<void>((nextPort) => {
                                    const req = http.request({
                                        hostname: '127.0.0.1',
                                        port: port,
                                        path: '/api/',
                                        method: 'POST',
                                        headers: {
                                            'Content-Type': 'application/json',
                                            'Content-Length': Buffer.byteLength(postData)
                                        },
                                        timeout: 10000
                                    }, (res: any) => {
                                        let data = '';
                                        res.on('data', (chunk: any) => { data += chunk; });
                                        res.on('end', () => {
                                            if (!resolved) {
                                                try {
                                                    const parsed = JSON.parse(data);
                                                    resolved = true;
                                                    resolve({
                                                        success: !!parsed.success,
                                                        extractedFiles: parsed.extractedFiles,
                                                        error: parsed.error
                                                    });
                                                } catch {
                                                    nextPort();
                                                }
                                            }
                                        });
                                    });
                                    req.on('error', () => {
                                        nextPort();
                                    });
                                    req.write(postData);
                                    req.end();
                                });
                            } catch {
                            }
                        }

                        if (!resolved) {
                            fallbackWebviewIpc();
                        }

                        function fallbackWebviewIpc() {
                            const timeout = setTimeout(() => {
                                webviewSubscription.dispose();
                                if (!resolved) {
                                    resolved = true;
                                    resolve({ success: false, error: 'Animation conversion timed out (10s). Ensure Godot Editor is open and running.' });
                                }
                            }, 10000);

                            const webviewSubscription = webview.onDidReceiveMessage((msg: any) => {
                                if ((msg.action === 'processRawAnimationResult' || msg.type === 'processRawAnimationResult') && msg.requestId === reqId) {
                                    clearTimeout(timeout);
                                    webviewSubscription.dispose();
                                    if (!resolved) {
                                        resolved = true;
                                        resolve({
                                            success: !!msg.success,
                                            extractedFiles: msg.extractedFiles,
                                            error: msg.error
                                        });
                                    }
                                }
                            });

                            webview.postMessage({
                                type: 'godotIpc',
                                action: 'processRawAnimation',
                                requestId: reqId,
                                rawBase64: fileBase64,
                                fileName: fileName,
                                outputAnimsDir: subDir
                            });
                        }
                    });

                    if (conversionResult.success && conversionResult.extractedFiles && conversionResult.extractedFiles.length > 0) {
                        if (!metadata.Assets.animations) metadata.Assets.animations = {};
                        for (const item of conversionResult.extractedFiles) {
                            metadata.Assets.animations[item.fileName] = item.hash;
                        }
                        vscode.window.showInformationMessage(`Successfully imported and converted ${conversionResult.extractedFiles.length} animation(s) (.ranim) from ${fileName}`);
                    } else {
                        const errDetail = conversionResult.error ? `: ${conversionResult.error}` : '.';
                        vscode.window.showErrorMessage(`Failed to convert animation (${fileName}) to .ranim${errDetail}`);
                        return;
                    }
                }
            } else if (assetType === 'texture') {
                const cleanBase = path.basename(fileName, path.extname(fileName)).toLowerCase().replace(/[^a-z0-9_]/g, '_');
                let swatchName = cleanBase || 'custom_texture';
                
                if (!metadata.Assets) metadata.Assets = {};
                if (!metadata.Assets.textures) metadata.Assets.textures = {};

                let finalSwatchName = swatchName;
                let counter = 1;
                while (metadata.Assets.textures[finalSwatchName + '.ktx2']) {
                    finalSwatchName = `${swatchName}_${counter}`;
                    counter++;
                }

                const subDir = path.join(targetDir, 'Assets', 'textures');
                if (!fs.existsSync(subDir)) fs.mkdirSync(subDir, { recursive: true });

                const outputKtx2Path = path.join(subDir, `${finalSwatchName}.ktx2`);
                const reqId = 'tx_conv_' + Math.random().toString(36).substring(2, 9);
                const fileBase64 = fileBytes.toString('base64');

                // Async IPC event promise waiting for Godot's processRawTextureResult response
                const conversionResult = await new Promise<{ success: boolean; error?: string }>(async (resolve) => {
                    // Try direct REST HTTP call to Godot IPC listener first (bypasses iframe webview sandbox limits)
                    try {
                        const http = require('http');
                        const postData = JSON.stringify({
                            action: 'processRawTexture',
                            requestId: reqId,
                            rawBase64: fileBase64,
                            outputKtx2Path: outputKtx2Path,
                            swatchName: finalSwatchName
                        });

                        // Check url query param or fallback ports 8092/8093
                        const req = http.request({
                            hostname: '127.0.0.1',
                            port: 8092,
                            path: '/api/',
                            method: 'POST',
                            headers: {
                                'Content-Type': 'application/json',
                                'Content-Length': Buffer.byteLength(postData)
                            },
                            timeout: 5000
                        }, (res: any) => {
                            let data = '';
                            res.on('data', (chunk: any) => { data += chunk; });
                            res.on('end', () => {
                                try {
                                    const parsed = JSON.parse(data);
                                    resolve({ success: !!parsed.success, error: parsed.error });
                                } catch {
                                    resolve({ success: false, error: 'Invalid JSON response from Godot REST API' });
                                }
                            });
                        });
                        req.on('error', () => {
                            // Fallback to webview postMessage bridge if HTTP listener on 8092 is not reachable
                            fallbackWebviewIpc();
                        });
                        req.write(postData);
                        req.end();
                        return;
                    } catch {
                        fallbackWebviewIpc();
                    }

                    function fallbackWebviewIpc() {
                        const timeout = setTimeout(() => {
                            webviewSubscription.dispose();
                            resolve({ success: false, error: 'Conversion request timed out (5s).' });
                        }, 5000);

                        const webviewSubscription = webview.onDidReceiveMessage((msg: any) => {
                            if ((msg.action === 'processRawTextureResult' || msg.type === 'processRawTextureResult') && (msg.requestId === reqId || msg.swatchName === finalSwatchName)) {
                                clearTimeout(timeout);
                                webviewSubscription.dispose();
                                resolve({ success: !!msg.success, error: msg.error });
                            }
                        });

                        webview.postMessage({
                            type: 'godotIpc',
                            action: 'processRawTexture',
                            requestId: reqId,
                            rawBase64: fileBase64,
                            outputKtx2Path: outputKtx2Path,
                            swatchName: finalSwatchName
                        });
                    }
                });

                if (conversionResult.success && fs.existsSync(outputKtx2Path)) {
                    const ktx2Bytes = fs.readFileSync(outputKtx2Path);
                    const ktx2Hash = this.computeHashHex(ktx2Bytes);
                    metadata.Assets.textures[finalSwatchName + '.ktx2'] = ktx2Hash;
                    vscode.window.showInformationMessage(`Imported Texture (${finalSwatchName}.ktx2) successfully.`);
                } else {
                    const errDetail = conversionResult.error ? `: ${conversionResult.error}` : '.';
                    vscode.window.showErrorMessage(`Failed to convert texture (${finalSwatchName}) to KTX2${errDetail}`);
                    return;
                }
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

            // Merge custom entity arrays into freshMetadata if created during GLB import
            const arrayKeys = ['CustomUnits', 'CustomBuildings', 'CustomResources', 'CustomProps'];
            for (const arrKey of arrayKeys) {
                if (metadata[arrKey] && Array.isArray(metadata[arrKey])) {
                    if (!freshMetadata[arrKey] || !Array.isArray(freshMetadata[arrKey])) {
                        freshMetadata[arrKey] = [];
                    }
                    for (const item of metadata[arrKey]) {
                        const exists = freshMetadata[arrKey].some((u: any) => u && u.UnitId === item.UnitId);
                        if (!exists) {
                            freshMetadata[arrKey].push(item);
                        }
                    }
                }
            }

            await this.updateTextDocument(document, JSON.stringify(freshMetadata, null, 2));
        } catch (err: any) {
            vscode.window.showErrorMessage(`Failed to import asset: ${err.message}`);
        }
    }

    private async handleMigrateAsset(
        fromCategory: string,
        fromSubCategory: string | undefined,
        key: string,
        toCategory: string,
        toSubCategory: string | undefined,
        document: vscode.TextDocument
    ) {
        try {
            const targetDir = this.lastOpenedDirectory || path.dirname(document.uri.fsPath);
            
            // Helper to get directory path for a category & subcategory
            const getRelPath = (cat: string, subCat?: string) => {
                if (cat === 'glb') return path.join('Assets', 'models', (subCat || 'props').toLowerCase());
                if (cat === 'decals') return path.join('Assets', 'decals');
                if (cat === 'icons') return path.join('Assets', 'icons');
                if (cat === 'skyboxes') return path.join('Assets', 'skyboxes');
                if (cat === 'sfx') return path.join('Assets', 'audio', 'sfx');
                if (cat === 'music') return path.join('Assets', 'audio', 'music');
                if (cat === 'vfx_spritesheets') return path.join('Assets', 'vfx');
                if (cat === 'textures') return path.join('Assets', 'textures');
                return path.join('Assets', cat);
            };

            const srcRel = getRelPath(fromCategory, fromSubCategory);
            const dstRel = getRelPath(toCategory, toSubCategory);
            const srcAbs = path.join(targetDir, srcRel, key);
            const dstAbs = path.join(targetDir, dstRel, key);

            // Move file on disk if it exists
            if (fs.existsSync(srcAbs)) {
                if (!fs.existsSync(path.dirname(dstAbs))) {
                    fs.mkdirSync(path.dirname(dstAbs), { recursive: true });
                }
                fs.renameSync(srcAbs, dstAbs);
            }

            // Update JSON document
            const text = document.getText();
            let metadata: any = {};
            if (text.trim()) {
                try { metadata = JSON.parse(text); } catch {}
            }
            if (!metadata.Assets) metadata.Assets = {};

            // Remove old reference
            let itemVal: any = null;
            if (fromCategory === 'glb') {
                if (metadata.Assets.glb && fromSubCategory && metadata.Assets.glb[fromSubCategory]) {
                    itemVal = metadata.Assets.glb[fromSubCategory][key];
                    delete metadata.Assets.glb[fromSubCategory][key];
                    if (Object.keys(metadata.Assets.glb[fromSubCategory]).length === 0) {
                        delete metadata.Assets.glb[fromSubCategory];
                    }
                }
            } else {
                if (metadata.Assets[fromCategory]) {
                    itemVal = metadata.Assets[fromCategory][key];
                    delete metadata.Assets[fromCategory][key];
                    if (Object.keys(metadata.Assets[fromCategory]).length === 0) {
                        delete metadata.Assets[fromCategory];
                    }
                }
            }

            // Insert into new reference
            if (toCategory === 'glb') {
                const subCat = (toSubCategory || 'props').toLowerCase();
                if (!metadata.Assets.glb) metadata.Assets.glb = {};
                if (!metadata.Assets.glb[subCat]) metadata.Assets.glb[subCat] = {};
                metadata.Assets.glb[subCat][key] = itemVal || 'hash';
            } else {
                if (!metadata.Assets[toCategory]) metadata.Assets[toCategory] = {};
                metadata.Assets[toCategory][key] = itemVal || 'hash';
            }

            await this.updateTextDocument(document, JSON.stringify(metadata, null, 2));
            vscode.window.showInformationMessage(`Migrated asset '${key}' to ${toCategory}${toSubCategory ? '/' + toSubCategory : ''}`);
        } catch (err: any) {
            vscode.window.showErrorMessage(`Failed to migrate asset: ${err.message}`);
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
        
        let cleanPath = godotPath.trim();
        if (cleanPath.startsWith('res://')) {
            cleanPath = cleanPath.substring(6);
        }

        const candidateSubDirs = [
            '',
            path.join('Assets', 'models', 'units'),
            path.join('Assets', 'models', 'buildings'),
            path.join('Assets', 'models', 'resources'),
            path.join('Assets', 'models', 'props'),
            path.join('Assets', 'decals'),
            path.join('Assets', 'icons'),
            path.join('Assets', 'textures'),
            path.join('Assets', 'skyboxes'),
            path.join('Assets', 'vfx'),
            path.join('Assets', 'audio', 'sfx')
        ];

        const docDir = path.dirname(documentUri.fsPath);
        const searchRoots: string[] = [];

        let currentDir = docDir;
        while (true) {
            searchRoots.push(currentDir);
            const projectFile = path.join(currentDir, 'project.godot');
            if (fs.existsSync(projectFile)) {
                break;
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
                searchRoots.push(folder.uri.fsPath);
                searchRoots.push(path.join(folder.uri.fsPath, 'Realm.Godot'));
            }
        }

        for (const root of searchRoots) {
        for (const subDir of candidateSubDirs) {
                const fullCandidate = subDir ? path.join(root, subDir, cleanPath) : path.join(root, cleanPath);
                if (fs.existsSync(fullCandidate)) {
                    return fullCandidate;
                }
            }
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
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; connect-src http://127.0.0.1:* http://localhost:*; img-src ${webview.cspSource} data: https:; style-src ${webview.cspSource} 'unsafe-inline'; script-src 'nonce-${nonce}';">
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
                <button type="button" class="tab-btn" data-domain="buildings">🏢 Buildings</button>
                <button type="button" class="tab-btn" data-domain="resources">🪵 Resources</button>
                <button type="button" class="tab-btn" data-domain="props">📦 Props</button>
                <button type="button" class="tab-btn" data-domain="weapons">⚔️ Weapons</button>
                <button type="button" class="tab-btn" data-domain="abilities">🪄 Abilities</button>
                <button type="button" class="tab-btn" data-domain="upgrades">🛡️ Upgrades</button>
                <button type="button" class="tab-btn" data-domain="items">📦 Items</button>
                <button type="button" class="tab-btn" data-domain="assets">🎨 Assets</button>
                <button type="button" class="tab-btn" data-domain="properties">⚙️ Settings</button>
            </div>
            <div class="header-right-actions">
                <div id="save-status" class="save-status saved" title="Auto-saved to file">● Saved</div>
                <button type="button" id="toggle-lock-btn" class="btn secondary-btn small-btn" title="Lock Editor (Read-Only Mode)">🔓 Lock</button>
                <button type="button" id="toggle-buttons-btn" class="btn secondary-btn small-btn" title="Toggle Add/Delete Controls">➕ Edit Ops</button>
                <button type="button" id="toggle-debug-btn" class="btn secondary-btn small-btn" title="Toggle Debug JSON View">🐞 Debug</button>
                <span style="font-size: 11px; color: var(--text-muted); opacity: 0.8; margin-left: 4px;" title="Press F12 inside editor to open Chromium DevTools console for debugging">💡 F12 DevTools</span>
            </div>
        </div>
        <div class="editor-body">
            <div class="sidebar">
                <div class="sidebar-subheader" style="padding-top: 16px;">
                    <h2>Units List</h2>
                    <div class="add-buttons-group" style="display: flex; gap: 4px;">
                        <button id="add-unit-btn" class="btn primary-btn" style="padding: 4px 8px;" title="Add Unit">+ Add</button>
                        <button type="button" id="prune-entities-btn" class="btn secondary-btn" style="padding: 4px 8px;" title="Prune items never placed on terrain.json">✂️ Prune Unused</button>
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
                            <h2 id="editor-title">Edit Entity</h2>
                            <span id="editor-subtitle" class="subtitle">ID</span>
                        </div>
                        <div class="header-actions" style="display: flex; gap: 6px;">
                            <button type="button" id="copy-unit-btn" class="btn secondary-btn" title="Copy entity to clipboard">✂️ Copy</button>
                            <button type="button" id="paste-unit-btn" class="btn secondary-btn" title="Paste entity from clipboard">📋 Paste</button>
                            <button type="button" id="duplicate-unit-btn" class="btn secondary-btn">📋 Duplicate</button>
                            <button type="button" id="delete-unit-btn" class="btn secondary-btn" title="Delete entity">🗑️ Delete</button>
                        </div>
                    </div>
                </div>
                <div class="form-scroll-container">
                    <div class="form-section">
                        <h3>General Information</h3>
                        <div class="form-group">
                            <label for="field-UnitId">ID</label>
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
                            <label for="field-ModelPath">Model Asset (GLB)</label>
                            <div class="input-with-browse" style="display: flex; flex-direction: column; gap: 4px;">
                                <div style="display: flex; gap: 6px; width: 100%;">
                                    <select id="field-ModelPath" style="flex: 1; min-height: 30px;"></select>
                                    <button type="button" class="btn clear-btn" data-input-id="field-ModelPath" title="Clear path">❌</button>
                                </div>
                                <label style="font-size: 11px; color: var(--text-muted); cursor: pointer; display: flex; align-items: center; gap: 4px; margin-top: 2px;">
                                    <input type="checkbox" id="chk-show-all-glb" style="width: auto; margin: 0;" /> Show all GLB assets
                                </label>
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
                        <h3>Global Object Overrides</h3>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-YOffset">Y-Offset</label>
                                <input type="number" id="field-YOffset" step="0.05" placeholder="0.0" />
                            </div>
                            <div class="form-group">
                                <label for="field-CollisionCircle">Collision Circle</label>
                                <input type="number" id="field-CollisionCircle" step="0.05" min="0.1" placeholder="1.0" />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-Brightness">Brightness</label>
                                <input type="number" id="field-Brightness" step="0.02" min="0.10" max="1.75" placeholder="1.0" />
                            </div>
                            <div class="form-group">
                                <label for="field-Tint">Tint Color</label>
                                <input type="text" id="field-Tint" placeholder="#ffffff" />
                            </div>
                        </div>
                        <div class="form-group checkbox-group" style="margin-top: 6px;">
                            <input type="checkbox" id="field-RecalculateNormals" />
                            <label for="field-RecalculateNormals">Re-Calculate Normals</label>
                        </div>
                        <div class="form-group checkbox-group" style="margin-top: 6px;">
                            <input type="checkbox" id="field-IgnorePlayerColor" />
                            <label for="field-IgnorePlayerColor">Ignore Player Color</label>
                        </div>
                    </div>

                    <div id="section-resource-node-config" class="form-section">
                        <h3>Resource Deposit Settings</h3>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-MaxCapacity">Max Resource Capacity</label>
                                <input type="number" id="field-MaxCapacity" min="0" step="any" placeholder="2000" />
                            </div>
                            <div class="form-group">
                                <label for="field-HarvestRate">Harvest Yield / Cycle</label>
                                <input type="number" id="field-HarvestRate" min="0" step="any" placeholder="10" />
                            </div>
                        </div>
                        <div class="form-row">
                            <div class="form-group">
                                <label for="field-GrowthRate">Regen / Growth Rate (Units/sec)</label>
                                <input type="number" id="field-GrowthRate" min="0" step="any" placeholder="0.0" />
                            </div>
                            <div class="form-group">
                                <label for="field-MaxWorkers">Max Simultaneous Harvesters</label>
                                <input type="number" id="field-MaxWorkers" min="1" step="1" placeholder="5" />
                            </div>
                        </div>
                    </div>

                    <div id="section-unit-stats" class="form-section">
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

                    <div id="section-unit-costs" class="form-section">
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

                    <div id="section-unit-combat" class="form-section">
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

                    <div id="section-unit-capabilities" class="form-section">
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

                    <div id="section-unit-animations" class="form-section">
                        <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 4px;">
                            <h3 style="margin-bottom: 0;">Unit Animations (Optional)</h3>
                            <div style="display: flex; gap: 4px;">
                                <button type="button" class="btn small-btn copy-unit-comp-btn" data-key="Animations" title="Copy Animations block">📋 Copy</button>
                                <button type="button" class="btn small-btn paste-unit-comp-btn" data-key="Animations" title="Paste Animations block">📥 Paste</button>
                            </div>
                        </div>
                        <p class="desc" style="margin-bottom: 12px; color: var(--text-muted);">Configure animation variations for each action type (Idle, Walk, Attack, Death, Labor, Spell_Cast, Dance). In-game actions randomly pick from configured animations.</p>
                        <div id="unit-animations-container" class="list-editor-container"></div>
                    </div>

                    <div id="section-pathing-flags" class="form-section">
                        <h3>Placement & Pathing Flags</h3>
                        <div class="form-group">
                            <div id="field-PathingType-flags" style="display: flex; flex-wrap: wrap; gap: 10px; margin-top: 4px;">
                                <label style="display: flex; align-items: center; gap: 4px; font-weight: normal;"><input type="checkbox" class="pathing-flag-cb" value="1" /> Shallow Water (1)</label>
                                <label style="display: flex; align-items: center; gap: 4px; font-weight: normal;"><input type="checkbox" class="pathing-flag-cb" value="2" /> Deep Water (2)</label>
                                <label style="display: flex; align-items: center; gap: 4px; font-weight: normal;"><input type="checkbox" class="pathing-flag-cb" value="4" /> Flying (4)</label>
                                <label style="display: flex; align-items: center; gap: 4px; font-weight: normal;"><input type="checkbox" class="pathing-flag-cb" value="8" /> Ground (8)</label>
                                <label style="display: flex; align-items: center; gap: 4px; font-weight: normal;"><input type="checkbox" class="pathing-flag-cb" value="32" /> Buildable (32)</label>
                            </div>
                            <input type="hidden" id="field-PathingType" value="8" />
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
                                <label for="prop-ShroudType">Shroud Style</label>
                                <select id="prop-ShroudType">
                                    <option value="visible">Always Visible</option>
                                    <option value="VisionShroud">VisionShroud (Grey Mask)</option>
                                    <option value="ExplorationShroud">ExplorationShroud (Black Unexplored)</option>
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
                                <button type="button" id="paste-custom-weapon-btn" class="btn secondary-btn" title="Paste Weapon from Clipboard">📋 Paste Weapon</button>
                                <button type="button" id="prune-weapons-btn" class="btn secondary-btn" title="Prune weapons never used by placed units on terrain.json">✂️ Prune Unused</button>
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
                                <button type="button" id="paste-custom-ability-btn" class="btn secondary-btn" title="Paste Ability from Clipboard">📋 Paste Ability</button>
                                <button type="button" id="prune-abilities-btn" class="btn secondary-btn" title="Prune abilities never used by placed units on terrain.json">✂️ Prune Unused</button>
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
                                <button type="button" id="paste-custom-upgrade-btn" class="btn secondary-btn" title="Paste Upgrade from Clipboard">📋 Paste Upgrade</button>
                                <button type="button" id="prune-upgrades-btn" class="btn secondary-btn" title="Prune upgrades never used by placed units on terrain.json">✂️ Prune Unused</button>
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
                                <button type="button" id="paste-custom-item-btn" class="btn secondary-btn" title="Paste Item from Clipboard">📋 Paste Item</button>
                                <button type="button" id="prune-items-btn" class="btn secondary-btn" title="Prune items never used by placed units on terrain.json">✂️ Prune Unused</button>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
            
            <div id="custom-assets-form" class="editor-form hidden">
                <div class="form-header">
                    <div style="display: flex; justify-content: space-between; align-items: center; width: 100%;">
                        <div>
                            <div class="breadcrumb">Map > Assets Manager</div>
                            <h2>Assets Manager</h2>
                            <span class="subtitle">Import and manage textures, 3D models, decals, VFX, and audio</span>
                        </div>
                        <div>
                            <button type="button" id="btn-prune-unused-assets" class="btn secondary-btn" title="Prune unused assets unreferenced by metadata.json & delete files from workspace">✂️ Prune Unused</button>
                        </div>
                    </div>
                </div>
                <div class="form-scroll-container">
                    <div class="form-section">
                        <h3>🎨 Import Terrain Texture</h3>
                        <p class="desc" style="margin-bottom: 12px; color: var(--text-muted);">Import a custom terrain texture image. It will append as a new paint swatch and be converted into PBR KTX2 format with normal & AO maps.</p>
                        <div class="form-row">
                            <div class="form-group">
                                <button type="button" id="btn-import-texture" class="btn primary-btn">📥 Import Custom Texture</button>
                            </div>
                        </div>
                    </div>

                    <div class="form-section">
                        <h3>📦 Import 3D Model (GLB)</h3>
                        <p class="desc" style="margin-bottom: 12px; color: var(--text-muted);">Import binary GLB 3D models. Subcategory will categorize BLAKE3 hash in metadata.json under Units, Buildings, Resources, or Props.</p>
                        <div class="form-row" style="align-items: flex-end; gap: 16px;">
                            <div class="form-group">
                                <label for="glb-category-select">Default Category</label>
                                <select id="glb-category-select">
                                    <option value="units">Units</option>
                                    <option value="buildings">Buildings</option>
                                    <option value="resources">Resources</option>
                                    <option value="props">Props</option>
                                </select>
                            </div>
                            <div class="form-group checkbox-group" style="margin-bottom: 8px;">
                                <input type="checkbox" id="glb-ignore-player-color" />
                                <label for="glb-ignore-player-color" title="Skip player color shader and keep original textures intact">Ignore Player Color</label>
                            </div>
                            <div class="form-group" style="display: flex; align-items: flex-end;">
                                <button type="button" id="btn-import-glb" class="btn secondary-btn">📥 Import 3D Model</button>
                            </div>
                        </div>
                    </div>

                    <div class="form-section">
                        <h3>🌌 Import Skybox Panoramic Image</h3>
                        <p class="desc" style="margin-bottom: 12px; color: var(--text-muted);">Import a 360-degree panoramic HDRI / skybox image (PNG, JPG, EXR, HDR, etc.). Image will convert to PNG format for Godot world environment rendering.</p>
                        <div class="form-row">
                            <button type="button" id="btn-import-skybox" class="btn secondary-btn">📥 Import Skybox</button>
                        </div>
                    </div>

                    <div class="form-section">
                        <h3>🖼️ Import Decal & 2D Icon</h3>
                        <p class="desc" style="margin-bottom: 12px; color: var(--text-muted);">Import decal and UI icon images (PNG, JPG, BMP, etc.). Image will automatically convert to lossless PNG format.</p>
                        <div class="form-row" style="gap: 16px;">
                            <button type="button" id="btn-import-decal" class="btn secondary-btn">📥 Import Decal</button>
                            <button type="button" id="btn-import-icon" class="btn secondary-btn">📥 Import Icon</button>
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
                                <button type="button" id="btn-import-vfx" class="btn secondary-btn">📥 Import VFX Spritesheet</button>
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
                                <button type="button" id="btn-import-audio" class="btn secondary-btn">📥 Import Audio File</button>
                            </div>
                        </div>
                    </div>

                    <div class="form-section">
                        <h3>🏃 Import Animation (.ranim / .glb / .fbx)</h3>
                        <p class="desc" style="margin-bottom: 12px; color: var(--text-muted);">Import binary animation files (.ranim) or Mixamo animations. Animations can be assigned to Unit actions.</p>
                        <div class="form-row">
                            <button type="button" id="btn-import-animation" class="btn secondary-btn">📥 Import Animation File</button>
                        </div>
                    </div>

                    <div class="form-section">
                        <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 8px;">
                            <h3 style="margin: 0;">📂 Current Map Assets</h3>
                            <div style="display: flex; align-items: center; gap: 8px;">
                                <button type="button" id="btn-prune-unused-assets-section" class="btn secondary-btn small-btn" title="Prune unused assets unreferenced by metadata.json & delete files from workspace">✂️ Prune Unused</button>
                                <label for="asset-type-filter-select" style="font-size: 12px; font-weight: 600; color: var(--text-muted, #858585);">Filter Type:</label>
                                <select id="asset-type-filter-select" style="background: var(--vscode-input-background, #252526); color: var(--vscode-input-foreground, #cccccc); border: 1px solid var(--vscode-input-border, #3c3c3c); border-radius: 4px; padding: 2px 8px; font-size: 12px; cursor: pointer;">
                                    <option value="all">All</option>
                                </select>
                            </div>
                        </div>
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
