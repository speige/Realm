using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;

namespace Realm.Distribution.Tests;

[TestFixture]
public class DistributionCoreTests
{
    private string _testDirectory = string.Empty;

    [SetUp]
    public void Setup()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "RealmDistTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }
        catch
        {
        }
    }

    [Test]
    public void GenerateAndSaveAdminKeys()
    {
        var (privateKey, publicKey) = AdminBypassAuth.GenerateAdminKeyPair();
        Assert.That(privateKey, Is.Not.Null.And.Not.Empty);
        Assert.That(publicKey, Is.Not.Null.And.Not.Empty);

        string solutionDirectory = @"D:\git\Realm\Realm";
        string privateKeyFilePath = Path.Combine(solutionDirectory, "admin_private.key");
        File.WriteAllText(privateKeyFilePath, privateKey);

        string appSettingsPath = Path.Combine(solutionDirectory, "Realm.Lobby", "appsettings.json");
        string appSettingsJson = $"{{\n  \"AdminPublicKey\": \"{publicKey}\",\n  \"StorageDirectory\": \".data/cas\",\n  \"CapacityPercentage\": 100\n}}\n";
        File.WriteAllText(appSettingsPath, appSettingsJson);

        string bypassToken = AdminBypassAuth.CreateBypassToken(privateKey, "asset_package", "1.0.0");
        bool verified = AdminBypassAuth.VerifyBypassToken(publicKey, "asset_package", "1.0.0", bypassToken);
        Assert.That(verified, Is.True);
    }

    [Test]
    public void GenerateAssetPackageManifest()
    {
        string assetPackageDirectory = @"C:\temp\asset_package";
        if (!Directory.Exists(assetPackageDirectory))
        {
            Assert.Ignore("C:\\temp\\asset_package does not exist on this machine.");
            return;
        }

        var manifest = MapManifest.CreateFromDirectory(
            assetPackageDirectory,
            "asset_package",
            "Realm",
            "1.0.0",
            "Asset Package containing categorized 2D, 3D, animations, and audio dependencies.",
            new List<string> { "AssetPack" });

        Assert.That(manifest.Files.Count, Is.GreaterThan(70));
        string manifestPath = Path.Combine(assetPackageDirectory, "manifest.json");
        manifest.SaveToFile(manifestPath);

        Assert.That(File.Exists(manifestPath), Is.True);
        var loadedManifest = MapManifest.LoadFromFile(manifestPath);
        Assert.That(loadedManifest, Is.Not.Null);
        Assert.That(loadedManifest!.Files.Count, Is.EqualTo(manifest.Files.Count));
        Assert.That(loadedManifest.Tags, Contains.Item("AssetPack"));
    }

    [Test]
    public void ShardingAlgorithm_PercentageDistribution()
    {
        string seeder100 = "seeder_full_100";
        string seeder50 = "seeder_half_50";
        string seeder1 = "seeder_tiny_1";

        int count100 = 0;
        int count50 = 0;
        int count1 = 0;

        int totalSamples = 1000;
        for (int i = 0; i < totalSamples; i++)
        {
            string fakeHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

            if (DistributionSharding.SeederAcceptsHash(seeder100, 100, fakeHash))
            {
                count100++;
            }

            if (DistributionSharding.SeederAcceptsHash(seeder50, 50, fakeHash))
            {
                count50++;
            }

            if (DistributionSharding.SeederAcceptsHash(seeder1, 1, fakeHash))
            {
                count1++;
            }
        }

        Assert.That(count100, Is.EqualTo(totalSamples));
        Assert.That(count50, Is.InRange(400, 600));
        Assert.That(count1, Is.InRange(1, 35));
    }

    [Test]
    public void ContentAddressableStorage_Deduplication_And_HeaderMerging()
    {
        var storage = new ContentAddressableStorage(_testDirectory);
        byte[] assetBytes = new byte[1024];
        new Random(42).NextBytes(assetBytes);

        var (authorPriv, authorPub) = AuthorSignatureHelper.GenerateKeyPair();
        var (otherPriv, otherPub) = AuthorSignatureHelper.GenerateKeyPair();

        string initialMetadata = "{\"tags\":[\"rock\",\"nature\"],\"brightness\":0.5}";
        var firstStore = storage.StoreAsset(assetBytes, ".glb", initialMetadata, authorPub, AuthorSignatureHelper.SignMessage(authorPriv, ContentAddressableStorage.NormalizeBlake3Hash(RealmMetadataHelper.ComputeBlake3(assetBytes, ".glb"))));
        Assert.That(firstStore.Success, Is.True);
        Assert.That(firstStore.Deduplicated, Is.False);

        string unauthorizedMetadata = "{\"tags\":[\"foliage\"],\"brightness\":0.9}";
        var secondStore = storage.StoreAsset(assetBytes, ".glb", unauthorizedMetadata, otherPub, AuthorSignatureHelper.SignMessage(otherPriv, firstStore.Blake3Hash));
        Assert.That(secondStore.Success, Is.True);
        Assert.That(secondStore.Deduplicated, Is.True);
        Assert.That(secondStore.Merged, Is.True);

        string? mergedMeta = storage.GetAssetMetadata(firstStore.Blake3Hash);
        Assert.That(mergedMeta, Is.Not.Null);
        Assert.That(mergedMeta!, Does.Contain("rock"));
        Assert.That(mergedMeta, Does.Contain("nature"));
        Assert.That(mergedMeta, Does.Contain("foliage"));
        Assert.That(mergedMeta, Does.Contain("0.5"));

        string authorizedUpdate = "{\"tags\":[\"custom\"],\"brightness\":1.0}";
        var thirdStore = storage.StoreAsset(assetBytes, ".glb", authorizedUpdate, authorPub, AuthorSignatureHelper.SignMessage(authorPriv, firstStore.Blake3Hash));
        Assert.That(thirdStore.Success, Is.True);
        Assert.That(thirdStore.Deduplicated, Is.True);
        Assert.That(thirdStore.Merged, Is.True);

        string? finalMeta = storage.GetAssetMetadata(firstStore.Blake3Hash);
        Assert.That(finalMeta, Is.Not.Null);
        Assert.That(finalMeta!, Does.Contain("custom"));
        Assert.That(finalMeta, Does.Contain("1"));
        Assert.That(finalMeta, Does.Not.Contain("rock"));
    }

    [Test]
    public void Storage_RejectsAssetsOver15MB()
    {
        var storage = new ContentAddressableStorage(_testDirectory);
        byte[] oversizedBytes = new byte[ContentAddressableStorage.MaximumAssetSizeBytes + 10];

        var result = storage.StoreAsset(oversizedBytes, ".glb");
        Assert.That(result.Success, Is.False);
        Assert.That(result.Message, Does.Contain("maximum size limit"));
    }

    [Test]
    public async Task Isolated_RanimUpload_MatchesBlake3()
    {
        string filePath = @"C:\temp\asset_package\Animations\180 Turn W Briefcase - Female 180 Turn With Briefcase.ranim";
        if (!File.Exists(filePath))
        {
            Assert.Ignore("File not found");
        }

        byte[] originalBytes = File.ReadAllBytes(filePath);
        string originalBlake3 = RealmMetadataHelper.ComputeBlake3(originalBytes, ".ranim");
        string? metadata = RealmMetadataHelper.ExtractMetadata(filePath);

        var serverCas = new ContentAddressableStorage(Path.Combine(_testDirectory, "server_cas"));
        var server = new DistributionServer(serverCas, "test_seeder", 100);
        int port = 50555;
        server.Start(port);

        try
        {
            var client = new DistributionClient($"http://127.0.0.1:{port}");
            var uploadWithMeta = await client.UploadAssetAsync($"http://127.0.0.1:{port}", originalBytes, ".ranim", metadata);

            Assert.That(uploadWithMeta.Success, Is.True);
            Assert.That(uploadWithMeta.Blake3Hash, Is.EqualTo(ContentAddressableStorage.NormalizeBlake3Hash(originalBlake3)));
            Assert.That(serverCas.HasAsset(originalBlake3), Is.True);
        }
        finally
        {
            server.Stop();
        }
    }
}
