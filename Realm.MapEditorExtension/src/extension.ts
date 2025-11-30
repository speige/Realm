import * as vscode from 'vscode';
import { RealmMapEditorProvider } from './editorProvider';

export function activate(context: vscode.ExtensionContext) {
    context.subscriptions.push(RealmMapEditorProvider.register(context));
}

export function deactivate() {}
