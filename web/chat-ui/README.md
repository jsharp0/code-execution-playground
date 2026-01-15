# MCP Chat UI

A simple React + TypeScript chat interface for an MCP client host.

## Features
- Message list, input box, and send button.
- Loading and error states for the API call.
- Tailwind CSS styling.

## Setup

```bash
cd web/chat-ui
npm install
```

## Wiring to your MCP client host

The UI sends a `POST /chat` request with JSON payload `{ "message": "..." }`.
You can point it at your host in one of these ways:

### Option 1: Proxy through the Vite dev server
1. Update `vite.config.ts` to add a proxy:
   ```ts
   export default defineConfig({
     plugins: [react()],
     server: {
       proxy: {
         "/chat": "http://localhost:3000",
       },
     },
   });
   ```
2. Start your MCP client host on `http://localhost:3000`.

### Option 2: Use an environment variable
Set `VITE_MCP_BASE_URL` to your MCP client host, for example:

```bash
VITE_MCP_BASE_URL="http://localhost:3000" npm run dev
```

The UI will call `${VITE_MCP_BASE_URL}/chat`.

## Run locally

```bash
npm run dev
```

## Build

```bash
npm run build
```
