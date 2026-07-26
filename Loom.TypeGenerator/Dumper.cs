using System.Text.Json;
using Loom.TypeGenerator.ApiTypes;

namespace Loom.TypeGenerator;

internal static class Dumper
{
    private const string BaseUrl = "https://raw.githubusercontent.com/MaximumADHD/Roblox-Client-Tracker/roblox/";
    private static readonly HttpClient _client = new();

    public static async Task<Dump> GetDump()
    {
        var body = await Request("Mini-API-Dump.json");
        Log.Info("acquired API dump");

        return JsonSerializer.Deserialize<Dump>(body)!;
    }

    public static async Task<ReflectionMetadataReader> GetReflectionMetadata()
    {
        var body = await Request("ReflectionMetadata.xml");
        Log.Info("acquired reflection metadata");

        return new ReflectionMetadataReader(body);
    }

    private static async Task<string> Request(string endpoint)
    {
        try
        {
            var response = await _client.GetAsync(BaseUrl + endpoint);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }
        catch (HttpRequestException e)
        {
            Log.Fatal($"request error: {e.Message}");
        }
        catch (Exception e)
        {
            Log.Fatal($"unexpected error: {e.Message}");
        }

        return null!;
    }
}