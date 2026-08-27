using System;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

public static class MapJsonFormatter
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		IndentCharacter = '\t',
		IndentSize = 1,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		NewLine = "\n"
	};

	private static readonly JsonDocumentOptions DocumentOptions = new()
	{
		AllowTrailingCommas = true,
		CommentHandling = JsonCommentHandling.Skip
	};

	public static JsonNode? SortKeysRecursively(JsonNode? node)
	{
		if (node is JsonObject obj)
		{
			var sortedObj = new JsonObject();
			var keys = obj.Select(kvp => kvp.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
			foreach (var key in keys)
			{
				var value = obj[key];
				sortedObj[key] = value != null ? SortKeysRecursively(value.DeepClone()) : null;
			}
			return sortedObj;
		}
		else if (node is JsonArray arr)
		{
			var newArr = new JsonArray();
			foreach (var item in arr)
			{
				newArr.Add(item != null ? SortKeysRecursively(item.DeepClone()) : null);
			}
			return newArr;
		}

		return node?.DeepClone();
	}

	public static string FormatJson(string jsonText)
	{
		if (string.IsNullOrWhiteSpace(jsonText))
		{
			return "{\n}\n";
		}

		var parsed = JsonNode.Parse(jsonText, documentOptions: DocumentOptions);
		if (parsed == null)
		{
			return "{\n}\n";
		}

		var sorted = SortKeysRecursively(parsed);
		string result = sorted != null ? sorted.ToJsonString(JsonOptions) : "{\n}";
		return result.TrimEnd() + "\n";
	}

	public static string FormatNode(JsonNode node)
	{
		if (node == null)
		{
			return "{\n}\n";
		}

		var sorted = SortKeysRecursively(node);
		string result = sorted != null ? sorted.ToJsonString(JsonOptions) : "{\n}";
		return result.TrimEnd() + "\n";
	}

	public static void SaveFormattedJson(string filePath, string jsonText)
	{
		string formatted = FormatJson(jsonText);
		string directory = Path.GetDirectoryName(filePath);
		if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
		{
			Directory.CreateDirectory(directory);
		}
		EditorService.LastInternalSaveTimeUtc = DateTime.UtcNow;
		File.WriteAllText(filePath, formatted);
	}

	public static void SaveFormattedJson(string filePath, JsonNode node)
	{
		string formatted = FormatNode(node);
		string directory = Path.GetDirectoryName(filePath);
		if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
		{
			Directory.CreateDirectory(directory);
		}
		EditorService.LastInternalSaveTimeUtc = DateTime.UtcNow;
		File.WriteAllText(filePath, formatted);
	}
}
