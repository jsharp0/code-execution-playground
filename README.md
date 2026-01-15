# Code Execution Playground

## Architecture overview

This repository is a three-part playground that wires an MCP tool server into an OpenAI-powered client host (with an HTTP API), with an optional React UI on top.

```
┌──────────────────┐       MCP calls       ┌────────────────────┐
│  MCP Client Host │──────────────────────▶│     MCP Server     │
│ (src/McpClientHost) │                    │ (src/McpServer)    │
└──────────────────┘                       └────────────────────┘
        ▲
        │ OpenAI chat + tool calls
        ▼
┌──────────────────┐
│   OpenAI API     │
└──────────────────┘

┌──────────────────┐        HTTP /chat        ┌──────────────────────┐
│   React Chat UI  │─────────────────────────▶│  MCP Client Host API │
│   (web/chat-ui)  │                          │ (src/McpClientHost)  │
└──────────────────┘                          └──────────────────────┘
```

> **Note:** The MCP client host in this repo is an ASP.NET Core minimal API that already exposes `POST /chat`, so the React UI can point at it directly.

## Service layout

| Path | Role | Notes |
| --- | --- | --- |
| `src/McpServer` | MCP tool server | ASP.NET Core server exposing MCP at `/mcp`. |
| `src/McpClientHost` | MCP client host | ASP.NET Core API that calls OpenAI and MCP tools (exposes `POST /chat`). |
| `web/chat-ui` | React UI | Vite + React client that `POST`s to `/chat`. |

## Local ports

| Service | Default port | Config |
| --- | --- | --- |
| MCP Server | `5000` | `ASPNETCORE_URLS` or `launchSettings.json`. |
| MCP endpoint | `http://localhost:5000/mcp` | Hard-coded route in the server. |
| React UI (Vite dev) | `5173` | Vite default, configurable in `vite.config.ts`. |
| Client Host API | `5000`/`5001` | ASP.NET Core default; override with `ASPNETCORE_URLS` or `--urls` to avoid port conflicts. |

## Environment variables

### MCP client host (`src/McpClientHost`)

| Setting | Environment variable | Description |
| --- | --- | --- |
| OpenAI API key | `OpenAI__ApiKey` | **Required.** Your OpenAI API key. |
| OpenAI base URL | `OpenAI__BaseUrl` | Optional override (default: `https://api.openai.com/v1`). |
| OpenAI model | `OpenAI__Model` | Optional override (default: `gpt-4o-mini`). |
| MCP server URL | `Mcp__ServerUrl` | **Required.** Example: `http://localhost:5000`. |
| MCP API key | `Mcp__ApiKey` | Optional MCP API key. |

### React UI (`web/chat-ui`)

| Setting | Environment variable | Description |
| --- | --- | --- |
| Client host base URL | `VITE_MCP_BASE_URL` | Optional. The UI calls `${VITE_MCP_BASE_URL}/chat`. |

## Quickstart (three terminals)

> Replace the placeholder values with your own keys/URLs.

### 1) Start the MCP server

```bash
dotnet run --project src/McpServer
```

### 2) Start the MCP client host

```bash
export OpenAI__ApiKey="your-openai-key"
export Mcp__ServerUrl="http://localhost:5000"
export ASPNETCORE_URLS="http://localhost:3000"

dotnet run --project src/McpClientHost
```

The client host will expose `POST /chat` and run tool calls through the MCP server.

### 3) Start the React UI

```bash
cd web/chat-ui
npm install
VITE_MCP_BASE_URL="http://localhost:3000" npm run dev
```

The UI expects a `/chat` endpoint on the client host API.

## Extra: MCP inspector

You can inspect available MCP tools using the official inspector:

```bash
npx @modelcontextprotocol/inspector http://localhost:5000/mcp
```
