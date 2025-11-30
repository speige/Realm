using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Realm.TranslationTool
{
	class Program
	{
		private static readonly string BaseModel = "gemma4:12b-it-q4_K_M";
		private static readonly Dictionary<string, string> LanguageLocaleMap = new()
		{
			{ "Español", "es" },
			{ "Français", "fr" },
			{ "Deutsch", "de" },
			{ "Português", "pt" },
			{ "Русский", "ru" },
			{ "中文", "zh" },
			{ "日本語", "ja" },
			{ "العربية", "ar" },
			{ "हिन्दी", "hi" }
		};

		static void Main(string[] args)
		{
			string workingDir = Directory.GetCurrentDirectory();
			string godotLocaleDir = Path.GetFullPath(Path.Combine(workingDir, "Realm.Godot", "locale"));
			if (!Directory.Exists(godotLocaleDir))
			{
				godotLocaleDir = Path.GetFullPath(Path.Combine(workingDir, "..", "Realm.Godot", "locale"));
			}

			if (!Directory.Exists(godotLocaleDir))
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"Error: Locale directory not found at: {godotLocaleDir}");
				Console.ResetColor();
				return;
			}

			string enFilePath = Path.Combine(godotLocaleDir, "en.json");
			if (!File.Exists(enFilePath))
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"Error: English source file en.json not found at: {enFilePath}");
				Console.ResetColor();
				return;
			}

			string cacheDir = Path.Combine(workingDir, ".translation_cache");
			if (!Directory.Exists(cacheDir))
			{
				Directory.CreateDirectory(cacheDir);
			}

			string enJson = File.ReadAllText(enFilePath, Encoding.UTF8);
			var sourceStrings = JsonSerializer.Deserialize<Dictionary<string, string>>(enJson);
			if (sourceStrings == null)
			{
				Console.WriteLine("Error: Failed to deserialize en.json");
				return;
			}

			foreach (var kvp in LanguageLocaleMap)
			{
				string languageName = kvp.Key;
				string locale = kvp.Value;

				Console.ForegroundColor = ConsoleColor.Cyan;
				Console.WriteLine($"--- Processing Language: {languageName} ({locale}) ---");
				Console.ResetColor();

				string outFilePath = Path.Combine(godotLocaleDir, $"{locale}.json");
				Dictionary<string, string> targetStrings = new();
				if (File.Exists(outFilePath))
				{
					try
					{
						string existingContent = File.ReadAllText(outFilePath, Encoding.UTF8);
						var existingDict = JsonSerializer.Deserialize<Dictionary<string, string>>(existingContent);
						if (existingDict != null)
						{
							targetStrings = existingDict;
						}
					}
					catch
					{
					}
				}

				bool modified = false;
				foreach (var sourceKvp in sourceStrings)
				{
					string key = sourceKvp.Key;
					string englishText = sourceKvp.Value;

					if (targetStrings.ContainsKey(key) && !string.IsNullOrWhiteSpace(targetStrings[key]))
					{
						continue;
					}

					string translatedText = TranslateLine(englishText, languageName, locale, "RTS game user interface string", cacheDir);
					targetStrings[key] = translatedText;
					modified = true;
				}

				if (modified || !File.Exists(outFilePath))
				{
					var options = new JsonSerializerOptions { WriteIndented = true };
					string outputJson = JsonSerializer.Serialize(targetStrings, options);
					File.WriteAllText(outFilePath, outputJson, Encoding.UTF8);
					Console.ForegroundColor = ConsoleColor.Green;
					Console.WriteLine($"Saved: {locale}.json");
					Console.ResetColor();
				}
			}

			Console.ForegroundColor = ConsoleColor.Green;
			Console.WriteLine("\nAll translations completed successfully.");
			Console.ResetColor();
		}

		private static string TranslateLine(string text, string targetLanguage, string locale, string context, string cacheDirectory)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				return text;
			}

			string stringHash = GetStringHash($"{text}-{context}");
			string cacheFile = Path.Combine(cacheDirectory, $"{locale}_{stringHash}.json");

			if (!File.Exists(cacheFile))
			{
				string preview = text.Length > 40 ? text.Substring(0, 40) + "..." : text;
				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.WriteLine($"Translating: '{preview}' to {targetLanguage}...");
				Console.ResetColor();

				string prompt = $"Translate the following text from English to {targetLanguage}.\n" +
								$"Maintain the correct context for a real-time strategy (RTS) video game.\n" +
								$"Output ONLY the translated text, do not include quotes, explanations, or any preamble.\n" +
								$"Context: {context}\n" +
								$"Text: \"{text}\"";

				var requestObj = new
				{
					model = BaseModel,
					prompt = prompt,
					stream = false
				};

				string jsonRequest = JsonSerializer.Serialize(requestObj);
				string response = RunOllama(jsonRequest);
				string translatedText = response.Trim().Replace("\"", "");

				var cacheObj = new CacheEntry
				{
					Original = text,
					TranslatedText = translatedText,
					Locale = locale,
					Context = context,
					Timestamp = DateTime.Now.ToString()
				};

				string cacheJson = JsonSerializer.Serialize(cacheObj, new JsonSerializerOptions { WriteIndented = true });
				File.WriteAllText(cacheFile, cacheJson, Encoding.UTF8);
			}

			try
			{
				string cacheContent = File.ReadAllText(cacheFile, Encoding.UTF8);
				var cacheEntry = JsonSerializer.Deserialize<CacheEntry>(cacheContent);
				if (cacheEntry != null)
				{
					if (cacheEntry.Original.Trim().Replace(" ", "") != text.Trim().Replace(" ", ""))
					{
						throw new Exception("Source mismatch");
					}

					return cacheEntry.TranslatedText;
				}
			}
			catch
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"Warning: Cache file {cacheFile} is corrupted. Deleting.");
				Console.ResetColor();
				try
				{
					File.Delete(cacheFile);
				}
				catch { }
			}

			return text;
		}

		private static string GetStringHash(string input)
		{
			using (var sha256 = SHA256.Create())
			{
				byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
				var builder = new StringBuilder();
				foreach (var b in bytes)
				{
					builder.Append(b.ToString("X2"));
				}
				return builder.ToString();
			}
		}

		private static string RunOllama(string jsonRequest)
		{
			var psi = new ProcessStartInfo
			{
				FileName = "ollama",
				Arguments = $"run --think=false {BaseModel}",
				RedirectStandardInput = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true
			};

			using (var process = Process.Start(psi))
			{
				if (process == null)
				{
					return "";
				}

				using (var writer = process.StandardInput)
				{
					writer.Write(jsonRequest);
				}

				string output = process.StandardOutput.ReadToEnd();
				process.WaitForExit();

				return output;
			}
		}

		private class CacheEntry
		{
			public string Original { get; set; } = "";
			public string TranslatedText { get; set; } = "";
			public string Locale { get; set; } = "";
			public string Context { get; set; } = "";
			public string Timestamp { get; set; } = "";
		}
	}
}
