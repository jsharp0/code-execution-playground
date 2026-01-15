using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient();
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

app.MapGet("/", () => "MCP server is running.");
app.MapMcp();

app.Run();
