using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace Atlas.Cli;

/// <summary>
/// Simple config stored in ~/.atlas/config.json
/// </summary>
public class AtlasConfig
{
    public string BaseUrl { get; set; } = "http://localhost:5000";
    public string? ApiKey { get; set; }
    public string? JwtToken { get; set; }
}

public static class ConfigStore
{
    private static readonly string ConfigDir  = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".atlas");
    private static readonly string ConfigFile = Path.Combine(ConfigDir, "config.json");

    public static AtlasConfig Load()
    {
        if (!File.Exists(ConfigFile)) return new AtlasConfig();
        try
        {
            var json = File.ReadAllText(ConfigFile);
            return JsonSerializer.Deserialize<AtlasConfig>(json) ?? new AtlasConfig();
        }
        catch { return new AtlasConfig(); }
    }

    public static void Save(AtlasConfig config)
    {
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(ConfigFile, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public static class ApiClient
{
    public static HttpClient Create(AtlasConfig config)
    {
        var client = new HttpClient { BaseAddress = new Uri(config.BaseUrl) };
        if (!string.IsNullOrWhiteSpace(config.ApiKey))
            client.DefaultRequestHeaders.Add("X-Api-Key", config.ApiKey);
        else if (!string.IsNullOrWhiteSpace(config.JwtToken))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.JwtToken);
        return client;
    }
}
