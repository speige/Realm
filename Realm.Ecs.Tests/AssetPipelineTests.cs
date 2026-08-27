using NUnit.Framework;
using Realm.AssetPipeline;
using System.IO;
using System.Text.Json.Nodes;

namespace Realm.Ecs.Tests;

[TestFixture]
public class AssetPipelineTests
{
	[Test]
	public void TestGlbManifestUtils_BuildAndParseGlb()
	{
		var root = new JsonObject
		{
			["asset"] = new JsonObject { ["version"] = "2.0" },
			["scenes"] = new JsonArray(new JsonObject { ["name"] = "Scene" }),
			["scene"] = 0
		};

		byte[] binData = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
		byte[] glbBytes = GlbManifestUtils.BuildGlb(root, binData);

		Assert.That(GlbManifestUtils.IsValidGlb(glbBytes), Is.True);

		var (parsedJson, parsedBin, version) = GlbManifestUtils.ParseGlb(glbBytes);
		Assert.That(parsedJson, Is.Not.Null);
		Assert.That(version, Is.EqualTo(2));
		Assert.That(parsedBin, Is.Not.Null);
		Assert.That(parsedBin!.Length, Is.GreaterThanOrEqualTo(8));
	}

	[Test]
	public void TestGlbManifestUtils_OptimizationMetadataInjectionAndStripping()
	{
		var root = new JsonObject
		{
			["asset"] = new JsonObject { ["version"] = "2.0" }
		};

		byte[] initialGlb = GlbManifestUtils.BuildGlb(root, null);
		Assert.That(GlbManifestUtils.HasOptimizationFlag(initialGlb), Is.False);

		byte[] optimizedGlb = GlbManifestUtils.InjectOptimizationMetadata(initialGlb, "0.1.0-alpha");
		Assert.That(GlbManifestUtils.HasOptimizationFlag(optimizedGlb), Is.True);

		var (unoptimizedGlb, wasModified) = GlbManifestUtils.StripOptimizationMetadata(optimizedGlb);
		Assert.That(wasModified, Is.True);
		Assert.That(GlbManifestUtils.HasOptimizationFlag(unoptimizedGlb), Is.False);
	}

	[Test]
	public void TestGlbManifestUtils_SanitizeMaterials()
	{
		var root = new JsonObject
		{
			["asset"] = new JsonObject { ["version"] = "2.0" },
			["materials"] = new JsonArray(
				new JsonObject
				{
					["name"] = "Mat1",
					["pbrMetallicRoughness"] = new JsonObject
					{
						["baseColorTexture"] = new JsonObject { ["index"] = 0 }
					}
				}
			)
		};

		byte[] glb = GlbManifestUtils.BuildGlb(root, null);
		byte[] sanitizedGlb = GlbManifestUtils.SanitizeMaterials(glb);

		var (parsed, _, _) = GlbManifestUtils.ParseGlb(sanitizedGlb);
		Assert.That(parsed, Is.Not.Null);
		var mats = parsed!["materials"] as JsonArray;
		Assert.That(mats, Is.Not.Null);
		var pbr = mats![0]!["pbrMetallicRoughness"] as JsonObject;
		Assert.That(pbr, Is.Not.Null);
		Assert.That(pbr!.ContainsKey("baseColorFactor"), Is.True);
	}

	[Test]
	public void TestGlbOptimizer_MetadataInspection()
	{
		var root = new JsonObject
		{
			["asset"] = new JsonObject { ["version"] = "2.0" },
			["meshes"] = new JsonArray(new JsonObject { ["name"] = "Mesh1" }),
			["nodes"] = new JsonArray(new JsonObject { ["name"] = "Node1" }),
			["materials"] = new JsonArray(new JsonObject { ["name"] = "Mat1" })
		};

		byte[] glb = GlbManifestUtils.BuildGlb(root, null);
		var optimizer = new GlbOptimizer();
		var meta = optimizer.GetMetadata(glb);

		Assert.That(meta.MeshCount, Is.EqualTo(1));
		Assert.That(meta.NodeCount, Is.EqualTo(1));
		Assert.That(meta.MaterialCount, Is.EqualTo(1));
		Assert.That(meta.IsOptimized, Is.False);
	}

	[Test]
	public void TestNativeToolRunner_Locator()
	{
		string? path = NativeToolRunner.FindGltfPackPath();
		// In repo environment ThirdPartyBinaries/gltfpack.exe exists
		Assert.That(path, Is.Not.Null);
		Assert.That(File.Exists(path!), Is.True);
	}
}
