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
	public bool IsVisible => _isVisible;

	private readonly ConcurrentQueue<Action> _actionQueue = new ConcurrentQueue<Action>();

	private const uint WM_USER = 0x0400;
	private const uint WM_WAKEUP = WM_USER + 1;
	private const uint WM_CLOSE = 0x0010;
	private const int SW_HIDE = 0;
	private const int SW_SHOW = 5;

	private const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
	private const uint WS_VISIBLE = 0x10000000;

	private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
	private WndProcDelegate _customWndProcDelegate;

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

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	public struct WNDCLASSEX
	{
		public int cbSize;
		public int style;
		public IntPtr lpfnWndProc;
		public int cbClsExtra;
		public int cbWndExtra;
		public IntPtr hInstance;
		public IntPtr hIcon;
		public IntPtr hCursor;
		public IntPtr hbrBackground;
		public string lpszMenuName;
		public string lpszClassName;
		public IntPtr hIconSm;
	}

	[DllImport("user32.dll")]
	public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

	[DllImport("user32.dll")]
	public static extern bool TranslateMessage(ref MSG lpMsg);

	[DllImport("user32.dll")]
	public static extern IntPtr DispatchMessage(ref MSG lpMsg);

	[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")]
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

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "RegisterClassExW")]
	public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DefWindowProcW")]
	public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
	public static extern IntPtr GetModuleHandle(string lpModuleName);

	public void Initialize(Control containerControl)
	{
		if (_isInitialized)
		{
			_containerControl = containerControl;
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

			if (!File.Exists(exePath))
			{
				GD.PrintErr("VS Code executable not found at: " + exePath);
				return;
			}

			string serverDataDir = Path.Combine(embedDir, "user-data-dir");
			string extensionsDir = Path.Combine(embedDir, "extensions-dir");

			if (IsInstalling)
			{
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
		var wndClass = new WNDCLASSEX();
		wndClass.cbSize = Marshal.SizeOf(typeof(WNDCLASSEX));
		wndClass.style = 0;
		_customWndProcDelegate = CustomWndProc;
		wndClass.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_customWndProcDelegate);
		wndClass.cbClsExtra = 0;
		wndClass.cbWndExtra = 0;
		wndClass.hInstance = GetModuleHandle(null);
		wndClass.hIcon = IntPtr.Zero;
		wndClass.hCursor = IntPtr.Zero;
		wndClass.hbrBackground = IntPtr.Zero;
		wndClass.lpszMenuName = null;
		wndClass.lpszClassName = "VSCodeEmbedWindow";
		wndClass.hIconSm = IntPtr.Zero;

		RegisterClassEx(ref wndClass);

		_childHwnd = CreateWindowEx(
			0,
			"VSCodeEmbedWindow",
			"Realm Editor",
			WS_OVERLAPPEDWINDOW | WS_VISIBLE,
			0,
			0,
			800,
			600,
			IntPtr.Zero,
			IntPtr.Zero,
			GetModuleHandle(null),
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

	private IntPtr CustomWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
	{
		if (msg == 0x0005) // WM_SIZE
		{
			int w = (int)(lParam.ToInt64() & 0xFFFF);
			int h = (int)((lParam.ToInt64() >> 16) & 0xFFFF);
			if (_controller != null)
			{
				_controller.Bounds = new System.Drawing.Rectangle(0, 0, w, h);
			}
		}
		else if (msg == 0x0010) // WM_CLOSE
		{
			_actionQueue.Enqueue(() =>
			{
				ShowWindow(hWnd, SW_HIDE);
				_isVisible = false;
			});
			PostMessage(hWnd, WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);
			return IntPtr.Zero;
		}
		return DefWindowProc(hWnd, msg, wParam, lParam);
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
			
			_controller.AcceleratorKeyPressed += (sender, args) =>
			{
				if (args.VirtualKey == 0x73) // VK_F4
				{
					if (args.KeyEventKind == CoreWebView2KeyEventKind.SystemKeyDown)
					{
						args.Handled = true;
						_actionQueue.Enqueue(() =>
						{
							ShowWindow(_childHwnd, SW_HIDE);
							_isVisible = false;
						});
						PostMessage(_childHwnd, WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);
					}
				}
			};
			
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

	private void PositionOnScreen()
	{
		if (_containerControl == null || !GodotObject.IsInstanceValid(_containerControl))
		{
			return;
		}

		int screenCount = DisplayServer.GetScreenCount();
		int currentScreen = DisplayServer.WindowGetCurrentScreen();
		Vector2I targetPos;
		Vector2I targetSize;

		if (screenCount > 1)
		{
			int targetScreen = (currentScreen + 1) % screenCount;
			targetPos = DisplayServer.ScreenGetPosition(targetScreen);
			targetSize = DisplayServer.ScreenGetSize(targetScreen);
		}
		else
		{
			targetPos = DisplayServer.ScreenGetPosition(currentScreen);
			targetSize = DisplayServer.ScreenGetSize(currentScreen);
		}

		SetWindowPos(_childHwnd, IntPtr.Zero, targetPos.X, targetPos.Y, targetSize.X, targetSize.Y, 0x0014);
	}

	public void SetVisible(bool visible)
	{
		_isVisible = visible;
		if (!_isInitialized)
		{
			return;
		}
		_actionQueue.Enqueue(() =>
		{
			if (_controller != null)
			{
				_controller.IsVisible = visible;
			}
			if (visible)
			{
				PositionOnScreen();
				ShowWindow(_childHwnd, SW_SHOW);
				ShowWindow(_childHwnd, 3);
			}
			else
			{
				ShowWindow(_childHwnd, SW_HIDE);
			}
		});
		PostMessage(_childHwnd, WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);
	}

	public void UpdateBounds()
	{
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
		if (GameHost.Instance != null && GameHost.Instance.IsMapEditorMode)
		{
			string tempWorkspace = ProjectSettings.GlobalizePath("user://temp_map_workspace");
			if (Directory.Exists(tempWorkspace))
			{
				return tempWorkspace.Replace("\\", "/");
			}
		}

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
