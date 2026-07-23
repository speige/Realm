import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import { RealmMapEditorProvider } from './editorProvider';

export function activate(context: vscode.ExtensionContext) {
    context.subscriptions.push(RealmMapEditorProvider.register(context));
    openStartupFiles(context);
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
