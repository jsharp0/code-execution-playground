# MCP Server (ASP.NET Core)

This project is a minimal HTTP-based MCP server built with the official C# SDK.

## Run

```bash
dotnet run --project src/McpServer
```

By default ASP.NET Core listens on `http://localhost:5000`. You can override this by setting `ASPNETCORE_URLS`:

```bash
ASPNETCORE_URLS=http://localhost:3001 dotnet run --project src/McpServer
```

The MCP endpoint is exposed at:

```
http://localhost:5000/mcp
```

## Tooling

All tools are registered via `[McpServerTool]` attributes and are discoverable with `listTools`:

- `GetTimeAsync`
- `FetchWeatherAsync`
- `ComputeStatsAsync`
- `ListFilesAsync`
- `GenerateGuidAsync`
- `GetHostInfoAsync`
- `GetEnvironmentKeysAsync`
- `CalculateSha256Async`
- `GetRandomNumberAsync`
- `DelayAsync`
- `EchoAsync`

## Example MCP inspection

Use the MCP inspector to explore tools and call them:

```bash
npx @modelcontextprotocol/inspector http://localhost:5000/mcp
```
