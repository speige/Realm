using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Realm.Shared.ModelOptimization;

public static unsafe class MeshOptimizerNative
{
	private const string LibraryName = "meshoptimizer";

	static MeshOptimizerNative()
	{
		NativeLibrary.SetDllImportResolver(typeof(MeshOptimizerNative).Assembly, ResolveDll);
	}

	private static IntPtr ResolveDll(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
	{
		if (libraryName != LibraryName)
		{
			return IntPtr.Zero;
		}

		if (NativeLibrary.TryLoad(LibraryName, assembly, searchPath, out IntPtr handle))
		{
			return handle;
		}

		string baseDir = AppDomain.CurrentDomain.BaseDirectory;
		string? processDir = !string.IsNullOrEmpty(Environment.ProcessPath) ? Path.GetDirectoryName(Environment.ProcessPath) : null;
		string? asmDir = !string.IsNullOrEmpty(assembly.Location) ? Path.GetDirectoryName(assembly.Location) : null;
		bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
		bool isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
		bool isOsx = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

		string relativePath = isWindows ? Path.Combine("runtimes", "win-x64", "native", "meshoptimizer.dll") :
			(isLinux ? Path.Combine("runtimes", "linux-x64", "native", "libmeshoptimizer.so") :
			Path.Combine("runtimes", "osx-x64", "native", "libmeshoptimizer.dylib"));

		string fileName = isWindows ? "meshoptimizer.dll" : (isLinux ? "libmeshoptimizer.so" : "libmeshoptimizer.dylib");

		var candidatePaths = new List<string>
		{
			Path.Combine(baseDir, relativePath),
			Path.Combine(baseDir, fileName),
			Path.Combine(baseDir, "ThirdPartyBinaries", fileName)
		};

		if (!string.IsNullOrEmpty(processDir))
		{
			candidatePaths.Add(Path.Combine(processDir, relativePath));
			candidatePaths.Add(Path.Combine(processDir, fileName));
			candidatePaths.Add(Path.Combine(processDir, "ThirdPartyBinaries", fileName));
		}

		if (!string.IsNullOrEmpty(asmDir))
		{
			candidatePaths.Add(Path.Combine(asmDir, relativePath));
			candidatePaths.Add(Path.Combine(asmDir, fileName));
			candidatePaths.Add(Path.Combine(asmDir, "ThirdPartyBinaries", fileName));
		}

		foreach (string candidate in candidatePaths)
		{
			if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out handle))
			{
				return handle;
			}
		}

		try
		{
			string tempDir = Path.Combine(Path.GetTempPath(), "realm_native_libs");
			string tempDll = Path.Combine(tempDir, fileName);
			if (File.Exists(tempDll) && NativeLibrary.TryLoad(tempDll, out handle))
			{
				return handle;
			}

			System.IO.Stream? stream = assembly.GetManifestResourceStream($"Realm.Shared.ThirdPartyBinaries.{fileName}");
			if (stream == null)
			{
				foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
				{
					var names = a.GetManifestResourceNames();
					foreach (var name in names)
					{
						if (name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
						{
							stream = a.GetManifestResourceStream(name);
							break;
						}
					}
					if (stream != null) break;
				}
			}

			if (stream != null)
			{
				Directory.CreateDirectory(tempDir);
				using (var fileStream = File.Create(tempDll))
				{
					stream.CopyTo(fileStream);
				}
				if (NativeLibrary.TryLoad(tempDll, out handle))
				{
					return handle;
				}
			}
		}
		catch
		{
		}

		return IntPtr.Zero;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct Stream
	{
		public void* Data;
		public nuint Size;
		public nuint Stride;
	}

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "meshopt_generateVertexRemap")]
	public static extern nuint meshopt_generateVertexRemap(
		uint* destination,
		uint* indices,
		nuint index_count,
		void* vertices,
		nuint vertex_count,
		nuint vertex_size);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "meshopt_generateVertexRemapMulti")]
	public static extern nuint meshopt_generateVertexRemapMulti(
		uint* destination,
		uint* indices,
		nuint index_count,
		nuint vertex_count,
		Stream* streams,
		nuint stream_count);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "meshopt_remapVertexBuffer")]
	public static extern void meshopt_remapVertexBuffer(
		void* destination,
		void* vertices,
		nuint vertex_count,
		nuint vertex_size,
		uint* remap);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "meshopt_remapIndexBuffer")]
	public static extern void meshopt_remapIndexBuffer(
		uint* destination,
		uint* indices,
		nuint index_count,
		uint* remap);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "meshopt_simplify")]
	public static extern nuint meshopt_simplify(
		uint* destination,
		uint* indices,
		nuint index_count,
		float* vertex_positions,
		nuint vertex_count,
		nuint vertex_positions_stride,
		nuint target_index_count,
		float target_error,
		uint options,
		float* result_error);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "meshopt_simplifyWithAttributes")]
	public static extern nuint meshopt_simplifyWithAttributes(
		uint* destination,
		uint* indices,
		nuint index_count,
		float* vertex_positions,
		nuint vertex_count,
		nuint vertex_positions_stride,
		float* vertex_attributes,
		nuint vertex_attributes_stride,
		float* attribute_weights,
		nuint attribute_count,
		byte* vertex_lock,
		nuint target_index_count,
		float target_error,
		uint options,
		float* result_error);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "meshopt_simplifySloppy")]
	public static extern nuint meshopt_simplifySloppy(
		uint* destination,
		uint* indices,
		nuint index_count,
		float* vertex_positions,
		nuint vertex_count,
		nuint vertex_positions_stride,
		byte* vertex_lock,
		nuint target_index_count,
		float target_error,
		float* result_error);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "meshopt_simplifyScale")]
	public static extern float meshopt_simplifyScale(
		float* vertex_positions,
		nuint vertex_count,
		nuint vertex_positions_stride);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "meshopt_optimizeVertexCache")]
	public static extern void meshopt_optimizeVertexCache(
		uint* destination,
		uint* indices,
		nuint index_count,
		nuint vertex_count);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "meshopt_optimizeOverdraw")]
	public static extern void meshopt_optimizeOverdraw(
		uint* destination,
		uint* indices,
		nuint index_count,
		float* vertex_positions,
		nuint vertex_count,
		nuint vertex_positions_stride,
		float threshold);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "meshopt_optimizeVertexFetchRemap")]
	public static extern nuint meshopt_optimizeVertexFetchRemap(
		uint* destination,
		uint* indices,
		nuint index_count,
		nuint vertex_count);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "meshopt_optimizeVertexFetch")]
	public static extern nuint meshopt_optimizeVertexFetch(
		void* destination,
		uint* indices,
		nuint index_count,
		void* vertices,
		nuint vertex_count,
		nuint vertex_size);
}
