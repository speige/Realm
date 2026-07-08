using Godot;
using Realm.Godot.ReplaySystem;
using System;
using System.Collections.Generic;
using System.IO;

public partial class ReplayListPanel : Control
{
	private VBoxContainer _listContainer;
	private Button _backBtn;
	private Label _titleLabel;
	private Label _noReplaysLabel;

	public override void _Ready()
	{
		_listContainer = GetNode<VBoxContainer>("CenterContainer/MainFrame/VBox/ListFrame/ListContainer");
		_backBtn = GetNode<Button>("CenterContainer/MainFrame/VBox/Header/BackButton");
		_titleLabel = GetNode<Label>("CenterContainer/MainFrame/VBox/Header/TitleLabel");
		_noReplaysLabel = GetNode<Label>("CenterContainer/MainFrame/VBox/NoReplaysLabel");

		_backBtn.Pressed += OnBackPressed;
		_backBtn.MouseEntered += () => UIManager.Instance?.PlayHoverSound();

		UIStyle.ApplyTitle(_titleLabel, "REPLAYS", 28);
		UIStyle.ApplyButtonText(_backBtn, "BACK", 14);
		_backBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		_backBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		_backBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		_backBtn.AddThemeConstantOverride("icon_max_width", 0);

		GetNode<Panel>("Background").AddThemeStyleboxOverride("panel", UIStyle.CreateBgGradient());
		GetNode<PanelContainer>("CenterContainer/MainFrame").AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel());
		GetNode<PanelContainer>("CenterContainer/MainFrame/VBox/ListFrame").AddThemeStyleboxOverride("panel", new StyleBoxEmpty());

		PopulateReplaysList();
	}

	private void OnBackPressed()
	{
		UIManager.Instance?.PlayClickSound();
		UIManager.Instance?.TransitionTo(GameScreen.MainMenu);
	}

	private void PopulateReplaysList()
	{
		foreach (Node child in _listContainer.GetChildren())
		{
			child.QueueFree();
		}

		string replayDir = ProjectSettings.GlobalizePath("user://replays");
		if (!Directory.Exists(replayDir))
		{
			Directory.CreateDirectory(replayDir);
		}

		var files = Directory.GetFiles(replayDir, "*.rep");
		if (files.Length == 0)
		{
			_noReplaysLabel.Visible = true;
			return;
		}

		_noReplaysLabel.Visible = false;

		var sortedFiles = new List<string>(files);
		sortedFiles.Sort((a, b) => File.GetLastWriteTime(b).CompareTo(File.GetLastWriteTime(a)));

		int addedCount = 0;
		foreach (var filePath in sortedFiles)
		{
			try
			{
				int totalTicks;
				var header = ReplayPlaybackManager.ReadReplayHeader(filePath, out totalTicks);
				if (header == null) continue;

				var row = CreateReplayRow(filePath, header, totalTicks);
				_listContainer.AddChild(row);
				addedCount++;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[ReplayListPanel] Error processing replay file '{filePath}': {ex}");
			}
		}

		if (addedCount == 0)
		{
			_noReplaysLabel.Visible = true;
		}
	}

	private PanelContainer CreateReplayRow(string path, ReplayHeader header, int totalTicks)
	{
		var panel = new PanelContainer();
		panel.CustomMinimumSize = new Vector2(0, 70);
		panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;

		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.15f, 0.15f, 0.15f, 0.6f);
		style.SetBorderWidthAll(1);
		style.BorderColor = UIStyle.ColorBronze;
		style.CornerRadiusTopLeft = 4;
		style.CornerRadiusTopRight = 4;
		style.CornerRadiusBottomLeft = 4;
		style.CornerRadiusBottomRight = 4;
		panel.AddThemeStyleboxOverride("panel", style);

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 15);
		margin.AddThemeConstantOverride("margin_right", 15);
		margin.AddThemeConstantOverride("margin_top", 10);
		margin.AddThemeConstantOverride("margin_bottom", 10);
		panel.AddChild(margin);

		var hBox = new HBoxContainer();
		hBox.AddThemeConstantOverride("separation", 20);
		margin.AddChild(hBox);

		var infoVBox = new VBoxContainer();
		infoVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		hBox.AddChild(infoVBox);

		var mapLabel = new Label();
		string mapName = (header.MapName ?? "UNKNOWN").ToUpper();
		mapLabel.Text = mapName;
		UIStyle.ApplyTitle(mapLabel, mapName, 16);
		infoVBox.AddChild(mapLabel);

		var metaLabel = new Label();
		int totalSec = totalTicks / 30;
		string durationStr = string.Format("{0:D2}:{1:D2}", totalSec / 60, totalSec % 60);
		var dt = DateTimeOffset.FromUnixTimeSeconds(header.Timestamp).LocalDateTime;
		string playersStr = "";
		if (header.Players != null)
		{
			var names = new List<string>();
			foreach (var p in header.Players) names.Add(p.Name);
			playersStr = string.Join(" vs ", names);
		}
		metaLabel.Text = $"{dt:yyyy-MM-dd HH:mm}  |  {durationStr}  |  {playersStr}";
		metaLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
		metaLabel.AddThemeFontSizeOverride("font_size", 12);
		infoVBox.AddChild(metaLabel);

		var playBtn = new Button();
		playBtn.CustomMinimumSize = new Vector2(100, 40);
		playBtn.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		UIStyle.ApplyButtonText(playBtn, "WATCH", 13);
		playBtn.AddThemeStyleboxOverride("normal", UIStyle.CreateButtonNormal());
		playBtn.AddThemeStyleboxOverride("hover", UIStyle.CreateButtonHover());
		playBtn.AddThemeStyleboxOverride("pressed", UIStyle.CreateButtonPressed());
		playBtn.AddThemeConstantOverride("icon_max_width", 0);
		
		playBtn.Pressed += () => OnPlayReplayPressed(path);
		playBtn.MouseEntered += () => UIManager.Instance?.PlayHoverSound();
		hBox.AddChild(playBtn);

		return panel;
	}

	private void OnPlayReplayPressed(string path)
	{
		UIManager.Instance?.PlayClickSound();
		bool ok = ReplayPlaybackManager.Instance.LoadReplay(path);
		if (ok)
		{
			if (LobbyManager.Instance != null)
			{
				if (ReplayPlaybackManager.Instance.Header != null)
				{
					LobbyManager.Instance.ActiveMapName = ReplayPlaybackManager.Instance.Header.MapName ?? "melee";
				}
				LobbyManager.Instance.IsGameStarted = true;
			}
			GetTree().ChangeSceneToFile("res://Main.tscn");
		}
		else
		{
			GD.PrintErr($"Failed to load replay: {path}");
		}
	}
}
