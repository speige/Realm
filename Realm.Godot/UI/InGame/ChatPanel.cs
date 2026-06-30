using Godot;
using System;

public class ChatPanel
{
	private PanelContainer _chatPanelNode;
	private LineEdit _chatInput;
	private RichTextLabel _chatLog;
	private bool _isChatActive = false;

	public bool IsChatActive => _isChatActive;

	public ChatPanel(PanelContainer chatPanelNode, LineEdit chatInput, RichTextLabel chatLog)
	{
		_chatPanelNode = chatPanelNode;
		_chatInput = chatInput;
		_chatLog = chatLog;

		_chatInput.TextSubmitted += OnChatInputSubmitted;
		_chatInput.Visible = false;
	}

	public void ShowChatInput()
	{
		_isChatActive = true;
		_chatInput.Visible = true;
		_chatInput.GrabFocus();
	}

	public void HideChatInput()
	{
		_isChatActive = false;
		_chatInput.Visible = false;
		_chatInput.ReleaseFocus();
		_chatInput.Text = "";
	}

	private void OnChatInputSubmitted(string text)
	{
		HideChatInput();
		if (string.IsNullOrWhiteSpace(text)) return;

		string trimmedText = text.Trim();

		if (trimmedText.StartsWith("/"))
		{
			if (InGameHUD.Instance != null)
			{
				InGameHUD.Instance.TryTriggerCheat(trimmedText.Substring(1));
			}
			return;
		}

		string sender = LobbyManager.Instance?.LocalPlayer?.Name ?? "Player";
		if (LobbyManager.Instance != null)
		{
			LobbyManager.Instance.SendChatMessage(sender, trimmedText);
		}
		else
		{
			OnLobbyChatReceived(sender, trimmedText);
		}
	}

	public void OnLobbyChatReceived(string senderName, string message)
	{
		if (_chatLog == null) return;
		string cleanMsg = message.Replace("[", "[[").Replace("]", "]]");
		string textToAppend = $"[color=#a0a0a0][{DateTime.Now:HH:mm:ss}][/color] [color=#00ffc8]{senderName}:[/color] {cleanMsg}\n";
		_chatLog.AppendText(textToAppend);
	}
}
