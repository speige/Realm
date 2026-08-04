using Godot;
using Microsoft.Web.WebView2.Core;
using Realm.MapAPI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
	private int _vscodePort = 8089;

	private static readonly string[] RequiredExtensions = new[]
	{
		"ms-dotnettools.csdevkit",
		"OHZIInteractiveStudio.ohzi-vscode-glb-viewer",
		"Gruntfuggly.todo-tree",
		"mechatroner.rainbow-json",
		"patcx.vscode-nuget-gallery",
		"AykutSarac.jsoncrack-vscode"
	};

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
		string markerPath = Path.Combine(embedDir, "bypass_completed.marker");
		return File.Exists(exePath) && File.Exists(markerPath);
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
			string markerPath = Path.Combine(embedDir, "bypass_completed.marker");

			if (File.Exists(exePath) && File.Exists(markerPath))
			{
				_installCompleted = true;
				System.Threading.Tasks.Task.Run(() => InstallMissingExtensions(exePath, embedDir));
				return;
			}

			_isInstalling = true;
			_installTask = System.Threading.Tasks.Task.Run(() =>
			{
				try
				{
					if (!File.Exists(exePath))
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

					if (File.Exists(exePath))
					{
						RunBypassAndVerify(exePath, embedDir);
						InstallMissingExtensions(exePath, embedDir);
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
						_installCompleted = IsInstalled();
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
	private const int SW_SHOWMAXIMIZED = 3;

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

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	public static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

	[DllImport("kernel32.dll")]
	public static extern bool SetInformationJobObject(IntPtr hJob, int JobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

	[DllImport("kernel32.dll")]
	public static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

	private static IntPtr _jobHandle = IntPtr.Zero;

	private static void EnsureChildProcessJobObject()
	{
		if (_jobHandle != IntPtr.Zero || !OperatingSystem.IsWindows()) return;
		try
		{
			_jobHandle = CreateJobObject(IntPtr.Zero, null);
			if (_jobHandle != IntPtr.Zero)
			{
				int JOBOBJECT_EXTENDED_LIMIT_INFORMATION = 9;
				uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

				int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION_STRUCT));
				IntPtr extendedInfoPtr = Marshal.AllocHGlobal(length);
				try
				{
					ZeroMemory(extendedInfoPtr, length);
					Marshal.WriteInt32(extendedInfoPtr, 4, (int)JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE); // BasicLimitInformation.LimitFlags at offset 4 (after 4-byte PerProcessUserTimeLimit/PerJobUserTimeLimit or 8-byte alignment)
					// Structure layout:
					// JOBOBJECT_BASIC_LIMIT_INFORMATION (48 bytes):
					//   LONGLONG PerProcessUserTimeLimit (8)
					//   LONGLONG PerJobUserTimeLimit (8)
					//   DWORD LimitFlags (4) -> offset 16
					Marshal.WriteInt32(extendedInfoPtr, 16, (int)JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE);
					SetInformationJobObject(_jobHandle, JOBOBJECT_EXTENDED_LIMIT_INFORMATION, extendedInfoPtr, (uint)length);
				}
				finally
				{
					Marshal.FreeHGlobal(extendedInfoPtr);
				}
				AssignProcessToJobObject(_jobHandle, Process.GetCurrentProcess().Handle);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to create/assign Job Object: {ex.Message}");
		}
	}

	[DllImport("kernel32.dll", EntryPoint = "RtlZeroMemory")]
	private static extern void ZeroMemory(IntPtr destination, int length);

	[StructLayout(LayoutKind.Sequential)]
	private struct JOBOBJECT_BASIC_LIMIT_INFORMATION_STRUCT
	{
		public long PerProcessUserTimeLimit;
		public long PerJobUserTimeLimit;
		public uint LimitFlags;
		public UIntPtr MinimumWorkingSetSize;
		public UIntPtr MaximumWorkingSetSize;
		public uint ActiveProcessLimit;
		public UIntPtr Affinity;
		public uint PriorityClass;
		public uint SchedulingClass;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct IO_COUNTERS_STRUCT
	{
		public ulong ReadOperationCount;
		public ulong WriteOperationCount;
		public ulong OtherOperationCount;
		public ulong ReadTransferCount;
		public ulong WriteTransferCount;
		public ulong OtherTransferCount;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION_STRUCT
	{
		public JOBOBJECT_BASIC_LIMIT_INFORMATION_STRUCT BasicLimitInformation;
		public IO_COUNTERS_STRUCT IoInfo;
		public UIntPtr ProcessMemoryLimit;
		public UIntPtr JobMemoryLimit;
		public UIntPtr PeakProcessMemoryUsed;
		public UIntPtr PeakJobMemoryUsed;
	}

	private static void AddProcessToJob(Process proc)
	{
		if (proc == null || proc.HasExited || !OperatingSystem.IsWindows()) return;
		try
		{
			EnsureChildProcessJobObject();
			if (_jobHandle != IntPtr.Zero)
			{
				AssignProcessToJobObject(_jobHandle, proc.Handle);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to assign process {proc.Id} to Job Object: {ex.Message}");
		}
	}

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

			EnsureWorkspaceMapApiDll(projectRoot);

			string serverDataDir = Path.Combine(embedDir, "user-data-dir");
			string extensionsDir = Path.Combine(serverDataDir, "extensions");

			if (IsInstalling)
			{
				_installTask?.Wait();
			}

			var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
			l.Start();
			_vscodePort = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
			l.Stop();

			_vscodeProcess = new Process();
			_vscodeProcess.StartInfo.FileName = exePath;
			_vscodeProcess.StartInfo.Arguments = $"--extensions-dir \"{extensionsDir}\" serve-web --port {_vscodePort} --server-data-dir \"{serverDataDir}\" --accept-server-license-terms --without-connection-token";
			_vscodeProcess.StartInfo.CreateNoWindow = true;
			_vscodeProcess.StartInfo.UseShellExecute = false;
			_vscodeProcess.StartInfo.EnvironmentVariables["VSCODE_EXTENSIONS"] = extensionsDir;
			_vscodeProcess.StartInfo.EnvironmentVariables["VSCODE_EXTENSIONS_DIR"] = extensionsDir;
			_vscodeProcess.Start();
			AddProcessToJob(_vscodeProcess);
			GD.Print("VS Code server started successfully.");

			StartIpcHttpListener();
		}
		catch (Exception ex)
		{
			GD.PrintErr("Failed to start VS Code server: " + ex.Message);
		}
	}

	private void EnsureWorkspaceMapApiDll(string projectRoot)
	{
		try
		{
			string mapFolderRaw = GetMapFolderToOpen(projectRoot);
			if (!string.IsNullOrEmpty(mapFolderRaw) && Directory.Exists(mapFolderRaw))
			{
				string libDir = Path.Combine(mapFolderRaw, "lib");
				if (!File.Exists(Path.Combine(libDir, "Realm.MapAPI.dll")))
				{
					GD.Print("Realm.MapAPI.dll missing in workspace. Running EnsureMapProjectFiles...");
					GameHost.EnsureMapProjectFiles(mapFolderRaw);
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to ensure Realm.MapAPI.dll in workspace: {ex.Message}");
		}
	}

	private int _ipcHttpPort = 8092;

	private async void StartIpcHttpListener()
	{
		try
		{
			var listener = new System.Net.HttpListener();
			try
			{
				listener.Prefixes.Add($"http://127.0.0.1:{_ipcHttpPort}/api/");
				listener.Start();
			}
			catch
			{
				_ipcHttpPort = 8093;
				listener = new System.Net.HttpListener();
				listener.Prefixes.Add($"http://127.0.0.1:{_ipcHttpPort}/api/");
				listener.Start();
			}
			GD.Print($"[VSCodeManager] HTTP IPC bridge listening on http://127.0.0.1:{_ipcHttpPort}/api/");

			while (true)
			{
				var ctx = await listener.GetContextAsync();
				_ = System.Threading.Tasks.Task.Run(() =>
				{
					HandleIpcHttpRequest(ctx);
				});
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[VSCodeManager] HTTP IPC bridge error: {ex.Message}");
		}
	}

	private async void HandleIpcHttpRequest(System.Net.HttpListenerContext ctx)
	{
		try
		{
			ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
			ctx.Response.Headers.Add("Access-Control-Allow-Methods", "POST, GET, OPTIONS");
			ctx.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

			if (ctx.Request.HttpMethod == "OPTIONS")
			{
				ctx.Response.StatusCode = 200;
				ctx.Response.Close();
				return;
			}

			using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
			string body = await reader.ReadToEndAsync();
			var node = System.Text.Json.Nodes.JsonNode.Parse(body);
			string action = node?["action"]?.ToString() ?? node?["type"]?.ToString();

			var responseObj = new System.Text.Json.Nodes.JsonObject();

			if (action == "generateSnapshot")
			{
				string filePath = node["filePath"]?.ToString();
				string requestId = node["requestId"]?.ToString();

				string base64Png = "";
				var tcs = new System.Threading.Tasks.TaskCompletionSource<string>();
				Callable.From(async () =>
				{
					try
					{
						string b64 = "";
						if (MapEditorHUD.Instance != null)
						{
							b64 = await MapEditorHUD.Instance.GenerateAssetSnapshotBase64(filePath);
						}
						tcs.TrySetResult(b64);
					}
					catch (Exception ex)
					{
						GD.PrintErr($"[VSCodeManager] GenerateAssetSnapshotBase64 error: {ex.Message}");
						tcs.TrySetResult("");
					}
				}).CallDeferred();

				base64Png = await tcs.Task;

				responseObj["action"] = "snapshotResult";
				responseObj["type"] = "snapshotResult";
				responseObj["requestId"] = requestId;
				responseObj["filePath"] = filePath;
				responseObj["base64"] = base64Png;
			}
			else if (action == "processRawTexture")
			{
				string rawPngPath = node["rawPngPath"]?.ToString();
				string rawBase64 = node["rawBase64"]?.ToString();
				string outputKtx2Path = node["outputKtx2Path"]?.ToString();
				string swatchName = node["swatchName"]?.ToString();
				string requestId = node["requestId"]?.ToString();

				bool success = false;
				string errorMsg = "";

				try
				{
					string targetPngPath = rawPngPath;
					if (string.IsNullOrEmpty(targetPngPath) && !string.IsNullOrEmpty(rawBase64))
					{
						byte[] pngBytes = Convert.FromBase64String(rawBase64);
						targetPngPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"realm_tx_{swatchName}_{DateTime.Now.Ticks}.png");
						System.IO.File.WriteAllBytes(targetPngPath, pngBytes);
					}

					if (!string.IsNullOrEmpty(targetPngPath) && !string.IsNullOrEmpty(outputKtx2Path))
					{
						var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
						string innerErr = "";
						Callable.From(() =>
						{
							try
							{
								MapEditorHUD.Instance?.ConvertRawTextureDirect(targetPngPath, outputKtx2Path, swatchName);
								tcs.TrySetResult(System.IO.File.Exists(outputKtx2Path));
							}
							catch (Exception ex)
							{
								innerErr = ex.Message;
								GD.PrintErr($"[VSCodeManager] ConvertRawTextureDirect error: {ex.Message}");
								tcs.TrySetResult(false);
							}
						}).CallDeferred();

						success = await tcs.Task;
						if (!success && !string.IsNullOrEmpty(innerErr)) errorMsg = innerErr;
					}
				}
				catch (Exception ex)
				{
					errorMsg = ex.Message;
				}

				responseObj["action"] = "processRawTextureResult";
				responseObj["type"] = "processRawTextureResult";
				responseObj["requestId"] = requestId;
				responseObj["success"] = success;
				responseObj["error"] = errorMsg;
			}
			else if (action == "reloadMetadata" || action == "updateMetadata")
			{
				Callable.From(() =>
				{
					if (MapEditorHUD.Instance != null)
					{
						MapEditorHUD.Instance.ReadMetadataAndRefreshTextures();
					}
					else if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null)
					{
						GameHost.Instance.GroundTerrain.ReloadTerrainTextures(true);
					}
				}).CallDeferred();

				responseObj["action"] = "reloadMetadataResult";
				responseObj["type"] = "reloadMetadataResult";
				responseObj["success"] = true;
			}
			else if (action == "updateTextureParam")
			{
				string swatchName = node["swatchName"]?.ToString() ?? node["swatch"]?.ToString() ?? "";
				string tileMode = node["tileMode"]?.ToString() ?? node["Tile_Mode"]?.ToString() ?? "Stochastic";
				float uvScale = 1.0f;
				if (node["uvScale"] != null && float.TryParse(node["uvScale"].ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedUv))
				{
					uvScale = parsedUv;
				}
				else if (node["UV_Scale"] != null && float.TryParse(node["UV_Scale"].ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedUv2))
				{
					uvScale = parsedUv2;
				}

				float stochasticTileSize = 1.0f;
				if (node["stochasticTileSize"] != null && float.TryParse(node["stochasticTileSize"].ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedStoch))
				{
					stochasticTileSize = parsedStoch;
				}
				else if (node["Stochastic_Tile_Size"] != null && float.TryParse(node["Stochastic_Tile_Size"].ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedStoch2))
				{
					stochasticTileSize = parsedStoch2;
				}

				Callable.From(() =>
				{
					if (GameHost.Instance != null && GameHost.Instance.GroundTerrain != null)
					{
						GameHost.Instance.GroundTerrain.UpdateTextureParamDirect(swatchName, tileMode, uvScale, stochasticTileSize);
					}
				}).CallDeferred();

				responseObj["action"] = "updateTextureParamResult";
				responseObj["type"] = "updateTextureParamResult";
				responseObj["success"] = true;
			}

			string resJson = responseObj.ToJsonString();
			byte[] resBytes = System.Text.Encoding.UTF8.GetBytes(resJson);
			ctx.Response.ContentType = "application/json";
			ctx.Response.ContentLength64 = resBytes.Length;
			await ctx.Response.OutputStream.WriteAsync(resBytes, 0, resBytes.Length);
			ctx.Response.Close();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[VSCodeManager] HandleIpcHttpRequest error: {ex.Message}");
			try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
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
			if (!_isVisible) return IntPtr.Zero; // already hidden
			_actionQueue.Enqueue(() =>
			{
				if (_controller != null && _controller.CoreWebView2 != null)
				{
					_controller.CoreWebView2.Navigate("about:blank");
					_controller.IsVisible = false;
				}
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
				else if (args.VirtualKey == 0x7B) // VK_F12
				{
					if (args.KeyEventKind == CoreWebView2KeyEventKind.KeyDown)
					{
						args.Handled = true;
						_actionQueue.Enqueue(() =>
						{
							_controller?.CoreWebView2?.OpenDevToolsWindow();
						});
					}
				}
			};
			
			string mapFolderRaw = GetMapFolderToOpen(projectRoot);
			string unitsPathRaw = Path.Combine(mapFolderRaw, "metadata.json");
			string scriptPathRaw = Path.Combine(mapFolderRaw, "MapScript.cs");
			
			string mapFolder = FormatWinPathForUrl(mapFolderRaw);
			string unitsPath = FormatWinPathForUrl(unitsPathRaw);
			string scriptPath = FormatWinPathForUrl(scriptPathRaw);
			
			string targetUrl = $"http://127.0.0.1:{_vscodePort}/?folder={Uri.EscapeDataString(mapFolder)}&ipcPort={_ipcHttpPort}";

			_controller.CoreWebView2.NavigationCompleted += async (sender, args) =>
			{
				if (!args.IsSuccess || sender != _controller.CoreWebView2) return;
				try
				{
					await _controller.CoreWebView2.ExecuteScriptAsync(@"
(async () => {
    const DB = 'vscode-web-db';
    const STORE = 'vscode-userdata-store';
    const KEY = '/User/settings.json';
    try {
        const db = await new Promise((resolve, reject) => {
            const r = indexedDB.open(DB);
            r.onupgradeneeded = (e) => {
                if (!e.target.result.objectStoreNames.contains(STORE))
                    e.target.result.createObjectStore(STORE);
            };
            r.onsuccess = (e) => resolve(e.target.result);
            r.onerror = (e) => reject(e.target.error);
        });
        let config = {};
        const raw = await new Promise((resolve, reject) => {
            const t = db.transaction([STORE], 'readwrite');
            const g = t.objectStore(STORE).get(KEY);
            g.onsuccess = () => resolve(g.result);
            g.onerror = () => reject(g.error);
        });
        if (raw) {
            const buf = raw instanceof Uint8Array ? raw : raw.value;
            if (buf instanceof Uint8Array) {
                const s = new TextDecoder('utf-8').decode(buf).trim();
                if (s) config = JSON.parse(s);
            }
        }
        if (config['security.workspace.trust.enabled'] === false &&
            config['security.workspace.trust.startupPrompt'] === 'never') {
            return;
        }
        config['security.workspace.trust.enabled'] = false;
        config['security.workspace.trust.startupPrompt'] = 'never';
        const encoded = new TextEncoder().encode(JSON.stringify(config, null, '\t'));
        await new Promise((resolve, reject) => {
            const t = db.transaction([STORE], 'readwrite');
            const p = t.objectStore(STORE).put(encoded, KEY);
            p.onsuccess = () => resolve();
            p.onerror = () => reject(p.error);
        });
        window.location.replace(window.location.href);
    } catch (e) {
        console.error('Failed to set workspace trust:', e);
    }
})();
");
				}
				catch { }
			};

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
			if (visible)
			{
				_containerControl = null;
				Initialize(_containerControl);
			}
			return;
		}

		if (visible)
		{
			if (_vscodeProcess != null && _vscodeProcess.HasExited)
			{
				StartVSCodeServer();
				if (_vscodeProcess != null && !_vscodeProcess.HasExited)
				{
					_vscodeProcess.WaitForExit(5000);
				}
			}
		}

		_actionQueue.Enqueue(() =>
		{
			if (visible)
			{
				bool controllerValid = false;
				try
				{
					controllerValid = _controller != null && _controller.CoreWebView2 != null;
				}
				catch
				{
					controllerValid = false;
				}

				if (!controllerValid)
				{
					InitializeWebView();
					return;
				}

				string projectRoot = ProjectSettings.GlobalizePath("res://");
				string mapFolderRaw = GetMapFolderToOpen(projectRoot);
				string mapFolder = FormatWinPathForUrl(mapFolderRaw);
				string targetUrl = $"http://127.0.0.1:{_vscodePort}/?folder={Uri.EscapeDataString(mapFolder)}";
				_controller.CoreWebView2.Navigate(targetUrl);

				_controller.IsVisible = true;
				PositionOnScreen();
				ShowWindow(_childHwnd, SW_SHOWMAXIMIZED);
			}
			else
			{
				if (_controller != null)
				{
					_controller.IsVisible = false;
				}
				ShowWindow(_childHwnd, SW_HIDE);
			}
		});

		if (_childHwnd != IntPtr.Zero)
		{
			PostMessage(_childHwnd, WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);
		}
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
				string payload = System.Text.Json.JsonSerializer.Serialize(new[] { new[] { "openFile", fullPath } });
				string targetUrl = $"http://127.0.0.1:{_vscodePort}/?folder={Uri.EscapeDataString(mapFolder)}&payload={Uri.EscapeDataString(payload)}";
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
			string tempWorkspace = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
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

		_isInitialized = false;
	}

	private void RunBypassAndVerify(string exePath, string embedDir)
	{
		string serverDataDir = Path.Combine(embedDir, "user-data-dir");
		string extensionsDir = Path.Combine(serverDataDir, "extensions");

		Process tempProcess = new Process();
		tempProcess.StartInfo.FileName = exePath;
		tempProcess.StartInfo.Arguments = $"--extensions-dir \"{extensionsDir}\" serve-web --port 8089 --server-data-dir \"{serverDataDir}\" --accept-server-license-terms --without-connection-token";
		tempProcess.StartInfo.CreateNoWindow = true;
		tempProcess.StartInfo.UseShellExecute = false;
		tempProcess.StartInfo.EnvironmentVariables["VSCODE_EXTENSIONS"] = extensionsDir;
		tempProcess.StartInfo.EnvironmentVariables["VSCODE_EXTENSIONS_DIR"] = extensionsDir;
		tempProcess.Start();
		AddProcessToJob(tempProcess);

		System.Threading.Thread.Sleep(2000);

		var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
		var thread = new Thread(() =>
		{
			try
			{
				RunBypassSTA(tcs, embedDir);
			}
			catch (Exception ex)
			{
				GD.PrintErr("Bypass STA thread failed: " + ex.Message);
				tcs.TrySetException(ex);
			}
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();

		bool success = false;
		if (tcs.Task.Wait(45000))
		{
			success = tcs.Task.Result;
		}
		else
		{
			GD.PrintErr("Bypass task timed out.");
		}

		try
		{
			if (!tempProcess.HasExited)
			{
				tempProcess.Kill(true);
			}
		}
		catch
		{
		}

		if (success)
		{
			try
			{
				string markerPath = Path.Combine(embedDir, "bypass_completed.marker");
				File.WriteAllText(markerPath, "completed");
			}
			catch (Exception ex)
			{
				GD.PrintErr("Failed to write bypass marker file: " + ex.Message);
			}
		}
	}

	private void RunBypassSTA(System.Threading.Tasks.TaskCompletionSource<bool> tcs, string embedDir)
	{
		var syncContext = new SingleThreadSynchronizationContext();
		SynchronizationContext.SetSynchronizationContext(syncContext);

		var wndClass = new WNDCLASSEX();
		wndClass.cbSize = Marshal.SizeOf(typeof(WNDCLASSEX));
		wndClass.style = 0;
		WndProcDelegate bypassWndProc = BypassWndProc;
		wndClass.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(bypassWndProc);
		wndClass.cbClsExtra = 0;
		wndClass.cbWndExtra = 0;
		wndClass.hInstance = GetModuleHandle(null);
		wndClass.hIcon = IntPtr.Zero;
		wndClass.hCursor = IntPtr.Zero;
		wndClass.hbrBackground = IntPtr.Zero;
		wndClass.lpszMenuName = null;
		wndClass.lpszClassName = "VSCodeBypassWindow";
		wndClass.hIconSm = IntPtr.Zero;

		RegisterClassEx(ref wndClass);

		IntPtr bypassHwnd = CreateWindowEx(
			0,
			"VSCodeBypassWindow",
			"Bypass Window",
			0,
			0,
			0,
			100,
			100,
			IntPtr.Zero,
			IntPtr.Zero,
			GetModuleHandle(null),
			IntPtr.Zero);

		if (bypassHwnd == IntPtr.Zero)
		{
			tcs.TrySetResult(false);
			return;
		}

		CoreWebView2Controller controller = null;
		bool running = true;

		Action closeAction = () =>
		{
			if (controller != null)
			{
				controller.Close();
				controller = null;
			}
			running = false;
			PostMessage(bypassHwnd, WM_USER + 1, IntPtr.Zero, IntPtr.Zero);
		};

		InitializeBypassWebView(
			bypassHwnd,
			tcs,
			syncContext,
			c => { controller = c; },
			closeAction,
			embedDir
		);

		MSG msg;
		while (running && GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
		{
			if (msg.message == 0x0010)
			{
				running = false;
				break;
			}
			TranslateMessage(ref msg);
			DispatchMessage(ref msg);
			syncContext.RunPending();
		}
	}

	private static IntPtr BypassWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
	{
		return DefWindowProc(hWnd, msg, wParam, lParam);
	}

	private async void InitializeBypassWebView(IntPtr hwnd, System.Threading.Tasks.TaskCompletionSource<bool> tcs, SingleThreadSynchronizationContext syncContext, Action<CoreWebView2Controller> setController, Action closeAction, string embedDir)
	{
		try
		{
			string serverDataDir = Path.Combine(embedDir, "user-data-dir");
			string cachePath = Path.Combine(serverDataDir, "webview-cache");

			var env = await CoreWebView2Environment.CreateAsync(userDataFolder: cachePath);
			var localController = await env.CreateCoreWebView2ControllerAsync(hwnd);
			setController(localController);
			localController.Bounds = new System.Drawing.Rectangle(0, 0, 100, 100);
			localController.IsVisible = false;

			int navigationCount = 0;
			int retryCount = 0;
			localController.CoreWebView2.NavigationCompleted += async (sender, args) =>
			{
				if (!args.IsSuccess)
				{
					if (navigationCount == 0 && retryCount < 10)
					{
						retryCount++;
						await System.Threading.Tasks.Task.Delay(500);
						localController.CoreWebView2.Navigate($"http://127.0.0.1:{_vscodePort}/");
					}
					else
					{
						GD.PrintErr($"Bypass navigation failed: {args.WebErrorStatus}");
						tcs.TrySetResult(false);
						closeAction();
					}
					return;
				}

				navigationCount++;
				if (navigationCount == 1)
				{
					string jsScript = """
					(async () => {
						const DB_NAME = 'vscode-web-db';
						const STORE_NAME = 'vscode-userdata-store'; 
						const TARGET_KEY = '/User/settings.json';

						const request = indexedDB.open(DB_NAME);

						request.onsuccess = (event) => {
							const db = event.target.result;
							const transaction = db.transaction([STORE_NAME], 'readwrite');
							const store = transaction.objectStore(STORE_NAME);
							
							const getRequest = store.get(TARGET_KEY);

							getRequest.onsuccess = () => {
								let config = {};
								const rawData = getRequest.result;

								if (rawData) {
									const buffer = rawData instanceof Uint8Array ? rawData : rawData.value;
									
									if (buffer instanceof Uint8Array) {
										try {
											const decoder = new TextDecoder('utf-8');
											const jsonString = decoder.decode(buffer);
											if (jsonString.trim()) {
												config = JSON.parse(jsonString);
											}
										} catch (e) {
											console.warn("Error parsing existing binary settings. Resetting configuration layer.", e);
										}
									}
								}

								config["security.workspace.trust.enabled"] = false;
								config["security.workspace.trust.startupPrompt"] = "never";

								const updatedJsonString = JSON.stringify(config, null, '\t');
								const encoder = new TextEncoder();
								const encodedUint8Array = encoder.encode(updatedJsonString);

								let putPayload;
								if (rawData && typeof rawData === 'object' && !(rawData instanceof Uint8Array) && 'key' in rawData) {
									putPayload = { key: TARGET_KEY, value: encodedUint8Array };
								} else {
									putPayload = encodedUint8Array;
								}

								const putRequest = store.keyPath === null || !store.keyPath
									? store.put(putPayload, TARGET_KEY)
									: store.put(putPayload);

								putRequest.onsuccess = () => {
									console.log("%c[Success] Restricted mode successfully disabled via binary mutation! Reloading...", "color: #00ff00; font-weight: bold;");
									window.location.reload();
								};

								putRequest.onerror = (e) => console.error("Failed to write binary buffer to IndexedDB store:", e);
							};
						};

						request.onerror = () => console.error("Could not establish a database connection to:", DB_NAME);
					})();
					""";
					try
					{
						await localController.CoreWebView2.ExecuteScriptAsync(jsScript);
					}
					catch (Exception ex)
					{
						GD.PrintErr("ExecuteScriptAsync failed: " + ex.Message);
						tcs.TrySetResult(false);
						closeAction();
					}
				}
				else if (navigationCount == 2)
				{
					tcs.TrySetResult(true);
					closeAction();
				}
			};

			localController.CoreWebView2.Navigate($"http://127.0.0.1:{_vscodePort}/");
		}
		catch (Exception ex)
		{
			GD.PrintErr("InitializeBypassWebView failed: " + ex.Message);
			tcs.TrySetResult(false);
			closeAction();
		}
	}

	private void InstallMissingExtensions(string exePath, string embedDir)
	{
		try
		{
			string serverDataDir = Path.Combine(embedDir, "user-data-dir");
			string extensionsDir = Path.Combine(serverDataDir, "extensions");

			foreach (string extensionId in RequiredExtensions)
			{
				if (!IsExtensionInstalled(extensionsDir, extensionId))
				{
					GD.Print("VS Code: Installing missing extension " + extensionId);
					using (var process = new Process())
					{
						process.StartInfo.FileName = exePath;
						process.StartInfo.Arguments = $"--extensions-dir \"{extensionsDir}\" ext install {extensionId}";
						process.StartInfo.CreateNoWindow = true;
						process.StartInfo.UseShellExecute = false;
						process.Start();
						AddProcessToJob(process);
						process.WaitForExit();
					}
				}
			}

			string realmMapEditorId = "speige.realm-map-editor";
			string srcPath = Path.GetFullPath(Path.Combine(
				ProjectSettings.GlobalizePath("res://"),
				"..", "Realm.MapEditorExtension"
			));
			string dstPath = Path.Combine(extensionsDir, $"{realmMapEditorId}-1.0.0");

			// Always sync compiled extension files on launch so updates take effect without deleting vscode_embedded
			try
			{
				if (Directory.Exists(srcPath))
				{
					Directory.CreateDirectory(dstPath);
					CopyItemIfExists(Path.Combine(srcPath, "package.json"), Path.Combine(dstPath, "package.json"));
					CopyItemIfExists(Path.Combine(srcPath, "map_schema.json"), Path.Combine(dstPath, "map_schema.json"));
					CopyDirectoryIfExists(Path.Combine(srcPath, "dist"), Path.Combine(dstPath, "dist"));
					CopyDirectoryIfExists(Path.Combine(srcPath, "media"), Path.Combine(dstPath, "media"));
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr("VS Code: Failed to sync Realm Map Editor extension files: " + ex.Message);
			}

			if (!IsExtensionInstalled(extensionsDir, realmMapEditorId))
			{
				GD.Print("VS Code: Registering Realm Map Editor extension...");
				try
				{
					if (Directory.Exists(srcPath))
					{

						string obsoletePath = Path.Combine(extensionsDir, ".obsolete");
						if (File.Exists(obsoletePath))
						{
							var obsoleteJson = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, bool>>(File.ReadAllText(obsoletePath));
							if (obsoleteJson != null && obsoleteJson.Remove("speige.realm-map-editor-1.0.0"))
							{
								File.WriteAllText(obsoletePath, System.Text.Json.JsonSerializer.Serialize(obsoleteJson));
								GD.Print("VS Code: Removed Realm Map Editor from .obsolete.");
							}
						}

						string extensionsJsonPath = Path.Combine(extensionsDir, "extensions.json");
						if (File.Exists(extensionsJsonPath))
						{
							var jsonNode = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(extensionsJsonPath));
							if (jsonNode is System.Text.Json.Nodes.JsonArray jsonArray)
							{
								bool alreadyExists = false;
								foreach (var item in jsonArray)
								{
									if (item?["identifier"]?["id"]?.GetValue<string>() == "speige.realm-map-editor")
									{
										alreadyExists = true;
										break;
									}
								}

								if (!alreadyExists)
								{
									string dstAbsPath = Path.GetFullPath(dstPath).Replace("\\", "/");
									var newEntry = new System.Text.Json.Nodes.JsonObject
									{
										["identifier"] = new System.Text.Json.Nodes.JsonObject { ["id"] = "speige.realm-map-editor" },
										["version"] = "1.0.0",
										["location"] = new System.Text.Json.Nodes.JsonObject
										{
											["$mid"] = 1,
											["path"] = "/" + dstAbsPath,
											["scheme"] = "file"
										},
										["relativeLocation"] = "speige.realm-map-editor-1.0.0",
										["metadata"] = new System.Text.Json.Nodes.JsonObject
										{
											["installedTimestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
											["source"] = "local",
											["isApplicationScoped"] = false,
											["isMachineScoped"] = false
										}
									};
									jsonArray.Add(newEntry);
									File.WriteAllText(extensionsJsonPath, jsonNode.ToJsonString());
									GD.Print("VS Code: Registered Realm Map Editor in extensions.json.");
								}
							}
						}

						GD.Print("VS Code: Realm Map Editor extension installed successfully.");
					}
					else
					{
						GD.PrintErr("VS Code: Realm Map Editor extension source not found at " + srcPath);
					}
				}
				catch (Exception ex)
				{
					GD.PrintErr("VS Code: Failed to install Realm Map Editor extension: " + ex.Message);
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr("Failed to install missing VS Code extensions: " + ex.Message);
		}
	}

	private static void CopyItemIfExists(string src, string dst)
	{
		if (File.Exists(src))
			File.Copy(src, dst, true);
	}

	private static void CopyDirectoryIfExists(string src, string dst)
	{
		if (Directory.Exists(src))
		{
			Directory.CreateDirectory(dst);
			foreach (string filePath in Directory.GetFiles(src))
			{
				string fileName = Path.GetFileName(filePath);
				File.Copy(filePath, Path.Combine(dst, fileName), true);
			}
		}
	}

	private bool IsExtensionInstalled(string extensionsDir, string extensionId)
	{
		if (!Directory.Exists(extensionsDir))
		{
			return false;
		}
		try
		{
			string[] directories = Directory.GetDirectories(extensionsDir);
			foreach (string directory in directories)
			{
				string name = Path.GetFileName(directory);
				if (name.StartsWith(extensionId, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
		}
		catch
		{
		}
		return false;
	}

	private class SingleThreadSynchronizationContext : SynchronizationContext
	{
		private readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();

		public override void Post(SendOrPostCallback d, object? state)
		{
			_queue.Enqueue(() => d(state));
		}

		public override void Send(SendOrPostCallback d, object? state)
		{
			throw new NotSupportedException();
		}

		public void RunPending()
		{
			while (_queue.TryDequeue(out var action))
			{
				action();
			}
		}
	}
}
