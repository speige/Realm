import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import * as http from 'http';
import { RealmMapEditorProvider } from './editorProvider';

export function activate(context: vscode.ExtensionContext) {
    context.subscriptions.push(RealmMapEditorProvider.register(context));
    openStartupFiles(context);
    startGodotIpcListener(context);
}

function startGodotIpcListener(context: vscode.ExtensionContext): void {
    const ports = [8092, 8093];
    const pollInterval = setInterval(async () => {
        for (const port of ports) {
            try {
                const data = await httpGetJson(`http://127.0.0.1:${port}/api/poll`);
                if (data && Array.isArray(data.commands)) {
                    for (const cmd of data.commands) {
                        if (cmd === 'saveAll') {
                            await handleSaveAllCommand();
                        }
                    }
                }
            } catch {
            }
        }
    }, 300);

    context.subscriptions.push({
        dispose: () => clearInterval(pollInterval)
    });
}

async function handleSaveAllCommand(): Promise<void> {
    try {
        for (const doc of vscode.workspace.textDocuments) {
            if (doc.isDirty) {
                await doc.save();
            }
        }
        await vscode.commands.executeCommand('workbench.action.files.saveAll');
    } catch (err) {
        console.error('[RealmExtension] Error executing saveAll:', err);
    }
}

function httpGetJson(urlStr: string): Promise<any> {
    return new Promise((resolve, reject) => {
        const req = http.get(urlStr, (res) => {
            if (res.statusCode !== 200) {
                return reject(new Error(`Status ${res.statusCode}`));
            }
            let body = '';
            res.on('data', chunk => body += chunk);
            res.on('end', () => {
                try {
                    resolve(JSON.parse(body));
                } catch (e) {
                    reject(e);
                }
            });
        });
        req.on('error', reject);
        req.setTimeout(800, () => {
            req.destroy();
            reject(new Error('Timeout'));
        });
    });
}

async function openStartupFiles(context: vscode.ExtensionContext): Promise<void> {
    await delay(2000);

    const folders = vscode.workspace.workspaceFolders;
    if (!folders || folders.length === 0) { return; }

    const workspaceDir = folders[0].uri.fsPath;
    const scriptPath = path.join(workspaceDir, 'MapScript.cs');
    const metadataPath = path.join(workspaceDir, 'metadata.json');

    if (!fs.existsSync(scriptPath) || !fs.existsSync(metadataPath)) { return; }

    try {
        // Open metadata.json in background — custom editor handles it via priority: "default"
        const metadataUri = vscode.Uri.file(metadataPath);
        await vscode.commands.executeCommand('vscode.open', metadataUri, { preview: false, preserveFocus: true });

        // Open MapScript.cs as the active text editor
        const scriptUri = vscode.Uri.file(scriptPath);
        const scriptDoc = await vscode.workspace.openTextDocument(scriptUri);
        await vscode.window.showTextDocument(scriptDoc, { preview: false, preserveFocus: false });
        // Force revert to discard any stale in-memory content and read fresh from disk
        await vscode.commands.executeCommand('workbench.action.revertFile');
    } catch (err) {
        // Ignore errors — VS Code may not be fully ready, but we only try once
    }
}

function delay(ms: number): Promise<void> {
    return new Promise(resolve => setTimeout(resolve, ms));
}

export function deactivate() {}
