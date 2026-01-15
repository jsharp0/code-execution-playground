using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using COA.Mcp.Client;
using COA.Mcp.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
var logger = loggerFactory.CreateLogger("McpClientHost");

var openAiSection = configuration.GetSection("OpenAI");
var openAiApiKey = openAiSection["ApiKey"];
var openAiBaseUrl = openAiSection["BaseUrl"] ?? "https://api.openai.com/v1";
var openAiModel = openAiSection["Model"] ?? "gpt-4o-mini";

var mcpSection = configuration.GetSection("Mcp");
var mcpServerUrl = mcpSection["ServerUrl"];
var mcpApiKey = mcpSection["ApiKey"];

if (string.IsNullOrWhiteSpace(openAiApiKey))
{
    logger.LogError("OpenAI API key is missing. Set OpenAI__ApiKey in appsettings.json or as an environment variable.");
    return;
}

if (string.IsNullOrWhiteSpace(mcpServerUrl))
{
    logger.LogError("MCP server URL is missing. Set Mcp__ServerUrl in appsettings.json or as an environment variable.");
    return;
}

var clientBuilder = McpClientBuilder
    .Create(mcpServerUrl)
    .WithTimeout(TimeSpan.FromSeconds(30))
    .WithRetry(maxAttempts: 3, delayMs: 1000);

if (!string.IsNullOrWhiteSpace(mcpApiKey))
{
    clientBuilder = clientBuilder.WithApiKey(mcpApiKey);
}

var mcpClient = await clientBuilder.BuildAndInitializeAsync();
var listToolsResult = await mcpClient.ListToolsAsync();

logger.LogInformation("Connected to MCP server {ServerUrl}. Tools available: {ToolCount}", mcpServerUrl, listToolsResult.Tools.Count);
foreach (var tool in listToolsResult.Tools)
{
    logger.LogInformation("Tool: {ToolName} - {ToolDescription}", tool.Name, tool.Description);
}

using var httpClient = new HttpClient
{
    BaseAddress = new Uri(openAiBaseUrl)
};
httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", openAiApiKey);

var tools = listToolsResult.Tools
    .Select(tool => new OpenAiToolDefinition
    {
        Type = "function",
        Function = new OpenAiFunctionDefinition
        {
            Name = tool.Name,
            Description = tool.Description,
            Parameters = ToJsonElement(tool.InputSchema)
        }
    })
    .ToList();

var conversation = new List<ChatMessageRequest>
{
    new()
    {
        Role = "system",
        Content = "You are a helpful assistant. Use the available tools when needed."
    }
};

logger.LogInformation("Enter a prompt (empty line to exit).");

while (true)
{
    Console.Write("\n> ");
    var userInput = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(userInput))
    {
        break;
    }

    conversation.Add(new ChatMessageRequest
    {
        Role = "user",
        Content = userInput
    });

    var finalResponse = await RunToolLoopAsync(
        httpClient,
        openAiModel,
        tools,
        conversation,
        mcpClient,
        logger);

    Console.WriteLine($"\n{finalResponse}\n");
}

static async Task<string> RunToolLoopAsync(
    HttpClient httpClient,
    string model,
    IReadOnlyList<OpenAiToolDefinition> tools,
    List<ChatMessageRequest> conversation,
    COA.Mcp.Client.Interfaces.IMcpClient mcpClient,
    ILogger logger)
{
    while (true)
    {
        var request = new ChatCompletionRequest
        {
            Model = model,
            Messages = conversation,
            Tools = tools
        };

        using var response = await httpClient.PostAsJsonAsync("chat/completions", request, JsonOptions());
        response.EnsureSuccessStatusCode();

        var responsePayload = await response.Content.ReadAsStringAsync();
        var completion = JsonSerializer.Deserialize<ChatCompletionResponse>(responsePayload, JsonOptions())
                         ?? throw new InvalidOperationException("Unable to parse OpenAI response.");

        var message = completion.Choices.FirstOrDefault()?.Message
                      ?? throw new InvalidOperationException("OpenAI response missing a message.");

        if (message.ToolCalls == null || message.ToolCalls.Count == 0)
        {
            var content = message.Content ?? string.Empty;
            conversation.Add(new ChatMessageRequest
            {
                Role = "assistant",
                Content = content
            });
            return content;
        }

        conversation.Add(new ChatMessageRequest
        {
            Role = "assistant",
            Content = message.Content,
            ToolCalls = message.ToolCalls
        });

        foreach (var toolCall in message.ToolCalls)
        {
            var toolArguments = ParseToolArguments(toolCall.Function.Arguments);
            var toolResult = await mcpClient.CallToolAsync(toolCall.Function.Name, toolArguments);
            var toolText = ExtractToolText(toolResult);

            logger.LogInformation("Tool {ToolName} executed. Error: {IsError}", toolCall.Function.Name, toolResult.IsError);

            conversation.Add(new ChatMessageRequest
            {
                Role = "tool",
                ToolCallId = toolCall.Id,
                Content = toolText
            });
        }
    }
}

static JsonElement ToJsonElement(object? schema)
{
    if (schema is JsonElement element)
    {
        return element;
    }

    if (schema is JsonDocument document)
    {
        return document.RootElement.Clone();
    }

    if (schema is null)
    {
        return JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new Dictionary<string, object?>()
        });
    }

    return JsonSerializer.SerializeToElement(schema);
}

static JsonElement ParseToolArguments(string? arguments)
{
    if (string.IsNullOrWhiteSpace(arguments))
    {
        return JsonSerializer.SerializeToElement(new Dictionary<string, object?>());
    }

    using var document = JsonDocument.Parse(arguments);
    return document.RootElement.Clone();
}

static string ExtractToolText(CallToolResult toolResult)
{
    if (toolResult.Content == null || toolResult.Content.Count == 0)
    {
        return toolResult.IsError ? "Tool returned an error with no content." : "";
    }

    var builder = new StringBuilder();
    foreach (var content in toolResult.Content)
    {
        if (!string.IsNullOrWhiteSpace(content.Text))
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(content.Text);
        }
    }

    return builder.ToString();
}

static JsonSerializerOptions JsonOptions()
{
    return new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}

sealed class ChatCompletionRequest
{
    public required string Model { get; init; }

    public required List<ChatMessageRequest> Messages { get; init; }

    public IReadOnlyList<OpenAiToolDefinition>? Tools { get; init; }
}

sealed class ChatMessageRequest
{
    public required string Role { get; init; }

    public string? Content { get; init; }

    [JsonPropertyName("tool_calls")]
    public List<OpenAiToolCall>? ToolCalls { get; init; }

    [JsonPropertyName("tool_call_id")]
    public string? ToolCallId { get; init; }
}

sealed class OpenAiToolDefinition
{
    public required string Type { get; init; }

    public required OpenAiFunctionDefinition Function { get; init; }
}

sealed class OpenAiFunctionDefinition
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    public JsonElement Parameters { get; init; }
}

sealed class ChatCompletionResponse
{
    public required List<ChatChoice> Choices { get; init; }
}

sealed class ChatChoice
{
    public required ChatMessageResponse Message { get; init; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; init; }
}

sealed class ChatMessageResponse
{
    public required string Role { get; init; }

    public string? Content { get; init; }

    [JsonPropertyName("tool_calls")]
    public List<OpenAiToolCall>? ToolCalls { get; init; }
}

sealed class OpenAiToolCall
{
    public required string Id { get; init; }

    public required string Type { get; init; }

    public required OpenAiToolFunction Function { get; init; }
}

sealed class OpenAiToolFunction
{
    public required string Name { get; init; }

    public required string Arguments { get; init; }
}
