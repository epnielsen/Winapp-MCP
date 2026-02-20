using FlaUI.UIA3;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using WinAppMCP.Services;

var builder = Host.CreateApplicationBuilder(args);

// MCP protocol uses stdout — all logging MUST go to stderr
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

// Register FlaUI automation as singletons
builder.Services.AddSingleton<UIA3Automation>();
builder.Services.AddSingleton<FlaUIService>();
builder.Services.AddSingleton<ElementResolver>();

// Register MCP server with stdio transport and auto-discovered tools
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "WinApp-MCP",
            Version = "1.0.0"
        };
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
