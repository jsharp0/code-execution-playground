# MCP Client Host

Console app that connects to an MCP server, lists tools, and orchestrates OpenAI tool calls.

## Configuration

`appsettings.json` (or environment variables) controls the MCP server and OpenAI settings.

| Setting | Environment variable | Description |
| --- | --- | --- |
| `OpenAI:ApiKey` | `OpenAI__ApiKey` | OpenAI API key. |
| `OpenAI:BaseUrl` | `OpenAI__BaseUrl` | OpenAI API base URL (default: `https://api.openai.com/v1`). |
| `OpenAI:Model` | `OpenAI__Model` | OpenAI model ID (default: `gpt-4o-mini`). |
| `Mcp:ServerUrl` | `Mcp__ServerUrl` | MCP server base URL. |
| `Mcp:ApiKey` | `Mcp__ApiKey` | Optional MCP API key. |

## Run

```bash
dotnet run --project src/McpClientHost/McpClientHost.csproj
```

Paste a prompt, and the host will route tool calls to the MCP server until the model returns a final answer.
