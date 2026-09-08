using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;

namespace Realm.Shared.Animation;

public class GlbAutoRiggerOptions
{
    public bool NoFingers { get; set; } = true;
    public bool UseNormals { get; set; } = true;
    public bool WeightPostprocess { get; set; } = true;
    public Action<string>? LogCallback { get; set; }
}

public class GlbAutoRiggerResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? OutputPath { get; set; }
}

public static class GlbAutoRigger
{
    public static GlbAutoRiggerResult RigHumanoid(
        string inputPath,
        string outputPath,
        GlbAutoRiggerOptions? options = null)
    {
        options ??= new GlbAutoRiggerOptions();

        void Log(string msg)
        {
            options.LogCallback?.Invoke(msg);
            Console.WriteLine(msg);
        }

        if (!File.Exists(inputPath))
        {
            return new GlbAutoRiggerResult
            {
                Success = false,
                ErrorMessage = $"Input file does not exist: {inputPath}"
            };
        }

        string fullInputPath = Path.GetFullPath(inputPath);
        string fullOutputPath = Path.GetFullPath(outputPath);

        try
        {
            MakeItAnimatableSetup.EnsureSetup(options.LogCallback);
        }
        catch (Exception ex)
        {
            return new GlbAutoRiggerResult
            {
                Success = false,
                ErrorMessage = $"Make-It-Animatable setup failed: {ex.Message}"
            };
        }

        string? outputDir = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        var optimizer = new GlbOptimizer();
        byte[] sourceBytes = File.ReadAllBytes(fullInputPath);
        bool wasOptimized = optimizer.IsOptimized(sourceBytes);

        string rigSourcePath = fullInputPath;
        string? tempUnoptimizedPath = null;

        try
        {
            if (wasOptimized)
            {
                Log("  Detected pre-optimized GLB — unoptimizing first to restore mesh topology...");
                var unoptResult = optimizer.Unoptimize(sourceBytes);
                if (!unoptResult.Success || unoptResult.OutputGlbBytes == null)
                {
                    return new GlbAutoRiggerResult
                    {
                        Success = false,
                        ErrorMessage = $"Failed to unoptimize {inputPath}: {unoptResult.ErrorMessage}"
                    };
                }

                tempUnoptimizedPath = Path.Combine(
                    Path.GetDirectoryName(fullInputPath) ?? string.Empty,
                    $"__tmp_unopt_rig_{Path.GetFileName(fullInputPath)}");
                File.WriteAllBytes(tempUnoptimizedPath, unoptResult.OutputGlbBytes);
                rigSourcePath = tempUnoptimizedPath;
            }

            var kwargs = new JsonObject
            {
                ["is_gs"] = false,
                ["no_fingers"] = options.NoFingers,
                ["input_normal"] = options.UseNormals,
                ["bw_fix"] = options.WeightPostprocess,
                ["reset_to_rest"] = true,
                ["inplace"] = true,
                ["animation_file"] = JsonValue.Create<string?>(null)
            };
            string kwargsJson = kwargs.ToJsonString();

            Log($"Rigging: {fullInputPath}");
            Log($"  Output:             {fullOutputPath}");
            Log($"  no_fingers:         {options.NoFingers}");
            Log($"  use_normals:        {options.UseNormals}");
            Log($"  weight_postprocess: {options.WeightPostprocess}");

            var psi = new ProcessStartInfo
            {
                FileName = MakeItAnimatableSetup.PythonExePath,
                WorkingDirectory = MakeItAnimatableSetup.NodeDir,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.Environment["PYTHONUNBUFFERED"] = "1";
            psi.Environment["PYTHONIOENCODING"] = "utf-8";
            psi.ArgumentList.Add("-u");
            psi.ArgumentList.Add(MakeItAnimatableSetup.ServerScriptPath);
            psi.ArgumentList.Add("--input");
            psi.ArgumentList.Add(rigSourcePath);
            psi.ArgumentList.Add("--output");
            psi.ArgumentList.Add(fullOutputPath);
            psi.ArgumentList.Add("--kwargs");
            psi.ArgumentList.Add(kwargsJson);

            int exitCode = RunPipelineProcess(psi, Log);

            bool outputCreated = File.Exists(fullOutputPath) && new FileInfo(fullOutputPath).Length > 0;

            if (exitCode != 0 && exitCode != -1073741819 && exitCode != unchecked((int)0xC0000005))
            {
                return new GlbAutoRiggerResult
                {
                    Success = false,
                    ErrorMessage = $"Make-It-Animatable pipeline exited with code {exitCode}."
                };
            }

            if (!outputCreated)
            {
                return new GlbAutoRiggerResult
                {
                    Success = false,
                    ErrorMessage = $"Output rigged file was not created: {fullOutputPath}"
                };
            }
        }
        catch (Exception ex)
        {
            return new GlbAutoRiggerResult
            {
                Success = false,
                ErrorMessage = $"Auto-rigging exception: {ex.Message}"
            };
        }
        finally
        {
            if (tempUnoptimizedPath != null && File.Exists(tempUnoptimizedPath))
            {
                try { File.Delete(tempUnoptimizedPath); } catch { }
            }

            if (tempUnoptimizedPath != null)
            {
                string tempUnoptDir = Path.Combine(
                    Path.GetDirectoryName(tempUnoptimizedPath) ?? string.Empty,
                    Path.GetFileNameWithoutExtension(tempUnoptimizedPath));
                if (Directory.Exists(tempUnoptDir))
                {
                    try { Directory.Delete(tempUnoptDir, true); } catch { }
                }
            }

            string inputDirWithoutExt = Path.Combine(
                Path.GetDirectoryName(fullInputPath) ?? string.Empty,
                Path.GetFileNameWithoutExtension(fullInputPath));
            if (Directory.Exists(inputDirWithoutExt))
            {
                try { Directory.Delete(inputDirWithoutExt, true); } catch { }
            }
        }

        Log("  Re-optimizing output (LODs regenerated from rigged bone structure)...");
        var optimizeResult = optimizer.OptimizeFile(
            fullOutputPath,
            fullOutputPath,
            new OptimizationOptions { ForceReDecimate = true });

        if (!optimizeResult.Success)
        {
            Log($"  Warning: Re-optimization failed: {optimizeResult.ErrorMessage}");
            return new GlbAutoRiggerResult
            {
                Success = true,
                OutputPath = fullOutputPath,
                ErrorMessage = $"Rigged successfully, but re-optimization warning: {optimizeResult.ErrorMessage}"
            };
        }

        Log($"  Successfully rigged and optimized: {fullOutputPath} ({optimizeResult.OriginalSize} -> {optimizeResult.OptimizedSize} bytes)");

        return new GlbAutoRiggerResult
        {
            Success = true,
            OutputPath = fullOutputPath
        };
    }

    private static int RunPipelineProcess(ProcessStartInfo psi, Action<string> log)
    {
        using var proc = new Process { StartInfo = psi };
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) log(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) log(e.Data); };

        if (!proc.Start())
        {
            log("Error: Failed to start Python process.");
            return -1;
        }

        if (psi.RedirectStandardInput)
        {
            proc.StandardInput.Close();
        }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        proc.WaitForExit();

        return proc.ExitCode;
    }
}
