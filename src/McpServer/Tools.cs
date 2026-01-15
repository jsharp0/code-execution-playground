using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace McpServer;

[McpServerToolType]
public static class UtilityTools
{
    [McpServerTool, Description("Gets the current UTC time in ISO 8601 format.")]
    public static Task<string> GetTimeAsync() =>
        Task.FromResult(DateTimeOffset.UtcNow.ToString("O"));

    [McpServerTool, Description("Fetches current temperature and wind speed from Open-Meteo for the given coordinates.")]
    public static async Task<WeatherResult> FetchWeatherAsync(
        HttpClient httpClient,
        [Description("Latitude in decimal degrees")] double latitude,
        [Description("Longitude in decimal degrees")] double longitude,
        CancellationToken cancellationToken)
    {
        var url = $"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}&current=temperature_2m,wind_speed_10m";
        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        double? temperature = null;
        double? windSpeed = null;
        string timezone = "UTC";

        if (document.RootElement.TryGetProperty("timezone", out var timezoneElement))
        {
            timezone = timezoneElement.GetString() ?? timezone;
        }

        if (document.RootElement.TryGetProperty("current", out var current))
        {
            if (current.TryGetProperty("temperature_2m", out var temperatureElement))
            {
                temperature = temperatureElement.GetDouble();
            }

            if (current.TryGetProperty("wind_speed_10m", out var windElement))
            {
                windSpeed = windElement.GetDouble();
            }
        }

        return new WeatherResult(temperature, windSpeed, timezone);
    }

    [McpServerTool, Description("Computes basic statistics for a list of numbers.")]
    public static Task<StatsResult> ComputeStatsAsync(
        [Description("Numbers to analyze")] double[] numbers)
    {
        if (numbers is null)
        {
            throw new ArgumentNullException(nameof(numbers));
        }

        if (numbers.Length == 0)
        {
            return Task.FromResult(new StatsResult(0, null, null, null, 0));
        }

        var min = numbers.Min();
        var max = numbers.Max();
        var sum = numbers.Sum();
        var avg = numbers.Average();

        return Task.FromResult(new StatsResult(numbers.Length, min, max, avg, sum));
    }

    [McpServerTool, Description("Lists files in a directory, optionally limiting the number of results.")]
    public static Task<string[]> ListFilesAsync(
        [Description("Directory to list files from")] string directory,
        [Description("Maximum number of files to return")] int maxResults = 50)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = ".";
        }

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Directory not found: {directory}");
        }

        var results = Directory.EnumerateFiles(directory)
            .Take(Math.Max(0, maxResults))
            .ToArray();

        return Task.FromResult(results);
    }

    [McpServerTool, Description("Generates a new GUID.")]
    public static Task<string> GenerateGuidAsync() =>
        Task.FromResult(Guid.NewGuid().ToString());

    [McpServerTool, Description("Gets host and runtime information about the current process.")]
    public static Task<HostInfoResult> GetHostInfoAsync()
    {
        var result = new HostInfoResult(
            Environment.MachineName,
            RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.ProcessorCount,
            Process.GetCurrentProcess().Id);

        return Task.FromResult(result);
    }

    [McpServerTool, Description("Returns environment variable keys visible to the process.")]
    public static Task<string[]> GetEnvironmentKeysAsync()
    {
        var keys = Environment.GetEnvironmentVariables()
            .Keys
            .Cast<object>()
            .Select(key => key.ToString() ?? string.Empty)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult(keys);
    }

    [McpServerTool, Description("Computes a SHA-256 hash for the provided text.")]
    public static Task<HashResult> CalculateSha256Async(
        [Description("Text to hash")] string input)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = sha.ComputeHash(bytes);
        var hex = Convert.ToHexString(hash).ToLowerInvariant();

        return Task.FromResult(new HashResult(hex));
    }

    [McpServerTool, Description("Returns a random integer within the provided inclusive range.")]
    public static Task<int> GetRandomNumberAsync(
        [Description("Minimum value (inclusive)")] int minValue,
        [Description("Maximum value (inclusive)")] int maxValue)
    {
        if (minValue > maxValue)
        {
            throw new ArgumentException("Minimum value must be less than or equal to maximum value.");
        }

        var result = Random.Shared.Next(minValue, maxValue + 1);
        return Task.FromResult(result);
    }

    [McpServerTool, Description("Delays for the specified number of milliseconds.")]
    public static async Task<string> DelayAsync(
        [Description("Delay duration in milliseconds")] int milliseconds,
        CancellationToken cancellationToken)
    {
        if (milliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(milliseconds));
        }

        await Task.Delay(milliseconds, cancellationToken);
        return $"Delayed for {milliseconds} ms.";
    }

    [McpServerTool, Description("Echoes the provided message.")]
    public static Task<string> EchoAsync(
        [Description("Message to echo")] string message) =>
        Task.FromResult(message);
}

public record WeatherResult(double? TemperatureC, double? WindSpeedKph, string Timezone);

public record StatsResult(int Count, double? Min, double? Max, double? Average, double Sum);

public record HostInfoResult(
    string MachineName,
    string OSDescription,
    string FrameworkDescription,
    string ProcessArchitecture,
    int ProcessorCount,
    int ProcessId);

public record HashResult(string Sha256);
