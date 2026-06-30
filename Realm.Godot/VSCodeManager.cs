using Godot;
using Microsoft.Web.WebView2.Core;
using Realm.MapAPI;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class VSCodeManager
{
	private static VSCodeManager _instance;
	public static VSCodeManager Instance => _instance ??= new VSCodeManager();

	private bool _isInstalling = false;
	private bool _installCompleted = false;
	private System.Threading.Tasks.Task _installTask;
	private readonly object _installLock = new object();

	public bool IsInstalling
	{
		get
		{
			lock (_installLock)
			{
				return _isInstalling;
			}
		}
	}

	public bool IsInstallCompleted
	{
		get
		{
			lock (_installLock)
			{
				return _installCompleted;
			}
		}
	}

	public bool IsInstalled()
	{
		string projectRoot = ProjectSettings.GlobalizePath("res://");
		string embedDir = Path.Combine(projectRoot, "vscode_embedded");
		string binPath = Path.Combine(embedDir, "bin");
		string exePath = Path.Combine(binPath, "code.exe");
		return File.Exists(exePath);
	}

	public void StartInstallIfNeeded()
	{
		lock (_installLock)
		{
			if (_isInstalling || _installCompleted)
			{
				return;
			}

			string projectRoot = ProjectSettings.GlobalizePath("res://");
			string embedDir = Path.Combine(projectRoot, "vscode_embedded");
			string binPath = Path.Combine(embedDir, "bin");
			string exePath = Path.Combine(binPath, "code.exe");

			if (File.Exists(exePath))
			{
				_installCompleted = true;
				return;
			}

			_isInstalling = true;
			_installTask = System.Threading.Tasks.Task.Run(() =>
			{
				try
				{
					string scriptPath = Path.GetFullPath(Path.Combine(projectRoot, "..", "install_vscode.ps1"));
					if (File.Exists(scriptPath))
					{
						using (var installProcess = new Process())
						{
							installProcess.StartInfo.FileName = "powershell.exe";
							installProcess.StartInfo.Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\"";
							installProcess.StartInfo.CreateNoWindow = true;
							installProcess.StartInfo.UseShellExecute = false;
							installProcess.Start();
							installProcess.WaitForExit();
						}
					}
					else
					{
						GD.PrintErr("VS Code installer script not found at: " + scriptPath);
					}
				}
				catch (Exception ex)
				{
					GD.PrintErr("Background VS Code installation failed: " + ex.Message);
				}
				finally
				{
					lock (_installLock)
					{
						_isInstalling = false;
						string innerProjectRoot = ProjectSettings.GlobalizePath("res://");
						string innerEmbedDir = Path.Combine(innerProjectRoot, "vscode_embedded");
						string innerBinPath = Path.Combine(innerEmbedDir, "bin");
						string innerExePath = Path.Combine(innerBinPath, "code.exe");
						_installCompleted = File.Exists(innerExePath);
					}
				}
			});
		}
	}

	private Process _vscodeProcess;
	private Thread _staThread;
	private IntPtr _parentHwnd;
	private IntPtr _childHwnd;
	private Control _containerControl;
	private CoreWebView2Controller _controller;
	private bool _isInitialized;
	private bool _isVisible;

	private readonly ConcurrentQueue<Action> _actionQueue = new ConcurrentQueue<Action>();

	private const uint WM_USER = 0x0400;
	private const uint WM_WAKEUP = WM_USER + 1;
	private const uint WS_CHILD = 0x40000000;
	private const uint WS_VISIBLE = 0x10000000;
	private const uint WS_CLIPSIBLINGS = 0x04000000;
	private const int SW_HIDE = 0;
	private const int SW_SHOW = 5;

	[StructLayout(LayoutKind.Sequential)]
	public struct MSG
	{
		public IntPtr hwnd;
		public uint message;
		public IntPtr wParam;
		public IntPtr lParam;
		public uint time;
		public Point pt;
		public uint lPrivate;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct Point
	{
		public int x;
		public int y;
	}

	[DllImport("user32.dll")]
	public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

	[DllImport("user32.dll")]
	public static extern bool TranslateMessage(ref MSG lpMsg);

	[DllImport("user32.dll")]
	public static extern IntPtr DispatchMessage(ref MSG lpMsg);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern IntPtr CreateWindowEx(
		uint dwExStyle,
		string lpClassName,
		string lpWindowName,
		uint dwStyle,
		int x,
		int y,
		int nWidth,
		int nHeight,
		IntPtr hWndParent,
		IntPtr hMenu,
		IntPtr hInstance,
		IntPtr lpParam);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern bool DestroyWindow(IntPtr hWnd);

	[DllImport("user32.dll")]
	public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

	[DllImport("user32.dll")]
	public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

	[DllImport("user32.dll")]
	public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

	public void Initialize(Control containerControl)
	{
		if (_isInitialized)
		{
			_containerControl = containerControl;
			UpdateBounds();
			return;
		}

		_containerControl = containerControl;
		int windowId = containerControl.GetWindow().GetWindowId();
		long nativeHandle = DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, windowId);
		_parentHwnd = new IntPtr(nativeHandle);

		StartVSCodeServer();

		_staThread = new Thread(STAThreadLoop);
		_staThread.SetApartmentState(ApartmentState.STA);
		_staThread.Start();

		_isInitialized = true;
	}

	private void StartVSCodeServer()
	{
		try
		{
			string projectRoot = ProjectSettings.GlobalizePath("res://");
			string embedDir = Path.Combine(projectRoot, "vscode_embedded");
			string binPath = Path.Combine(embedDir, "bin");
			string exePath = Path.Combine(binPath, "code.exe");
			string serverDataDir = Path.Combine(embedDir, "user-data-dir");
			string extensionsDir = Path.Combine(serverDataDir, "extensions");

			if (!File.Exists(exePath))
			{
				StartInstallIfNeeded();
				_installTask?.Wait();
			}

			_vscodeProcess = new Process();
			_vscodeProcess.StartInfo.FileName = exePath;
			_vscodeProcess.StartInfo.Arguments = $"--extensions-dir \"{extensionsDir}\" serve-web --port 8089 --server-data-dir \"{serverDataDir}\" --accept-server-license-terms --without-connection-token";
			_vscodeProcess.StartInfo.CreateNoWindow = true;
			_vscodeProcess.StartInfo.UseShellExecute = false;
			_vscodeProcess.StartInfo.EnvironmentVariables["VSCODE_EXTENSIONS"] = extensionsDir;
			_vscodeProcess.StartInfo.EnvironmentVariables["VSCODE_EXTENSIONS_DIR"] = extensionsDir;
			_vscodeProcess.Start();
			GD.Print("VS Code server started successfully.");
		}
		catch (Exception ex)
		{
			GD.PrintErr("Failed to start VS Code server: " + ex.Message);
		}
	}

	private void STAThreadLoop()
	{
		_childHwnd = CreateWindowEx(
			0,
			"Static",
			"",
			WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS,
			0,
			0,
			800,
			600,
			_parentHwnd,
			IntPtr.Zero,
			IntPtr.Zero,
			IntPtr.Zero);

		if (_childHwnd == IntPtr.Zero)
		{
			GD.PrintErr("Failed to create child window.");
			return;
		}

		InitializeWebView();

		MSG msg;
		while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
		{
			TranslateMessage(ref msg);
			DispatchMessage(ref msg);

			while (_actionQueue.TryDequeue(out var action))
			{
				try
				{
					action();
				}
				catch (Exception ex)
				{
					GD.PrintErr("Error executing action on STA thread: " + ex.Message);
				}
			}
		}
	}

	private async void InitializeWebView()
	{
		try
		{
			string projectRoot = ProjectSettings.GlobalizePath("res://");
			string embedDir = Path.Combine(projectRoot, "vscode_embedded");
			string serverDataDir = Path.Combine(embedDir, "user-data-dir");
			string cachePath = Path.Combine(serverDataDir, "webview-cache");

			var env = await CoreWebView2Environment.CreateAsync(userDataFolder: cachePath);
			_controller = await env.CreateCoreWebView2ControllerAsync(_childHwnd);
			_controller.Bounds = new System.Drawing.Rectangle(0, 0, 800, 600);
			
			string mapFolderRaw = GetMapFolderToOpen(projectRoot);
			string unitsPathRaw = Path.Combine(mapFolderRaw, "metadata.json");
			
			string mapFolder = FormatWinPathForUrl(mapFolderRaw);
			string unitsPath = FormatWinPathForUrl(unitsPathRaw);
			
			string targetUrl = $"http://127.0.0.1:8089/?folder={Uri.EscapeDataString(mapFolder)}&payload={Uri.EscapeDataString("[[\"openFile\",\"" + unitsPath + "\"]]")}";
			_controller.CoreWebView2.Navigate(targetUrl);
			
			_controller.IsVisible = _isVisible;
			ShowWindow(_childHwnd, _isVisible ? SW_SHOW : SW_HIDE);
		}
		catch (Exception ex)
		{
			GD.PrintErr("Failed to initialize WebView2: " + ex.Message);
		}
	}

	public void SetVisible(bool visible)
	{
		_isVisible = visible;
		_actionQueue.Enqueue(() =>
		{
			if (_controller != null)
			{
				_controller.IsVisible = visible;
			}
			ShowWindow(_childHwnd, visible ? SW_SHOW : SW_HIDE);
		});
		PostMessage(_childHwnd, WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);
	}

	public void UpdateBounds()
	{
		if (_containerControl == null || !GodotObject.IsInstanceValid(_containerControl))
		{
			return;
		}

		var rect = _containerControl.GetGlobalRect();
		int x = (int)rect.Position.X;
		int y = (int)rect.Position.Y;
		int w = (int)rect.Size.X;
		int h = (int)rect.Size.Y;

		_actionQueue.Enqueue(() =>
		{
			SetWindowPos(_childHwnd, IntPtr.Zero, x, y, w, h, 0x0014);
			if (_controller != null)
			{
				_controller.Bounds = new System.Drawing.Rectangle(0, 0, w, h);
			}
		});
		PostMessage(_childHwnd, WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);
	}

	public void OpenFile(string relativePath)
	{
		string projectRoot = ProjectSettings.GlobalizePath("res://");
		string mapFolderRaw = GetMapFolderToOpen(projectRoot);
		string fullPathRaw = Path.Combine(mapFolderRaw, relativePath);

		string mapFolder = FormatWinPathForUrl(mapFolderRaw);
		string fullPath = FormatWinPathForUrl(fullPathRaw);

		_actionQueue.Enqueue(() =>
		{
			if (_controller != null)
			{
				string targetUrl = $"http://127.0.0.1:8089/?folder={Uri.EscapeDataString(mapFolder)}&payload={Uri.EscapeDataString("[[\"openFile\",\"" + fullPath + "\"]]")}";
				_controller.CoreWebView2.Navigate(targetUrl);
			}
		});
		PostMessage(_childHwnd, WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);
	}

	private string FormatWinPathForUrl(string path)
	{
		string formatted = path.Replace("\\", "/");
		if (!formatted.StartsWith("/"))
		{
			formatted = "/" + formatted;
		}
		return formatted;
	}

	public void SaveRecentMapDir(string dirPath)
	{
		try
		{
			string recentPathFile = ProjectSettings.GlobalizePath("user://recent_map_dir.txt");
			string directory = Path.GetDirectoryName(recentPathFile);
			if (!Directory.Exists(directory))
			{
				Directory.CreateDirectory(directory);
			}
			File.WriteAllText(recentPathFile, dirPath);
		}
		catch
		{
		}
	}

	private string GetMapFolderToOpen(string projectRoot)
	{
		try
		{
			string recentPathFile = ProjectSettings.GlobalizePath("user://recent_map_dir.txt");
			if (File.Exists(recentPathFile))
			{
				string path = File.ReadAllText(recentPathFile).Trim();
				if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
				{
					return path.Replace("\\", "/");
				}
			}
		}
		catch
		{
		}

		try
		{
			if (GameHost.Instance != null)
			{
				string docPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
				string blankDir = Path.Combine(docPath, "blank_map");
				if (!Directory.Exists(blankDir))
				{
					((IGameAPI)GameHost.Instance).GenerateMapDirectory("blank_map", docPath);
				}
				return blankDir.Replace("\\", "/");
			}
		}
		catch
		{
		}

		string fallbackMap = GameHost.Instance?.ActiveMapName ?? "melee";
		return Path.Combine(projectRoot, "Maps", fallbackMap).Replace("\\", "/");
	}

	public void CleanUp()
	{
		_actionQueue.Enqueue(() =>
		{
			if (_controller != null)
			{
				_controller.Close();
				_controller = null;
			}
			if (_childHwnd != IntPtr.Zero)
			{
				DestroyWindow(_childHwnd);
				_childHwnd = IntPtr.Zero;
			}
		});
		PostMessage(_childHwnd, WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);

		if (_vscodeProcess != null && !_vscodeProcess.HasExited)
		{
			try
			{
				_vscodeProcess.Kill(true);
			}
			catch
			{
			}
			_vscodeProcess = null;
		}
	}
}
