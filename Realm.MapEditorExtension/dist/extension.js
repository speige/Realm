"use strict";
var __create = Object.create;
var __defProp = Object.defineProperty;
var __getOwnPropDesc = Object.getOwnPropertyDescriptor;
var __getOwnPropNames = Object.getOwnPropertyNames;
var __getProtoOf = Object.getPrototypeOf;
var __hasOwnProp = Object.prototype.hasOwnProperty;
var __export = (target, all) => {
  for (var name in all)
    __defProp(target, name, { get: all[name], enumerable: true });
};
var __copyProps = (to, from, except, desc) => {
  if (from && typeof from === "object" || typeof from === "function") {
    for (let key of __getOwnPropNames(from))
      if (!__hasOwnProp.call(to, key) && key !== except)
        __defProp(to, key, { get: () => from[key], enumerable: !(desc = __getOwnPropDesc(from, key)) || desc.enumerable });
  }
  return to;
};
var __toESM = (mod, isNodeMode, target) => (target = mod != null ? __create(__getProtoOf(mod)) : {}, __copyProps(
  // If the importer is in node compatibility mode or this is not an ESM
  // file that has been converted to a CommonJS file using a Babel-
  // compatible transform (i.e. "__esModule" has not been set), then set
  // "default" to the CommonJS "module.exports" for node compatibility.
  isNodeMode || !mod || !mod.__esModule ? __defProp(target, "default", { value: mod, enumerable: true }) : target,
  mod
));
var __toCommonJS = (mod) => __copyProps(__defProp({}, "__esModule", { value: true }), mod);

// src/extension.ts
var extension_exports = {};
__export(extension_exports, {
  activate: () => activate,
  deactivate: () => deactivate
});
module.exports = __toCommonJS(extension_exports);

// src/editorProvider.ts
var vscode = __toESM(require("vscode"));
var path = __toESM(require("path"));
var fs = __toESM(require("fs"));
var RealmMapEditorProvider = class _RealmMapEditorProvider {
  constructor(context) {
    this.context = context;
  }
  static register(context) {
    const provider = new _RealmMapEditorProvider(context);
    return vscode.window.registerCustomEditorProvider(_RealmMapEditorProvider.viewType, provider);
  }
  static {
    this.viewType = "realm.mapEditor";
  }
  async resolveCustomTextEditor(document, webviewPanel, _token) {
    webviewPanel.webview.options = {
      enableScripts: true
    };
    webviewPanel.webview.html = this.getHtmlForWebview(webviewPanel.webview);
    const updateWebview = () => {
      webviewPanel.webview.postMessage({
        type: "update",
        text: document.getText()
      });
    };
    const changeDocumentSubscription = vscode.workspace.onDidChangeTextDocument((e) => {
      if (e.document.uri.toString() === document.uri.toString()) {
        updateWebview();
      }
    });
    webviewPanel.onDidDispose(() => {
      changeDocumentSubscription.dispose();
    });
    webviewPanel.webview.onDidReceiveMessage(async (e) => {
      switch (e.type) {
        case "ready":
          updateWebview();
          break;
        case "change":
          this.updateTextDocument(document, e.text);
          break;
        case "browseFile":
          await this.handleBrowseFile(webviewPanel.webview, e.fieldId, e.fieldClass, e.fieldIndex, e.fileTypes, document.uri);
          break;
        case "openFile":
          const absFile = this.resolveGodotPath(e.path, document.uri);
          if (absFile && fs.existsSync(absFile)) {
            vscode.commands.executeCommand("vscode.open", vscode.Uri.file(absFile));
          }
          break;
        case "resolvePath":
          const absPath = this.resolveGodotPath(e.path, document.uri);
          const webviewUri = absPath ? webviewPanel.webview.asWebviewUri(vscode.Uri.file(absPath)).toString() : "";
          webviewPanel.webview.postMessage({
            type: "resolvePathResult",
            requestId: e.requestId,
            uri: webviewUri
          });
          break;
      }
    });
  }
  async handleBrowseFile(webview, fieldId, fieldClass, fieldIndex, fileTypes, documentUri) {
    let defaultUri = void 0;
    if (documentUri) {
      const docDir = path.dirname(documentUri.fsPath);
      let currentDir = docDir;
      while (true) {
        const projectFile = path.join(currentDir, "project.godot");
        if (fs.existsSync(projectFile)) {
          defaultUri = vscode.Uri.file(currentDir);
          break;
        }
        const parent = path.dirname(currentDir);
        if (parent === currentDir) {
          break;
        }
        currentDir = parent;
      }
    }
    const filters = {};
    if (fileTypes && fileTypes.length > 0) {
      filters["Supported Files"] = fileTypes;
    }
    const options = {
      canSelectMany: false,
      openLabel: "Select Asset",
      defaultUri,
      filters: Object.keys(filters).length > 0 ? filters : void 0
    };
    const fileUri = await vscode.window.showOpenDialog(options);
    if (fileUri && fileUri[0]) {
      const selectedPath = fileUri[0].fsPath;
      const relativePath = this.getGodotRelativePath(selectedPath);
      webview.postMessage({
        type: "browseFileResult",
        fieldId,
        fieldClass,
        fieldIndex,
        path: relativePath
      });
    }
  }
  getGodotRelativePath(absolutePath) {
    let currentDir = path.dirname(absolutePath);
    while (true) {
      const projectFile = path.join(currentDir, "project.godot");
      if (fs.existsSync(projectFile)) {
        let rel = path.relative(currentDir, absolutePath);
        return "res://" + rel.replace(/\\/g, "/");
      }
      const parent = path.dirname(currentDir);
      if (parent === currentDir) {
        break;
      }
      currentDir = parent;
    }
    const godotFolderName = "Realm.Godot";
    const parts = absolutePath.split(path.sep);
    const idx = parts.findIndex((p) => p.toLowerCase() === godotFolderName.toLowerCase());
    if (idx !== -1) {
      const relativeParts = parts.slice(idx + 1);
      return "res://" + relativeParts.join("/");
    }
    const workspaceFolders = vscode.workspace.workspaceFolders;
    if (workspaceFolders) {
      for (const folder of workspaceFolders) {
        if (absolutePath.startsWith(folder.uri.fsPath)) {
          let rel = path.relative(folder.uri.fsPath, absolutePath);
          return rel.replace(/\\/g, "/");
        }
      }
    }
    return absolutePath.replace(/\\/g, "/");
  }
  resolveGodotPath(godotPath, documentUri) {
    if (!godotPath) {
      return null;
    }
    let cleanPath = godotPath;
    if (godotPath.startsWith("res://")) {
      cleanPath = godotPath.substring(6);
    }
    const docDir = path.dirname(documentUri.fsPath);
    let currentDir = docDir;
    while (true) {
      const projectFile = path.join(currentDir, "project.godot");
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
        const godotPathOption2 = path.join(folder.uri.fsPath, "Realm.Godot", cleanPath);
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
  updateTextDocument(document, text) {
    const edit = new vscode.WorkspaceEdit();
    edit.replace(
      document.uri,
      new vscode.Range(0, 0, document.lineCount, 0),
      text
    );
    return vscode.workspace.applyEdit(edit);
  }
  getHtmlForWebview(webview) {
    const scriptUri = webview.asWebviewUri(vscode.Uri.file(
      path.join(this.context.extensionPath, "media", "editor.js")
    ));
    const styleUri = webview.asWebviewUri(vscode.Uri.file(
      path.join(this.context.extensionPath, "media", "editor.css")
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
                <button type="button" class="tab-btn active" data-domain="units">\u{1F465} Units</button>
                <button type="button" class="tab-btn" data-domain="weapons">\u2694\uFE0F Weapons</button>
                <button type="button" class="tab-btn" data-domain="abilities">\u{1FA84} Abilities</button>
                <button type="button" class="tab-btn" data-domain="upgrades">\u{1F6E1}\uFE0F Upgrades</button>
                <button type="button" class="tab-btn" data-domain="items">\u{1F4E6} Items</button>
                <button type="button" class="tab-btn" data-domain="properties">\u2699\uFE0F Map Props</button>
            </div>
            <div class="header-right-actions">
                <div id="save-status" class="save-status saved" title="Auto-saved to file">\u25CF Saved</div>
                <button type="button" id="toggle-lock-btn" class="btn secondary-btn small-btn" title="Lock Editor (Read-Only Mode)">\u{1F513} Lock</button>
                <button type="button" id="toggle-buttons-btn" class="btn secondary-btn small-btn" title="Toggle Add/Delete Controls">\u2795 Edit Ops</button>
                <button type="button" id="toggle-debug-btn" class="btn secondary-btn small-btn" title="Toggle Debug JSON View">\u{1F41E} Debug</button>
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
                            <button type="button" id="copy-unit-btn" class="btn secondary-btn" title="Copy unit to clipboard">\u2702\uFE0F Copy Unit</button>
                            <button type="button" id="paste-unit-btn" class="btn secondary-btn" title="Paste unit from clipboard">\u{1F4CB} Paste Unit</button>
                            <button type="button" id="duplicate-unit-btn" class="btn secondary-btn">\u{1F4CB} Duplicate Unit</button>
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
                                <button type="button" class="btn browse-btn" data-input-id="field-ModelPath" data-file-types="gltf,glb,scn,tscn" title="Browse files">\u{1F4C1}</button>
                                <button type="button" class="btn clear-btn" data-input-id="field-ModelPath" title="Clear path">\u274C</button>
                            </div>
                        </div>
                        <div class="form-group">
                            <label for="field-PortraitModelPath">Portrait Model Path (Optional)</label>
                            <div class="input-with-browse">
                                <input type="text" id="field-PortraitModelPath" />
                                <button type="button" class="btn browse-btn" data-input-id="field-PortraitModelPath" data-file-types="gltf,glb,scn,tscn" title="Browse files">\u{1F4C1}</button>
                                <button type="button" class="btn clear-btn" data-input-id="field-PortraitModelPath" title="Clear path">\u274C</button>
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
                                    <button type="button" class="btn small-btn copy-unit-comp-btn" data-key="BuildOptions" title="Copy Build Options block">\u{1F4CB} Copy</button>
                                    <button type="button" class="btn small-btn paste-unit-comp-btn" data-key="BuildOptions" title="Paste Build Options block">\u{1F4E5} Paste</button>
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
                                    <button type="button" class="btn small-btn copy-unit-comp-btn" data-key="Abilities" title="Copy Abilities block">\u{1F4CB} Copy</button>
                                    <button type="button" class="btn small-btn paste-unit-comp-btn" data-key="Abilities" title="Paste Abilities block">\u{1F4E5} Paste</button>
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
                                    <button type="button" class="btn small-btn copy-unit-comp-btn" data-key="Weapons" title="Copy Weapons block">\u{1F4CB} Copy</button>
                                    <button type="button" class="btn small-btn paste-unit-comp-btn" data-key="Weapons" title="Paste Weapons block">\u{1F4E5} Paste</button>
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
                                    <button type="button" class="btn small-btn copy-unit-comp-btn" data-key="StartingItems" title="Copy Starting Items block">\u{1F4CB} Copy</button>
                                    <button type="button" class="btn small-btn paste-unit-comp-btn" data-key="StartingItems" title="Paste Starting Items block">\u{1F4E5} Paste</button>
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
                                    <button type="button" class="btn small-btn copy-unit-comp-btn" data-key="Upgrades" title="Copy Tech Upgrades block">\u{1F4CB} Copy</button>
                                    <button type="button" class="btn small-btn paste-unit-comp-btn" data-key="Upgrades" title="Paste Tech Upgrades block">\u{1F4E5} Paste</button>
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
                                    <button type="button" class="btn small-btn copy-unit-comp-btn" data-key="StatusEffects" title="Copy Status Effects block">\u{1F4CB} Copy</button>
                                    <button type="button" class="btn small-btn paste-unit-comp-btn" data-key="StatusEffects" title="Paste Status Effects block">\u{1F4E5} Paste</button>
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
                                    <button type="button" class="btn small-btn copy-unit-comp-btn" data-key="SoundEvents" title="Copy Sound Events block">\u{1F4CB} Copy</button>
                                    <button type="button" class="btn small-btn paste-unit-comp-btn" data-key="SoundEvents" title="Paste Sound Events block">\u{1F4E5} Paste</button>
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
                                    <button type="button" class="btn browse-btn" data-input-id="prop-MinimapImage" data-file-types="png,jpg,jpeg,svg,tga,dds" title="Browse files">\u{1F4C1}</button>
                                    <button type="button" class="btn clear-btn" data-input-id="prop-MinimapImage" title="Clear path">\u274C</button>
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
                                    <button type="button" class="btn browse-btn" data-input-id="prop-LoadingImage" data-file-types="png,jpg,jpeg,svg,tga,dds" title="Browse files">\u{1F4C1}</button>
                                    <button type="button" class="btn clear-btn" data-input-id="prop-LoadingImage" title="Clear path">\u274C</button>
                                </div>
                            </div>
                            <div class="form-group">
                                <label for="prop-LoadingMusic">Loading Music</label>
                                <div class="input-with-browse">
                                    <input type="text" id="prop-LoadingMusic" placeholder="res://Assets/...ogg" />
                                    <button type="button" class="btn browse-btn" data-input-id="prop-LoadingMusic" data-file-types="ogg,wav,mp3" title="Browse files">\u{1F4C1}</button>
                                    <button type="button" class="btn clear-btn" data-input-id="prop-LoadingMusic" title="Clear path">\u274C</button>
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
                                <button type="button" id="paste-custom-weapon-btn" class="btn secondary-btn" title="Paste Weapon from Clipboard">\u{1F4CB} Paste Weapon</button>
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
                                <button type="button" id="paste-custom-ability-btn" class="btn secondary-btn" title="Paste Ability from Clipboard">\u{1F4CB} Paste Ability</button>
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
                                <button type="button" id="paste-custom-upgrade-btn" class="btn secondary-btn" title="Paste Upgrade from Clipboard">\u{1F4CB} Paste Upgrade</button>
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
                                <button type="button" id="paste-custom-item-btn" class="btn secondary-btn" title="Paste Item from Clipboard">\u{1F4CB} Paste Item</button>
                            </div>
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
  getNonce() {
    let text = "";
    const possible = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    for (let i = 0; i < 32; i++) {
      text += possible.charAt(Math.floor(Math.random() * possible.length));
    }
    return text;
  }
};

// src/extension.ts
function activate(context) {
  context.subscriptions.push(RealmMapEditorProvider.register(context));
}
function deactivate() {
}
// Annotate the CommonJS export names for ESM import in node:
0 && (module.exports = {
  activate,
  deactivate
});
