using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using COA.Mcp.Client;
using COA.Mcp.Protocol;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables();

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod()));

var app = builder.Build();

app.UseCors();

var logger = app.Logger;

var openAiSection = app.Configuration.GetSection("OpenAI");
var openAiApiKey = openAiSection["ApiKey"];
var openAiBaseUrl = openAiSection["BaseUrl"] ?? "https://api.openai.com/v1";
var openAiModel = openAiSection["Model"] ?? "gpt-4o-mini";

var mcpSection = app.Configuration.GetSection("Mcp");
var mcpServerUrl = mcpSection["ServerUrl"];
var mcpApiKey = mcpSection["ApiKey"];

if (string.IsNullOrWhiteSpace(openAiApiKey))
{
    throw new InvalidOperationException("OpenAI API key is missing. Set OpenAI__ApiKey in appsettings.json or as an environment variable.");
}

if (string.IsNullOrWhiteSpace(mcpServerUrl))
{
    throw new InvalidOperationException("MCP server URL is missing. Set Mcp__ServerUrl in appsettings.json or as an environment variable.");
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

app.MapGet("/", () => Results.Ok(new { status = "ok" }));

app.MapPost("/chat", async (ChatRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest(new { message = "Message is required." });
    }

    var conversation = new List<ChatMessageRequest>
    {
        new()
        {
            Role = "system",
            Content = "You are a helpful assistant. Use the available tools when needed."
        },
        new()
        {
            Role = "user",
            Content = request.Message
        }
    };

    try
    {
        var reply = await RunToolLoopAsync(
            httpClient,
            openAiModel,
            tools,
            conversation,
            mcpClient,
            logger);

        return Results.Ok(new ChatResponse(reply));
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to process chat request.");
        return Results.Problem("Failed to process chat request.");
    }
});

await app.RunAsync();

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
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

sealed class ChatRequest
{
    public string? Message { get; init; }
}

sealed class ChatResponse
{
    public ChatResponse(string reply)
    {
        Reply = reply;
    }

    public string Reply { get; }
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
