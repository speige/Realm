using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Realm.Tools.Cli;

internal static class MakeItAnimatableSetup
{
    private static readonly string AppDataRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Realm.Tools.Cli");

    internal static readonly string NodeDir = Path.Combine(AppDataRoot, "MakeItAnimatable");
    internal static readonly string RepoDir = Path.Combine(NodeDir, "Make_It_Animatable");

    private const string RepoUrl = "https://github.com/jasongzy/Make-It-Animatable.git";
    private const string ModelRepoId = "jasongzy/Make-It-Animatable";
    private const string ModelRevision = "eb12b71253361fd1a7216625a95144af3c58263e";
    private const string MixamoDatasetId = "jasongzy/Mixamo";
    private const string MixamoRevision = "b1c7f4975ea3261d3d0aa2379f6e24754ccde9d8";

    internal static string ServerScriptPath => Path.Combine(NodeDir, "server.py");

    internal static string PythonExePath
    {
        get
        {
            bool isWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;
            return Path.Combine(RepoDir, "venv311",
                isWindows
                    ? Path.Combine("Scripts", "python.exe")
                    : Path.Combine("bin", "python"));
        }
    }

    private static string MarkerPath => Path.Combine(RepoDir, "venv311", "install_completed.marker");

    internal static bool IsSetupComplete() =>
        File.Exists(MarkerPath) &&
        File.Exists(PythonExePath) &&
        File.Exists(ServerScriptPath);

    internal static void EnsureSetup()
    {
        Directory.CreateDirectory(NodeDir);
        ExtractWrapperFiles();

        if (IsSetupComplete())
        {
            Console.WriteLine("[MIA] Make-It-Animatable environment is ready.");
            return;
        }

        Console.WriteLine("[MIA] First-time setup: initializing Make-It-Animatable environment.");
        Console.WriteLine($"[MIA] Install directory: {NodeDir}");
        Console.WriteLine();

        CloneRepo();
        ApplyPatches();
        EnsureVenv();
        DownloadPretrainedModels();
        DownloadMixamoBones();

        Console.WriteLine();
        Console.WriteLine("[MIA] Setup complete.");
    }

    private static void ExtractWrapperFiles()
    {
        Console.WriteLine("[MIA] Extracting wrapper files...");

        var asm = Assembly.GetExecutingAssembly();

        ExtractEmbeddedToFile(asm, "MiaSetup.server.py", Path.Combine(NodeDir, "server.py"));

        string patchesDestDir = Path.Combine(NodeDir, "patches");
        Directory.CreateDirectory(patchesDestDir);

        foreach (var resourceName in asm.GetManifestResourceNames()
            .Where(n => n.Contains(".MiaSetup.patches.") && n.EndsWith(".patch"))
            .OrderBy(n => n))
        {
            string fileName = DerivePatchFileName(resourceName);
            string destPath = Path.Combine(patchesDestDir, fileName);
            ExtractEmbeddedToFile(asm, resourceName, destPath, useExactName: true);
            Console.WriteLine($"[MIA]   Extracted patch: {fileName}");
        }
    }

    private static string DerivePatchFileName(string resourceName)
    {
        const string marker = ".patches.";
        int markerIdx = resourceName.IndexOf(marker, StringComparison.Ordinal);
        if (markerIdx < 0) return Path.GetFileName(resourceName);
        return resourceName[(markerIdx + marker.Length)..];
    }

    private static void ExtractEmbeddedToFile(Assembly asm, string resourceSuffix, string destPath, bool useExactName = false)
    {
        Stream? stream;
        if (useExactName)
        {
            stream = asm.GetManifestResourceStream(resourceSuffix);
        }
        else
        {
            stream = asm.GetManifestResourceNames()
                .Where(n => n.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase))
                .Select(n => asm.GetManifestResourceStream(n))
                .FirstOrDefault(s => s != null);
        }

        if (stream == null)
        {
            throw new FileNotFoundException(
                $"Embedded resource not found: '{resourceSuffix}'. " +
                $"Available resources: {string.Join(", ", asm.GetManifestResourceNames())}");
        }

        string? dir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using (stream)
        using (var fs = File.Create(destPath))
        {
            stream.CopyTo(fs);
        }
    }

    private static void CloneRepo()
    {
        if (Directory.Exists(RepoDir) && Directory.Exists(Path.Combine(RepoDir, ".git")))
        {
            Console.WriteLine("[MIA] Repository already cloned, skipping.");
            return;
        }

        if (Directory.Exists(RepoDir))
        {
            Directory.Delete(RepoDir, true);
        }

        Console.WriteLine($"[MIA] Cloning {RepoUrl}...");
        Console.WriteLine("[MIA] (This may take a few minutes — downloading submodules recursively)");

        var psi = new ProcessStartInfo { FileName = "git", WorkingDirectory = NodeDir, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        psi.ArgumentList.Add("clone");
        psi.ArgumentList.Add("--recursive");
        psi.ArgumentList.Add("--single-branch");
        psi.ArgumentList.Add(RepoUrl);
        psi.ArgumentList.Add(RepoDir);

        RunProcess(psi, "[MIA]", throwOnNonZero: true);
        Console.WriteLine("[MIA] Repository cloned successfully.");
    }

    private static void ApplyPatches()
    {
        string patchesDir = Path.Combine(NodeDir, "patches");
        if (!Directory.Exists(patchesDir)) return;

        var patches = Directory.GetFiles(patchesDir, "*.patch").OrderBy(f => f).ToList();
        if (patches.Count == 0) return;

        Console.WriteLine("[MIA] Applying patches...");

        foreach (var patchPath in patches)
        {
            string patchName = Path.GetFileName(patchPath);
            Console.Write($"[MIA]   {patchName}... ");

            var psi = new ProcessStartInfo { FileName = "git", WorkingDirectory = RepoDir, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            psi.ArgumentList.Add("apply");
            psi.ArgumentList.Add(patchPath);

            var (exitCode, _, stderr) = RunProcessCapture(psi, "[MIA]");

            if (exitCode == 0)
            {
                Console.WriteLine("applied.");
            }
            else if (stderr.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
                     stderr.Contains("patch does not apply", StringComparison.OrdinalIgnoreCase) ||
                     stderr.Contains("already applied", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("already applied, skipped.");
            }
            else
            {
                throw new InvalidOperationException(
                    $"Failed to apply patch '{patchName}' (exit {exitCode}):\n{stderr}");
            }
        }
    }

    private static void EnsureVenv()
    {
        string venvDir = Path.Combine(RepoDir, "venv311");

        if (File.Exists(MarkerPath) && File.Exists(PythonExePath))
        {
            Console.WriteLine("[MIA] Python venv already configured, skipping.");
            return;
        }

        CheckToolAvailable("uv", "--version",
            "uv is required to create the Python environment. Install it from https://github.com/astral-sh/uv");

        Console.WriteLine("[MIA] Creating Python 3.11 virtual environment...");

        var createPsi = new ProcessStartInfo { FileName = "uv", WorkingDirectory = RepoDir, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        createPsi.ArgumentList.Add("venv");
        createPsi.ArgumentList.Add("--clear");
        createPsi.ArgumentList.Add("--python");
        createPsi.ArgumentList.Add("3.11");
        createPsi.ArgumentList.Add(venvDir);

        RunProcess(createPsi, "[MIA]", throwOnNonZero: true);

        string requirementsPath = Path.Combine(RepoDir, "requirements.txt");
        if (!File.Exists(requirementsPath))
        {
            throw new FileNotFoundException($"requirements.txt not found after clone: {requirementsPath}");
        }

        Console.WriteLine("[MIA] Installing Python dependencies from requirements.txt...");
        Console.WriteLine("[MIA] (This may take 10–30 minutes depending on internet speed and GPU drivers)");

        var installPsi = new ProcessStartInfo { FileName = "uv", WorkingDirectory = RepoDir, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        installPsi.ArgumentList.Add("pip");
        installPsi.ArgumentList.Add("install");
        installPsi.ArgumentList.Add("--python");
        installPsi.ArgumentList.Add(PythonExePath);
        installPsi.ArgumentList.Add("-r");
        installPsi.ArgumentList.Add(requirementsPath);

        RunProcess(installPsi, "[MIA]", throwOnNonZero: true);

        File.WriteAllText(MarkerPath, DateTime.UtcNow.ToString("O"));
        Console.WriteLine("[MIA] Python environment ready.");
    }

    private static void DownloadPretrainedModels()
    {
        string modelsDir = Path.Combine(RepoDir, "output", "best", "new");
        Directory.CreateDirectory(modelsDir);

        if (Directory.GetFiles(modelsDir).Length > 0)
        {
            Console.WriteLine("[MIA] Pretrained models already present, skipping download.");
            return;
        }

        Console.WriteLine("[MIA] Downloading pretrained models from HuggingFace (jasongzy/Make-It-Animatable)...");
        Console.WriteLine("[MIA] (Pinned revision: " + ModelRevision[..8] + "...)");

        RunPythonInline($"""
import sys
from huggingface_hub import snapshot_download
snapshot_download(
    repo_type="model",
    repo_id="{ModelRepoId}",
    revision="{ModelRevision}",
    local_dir=r"{EscapeForPython(RepoDir)}",
    allow_patterns=["output/best/new/*"],
)
print("[MIA] Pretrained models downloaded successfully.")
""");
    }

    private static void DownloadMixamoBones()
    {
        string mixamoDir = Path.Combine(RepoDir, "data", "Mixamo");
        Directory.CreateDirectory(mixamoDir);

        if (Directory.GetFiles(mixamoDir, "*.fbx").Length > 0)
        {
            Console.WriteLine("[MIA] Mixamo bone data already present, skipping download.");
            return;
        }

        Console.WriteLine("[MIA] Downloading Mixamo bone data from HuggingFace (jasongzy/Mixamo)...");
        Console.WriteLine("[MIA] (Pinned revision: " + MixamoRevision[..8] + "...)");

        RunPythonInline($"""
import sys
from huggingface_hub import snapshot_download
snapshot_download(
    repo_type="dataset",
    repo_id="{MixamoDatasetId}",
    revision="{MixamoRevision}",
    local_dir=r"{EscapeForPython(mixamoDir)}",
    allow_patterns=["bones*.fbx"],
)
print("[MIA] Mixamo data downloaded successfully.")
""");
    }

    private static void RunPythonInline(string script)
    {
        string tempScript = Path.Combine(
            Path.GetTempPath(),
            $"mia_setup_{Guid.NewGuid():N}.py");

        try
        {
            File.WriteAllText(tempScript, script, Encoding.UTF8);

            var psi = new ProcessStartInfo
            {
                FileName = PythonExePath,
                WorkingDirectory = RepoDir,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            psi.Environment["PYTHONUNBUFFERED"] = "1";
            psi.Environment["PYTHONIOENCODING"] = "utf-8";
            psi.ArgumentList.Add("-u");
            psi.ArgumentList.Add(tempScript);

            RunProcess(psi, "[MIA]", throwOnNonZero: true);
        }
        finally
        {
            try { File.Delete(tempScript); } catch { }
        }
    }

    private static void CheckToolAvailable(string tool, string testArg, string errorMessage)
    {
        try
        {
            var psi = new ProcessStartInfo { FileName = tool, Arguments = testArg, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(5000);
        }
        catch
        {
            throw new InvalidOperationException(errorMessage);
        }
    }

    private static void RunProcess(ProcessStartInfo psi, string logPrefix, bool throwOnNonZero)
    {
        using var proc = new Process { StartInfo = psi };
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine($"{logPrefix} {e.Data}"); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) Console.Error.WriteLine($"{logPrefix} {e.Data}"); };

        if (!proc.Start())
        {
            throw new InvalidOperationException($"Failed to start process: {psi.FileName}");
        }

        if (psi.RedirectStandardInput)
        {
            proc.StandardInput.Close();
        }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        proc.WaitForExit();

        if (throwOnNonZero && proc.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Process exited with code {proc.ExitCode}: {psi.FileName}");
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) RunProcessCapture(ProcessStartInfo psi, string logPrefix)
    {
        using var proc = new Process { StartInfo = psi };
        var stdoutSb = new StringBuilder();
        var stderrSb = new StringBuilder();

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            stdoutSb.AppendLine(e.Data);
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            stderrSb.AppendLine(e.Data);
        };

        if (!proc.Start())
        {
            throw new InvalidOperationException($"Failed to start process: {psi.FileName}");
        }

        if (psi.RedirectStandardInput)
        {
            proc.StandardInput.Close();
        }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        proc.WaitForExit();

        return (proc.ExitCode, stdoutSb.ToString(), stderrSb.ToString());
    }

    private static string EscapeForPython(string path) =>
        path.Replace("\\", "\\\\");
}
