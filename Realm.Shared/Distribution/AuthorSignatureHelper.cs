using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NSec.Cryptography;

namespace Realm.Shared.Distribution;

public static class AuthorSignatureHelper
{
    private static readonly SignatureAlgorithm Algorithm = SignatureAlgorithm.Ed25519;

    public static (string PrivateKeyBase64, string PublicKeyBase64) GenerateKeyPair()
    {
        var creationParameters = new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport
        };

        using var key = Key.Create(Algorithm, creationParameters);
        byte[] privateBytes = key.Export(KeyBlobFormat.RawPrivateKey);
        byte[] publicBytes = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);

        return (Convert.ToBase64String(privateBytes), Convert.ToBase64String(publicBytes));
    }

    public static string SignMessage(string privateKeyBase64, string message)
    {
        byte[] privateBytes = Convert.FromBase64String(privateKeyBase64);
        using var key = Key.Import(Algorithm, privateBytes, KeyBlobFormat.RawPrivateKey);
        byte[] messageBytes = Encoding.UTF8.GetBytes(message);
        byte[] signatureBytes = Algorithm.Sign(key, messageBytes);
        return Convert.ToBase64String(signatureBytes);
    }

    public static bool VerifySignature(string publicKeyBase64, string message, string signatureBase64)
    {
        try
        {
            byte[] publicBytes = Convert.FromBase64String(publicKeyBase64);
            byte[] signatureBytes = Convert.FromBase64String(signatureBase64);
            var publicKey = PublicKey.Import(Algorithm, publicBytes, KeyBlobFormat.RawPublicKey);
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);
            return Algorithm.Verify(publicKey, messageBytes, signatureBytes);
        }
        catch
        {
            return false;
        }
    }

    public static string MergeMetadataHeaders(string? existingMetadataJson, string incomingMetadataJson, bool isAuthorizedOverwrite)
    {
        if (string.IsNullOrWhiteSpace(existingMetadataJson))
        {
            return incomingMetadataJson;
        }

        if (isAuthorizedOverwrite)
        {
            return incomingMetadataJson;
        }

        JsonNode? existingNode;
        JsonNode? incomingNode;

        try
        {
            existingNode = JsonNode.Parse(existingMetadataJson);
            incomingNode = JsonNode.Parse(incomingMetadataJson);
        }
        catch
        {
            return existingMetadataJson;
        }

        if (existingNode is not JsonObject existingObject || incomingNode is not JsonObject incomingObject)
        {
            return existingMetadataJson;
        }

        foreach (var property in incomingObject)
        {
            string propertyName = property.Key;
            JsonNode? incomingValue = property.Value;

            if (incomingValue == null)
            {
                continue;
            }

            if (!existingObject.ContainsKey(propertyName) || existingObject[propertyName] == null)
            {
                existingObject[propertyName] = incomingValue.DeepClone();
                continue;
            }

            JsonNode? existingValue = existingObject[propertyName];

            if (existingValue is JsonArray existingArray && incomingValue is JsonArray incomingArray)
            {
                var existingItemsSet = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in existingArray)
                {
                    if (item != null)
                    {
                        existingItemsSet.Add(item.ToJsonString());
                    }
                }

                foreach (var item in incomingArray)
                {
                    if (item != null)
                    {
                        string itemString = item.ToJsonString();
                        if (existingItemsSet.Add(itemString))
                        {
                            existingArray.Add(item.DeepClone());
                        }
                    }
                }
            }
        }

        return existingObject.ToJsonString();
    }
}
