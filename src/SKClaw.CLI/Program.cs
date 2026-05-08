using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.NamingConventionBinder;
using System.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using SKClaw.Core.Agents;
using SKClaw.Core.Channels;
using SKClaw.Core.Configuration;
using SKClaw.Core.Connectors;
using SKClaw.Core.Memory;
using SKClaw.Core.MCP;
using SKClaw.Core.Models;
using SKClaw.Core.Skills;

// ── Bootstrap ─────────────────────────────────────────────────
var appConfig = new AppConfiguration(ConfigurationManager.AppSettings);

// DI setup
var services = new ServiceCollection();
services.AddSingleton(appConfig);
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
services.AddSingleton<KernelFactory>();
services.AddSingleton(sp =>
{
    var factory = sp.GetRequiredService<KernelFactory>();
    var kernel = factory.CreateKernel();
    PluginRegistry.RegisterAll(kernel, appConfig);
    return kernel;
});
services.AddSingleton<SkClawMemory>();
services.AddSingleton<SkClawMcpServer>();
services.AddTransient<SkClawAgent>(sp =>
{
    var kernel = sp.GetRequiredService<Microsoft.SemanticKernel.Kernel>();
    var config = sp.GetRequiredService<AppConfiguration>();
    var logger = sp.GetRequiredService<ILogger<SkClawAgent>>();
    var memory = sp.GetRequiredService<SkClawMemory>();
    return new SkClawAgent(kernel, config, logger, memory);
});

var provider = services.BuildServiceProvider();

// ── Root Command ──────────────────────────────────────────────
var rootCmd = new RootCommand("SKClaw — AI Agent powered by Semantic Kernel");

// ── chat command ──────────────────────────────────────────────
var chatCmd = new Command("chat", "Start interactive chat with the AI agent");
var chatProviderOpt = new Option<string?>("--provider", "LLM provider override");
var chatStreamOpt = new Option<bool>("--stream", () => true, "Enable streaming output");
var chatModelOpt = new Option<string?>("--model", "Model override");
chatCmd.Add(chatProviderOpt);
chatCmd.Add(chatStreamOpt);
chatCmd.Add(chatModelOpt);

chatCmd.Handler = CommandHandler.Create<string?, bool, string?>(async (providerName, stream, model) =>
{
    PrintBanner();
    var agent = provider.GetRequiredService<SkClawAgent>();

    AnsiConsole.MarkupLine($"[grey]Provider: [cyan]{appConfig.LLM.DefaultProvider}[/] | Model: [cyan]{appConfig.LLM.DefaultModel}[/][/]");
    AnsiConsole.MarkupLine("[grey]Type [white]/help[/] for commands, [white]/exit[/] to quit[/]\n");

    var sessionId = Guid.NewGuid().ToString("N")[..8];

    while (true)
    {
        AnsiConsole.Markup("[bold blue]You[/] » ");
        var input = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(input)) continue;

        if (input.StartsWith('/'))
        {
            var handled = await HandleCliCommand(input, agent, appConfig);
            if (handled == CliResult.Exit) break;
            continue;
        }

        if (stream && appConfig.Agent.StreamResponse)
        {
            AnsiConsole.Markup("\n[bold green]SKClaw[/] » ");
            await foreach (var chunk in agent.ChatStreamAsync(input))
            {
                Console.Write(chunk);
            }
            Console.WriteLine("\n");
        }
        else
        {
            AgentResponse? response = null;
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Thinking...", async ctx =>
                {
                    response = await agent.ChatAsync(input, sessionId);
                });

            AnsiConsole.MarkupLine($"\n[bold green]SKClaw[/] » {Markup.Escape(response?.Content ?? "")}");
            Console.WriteLine();
        }
    }
});

// ── ask command ────────────────────────────────────────────────
var askCmd = new Command("ask", "Ask a single question");
var askArg = new Argument<string>("question") { Description = "The question to ask" };
var askJsonOpt = new Option<bool>("--json", "Output response as JSON");
askCmd.Add(askArg);
askCmd.Add(askJsonOpt);

askCmd.Handler = CommandHandler.Create<string, bool>(async (question, json) =>
{
    var agent = provider.GetRequiredService<SkClawAgent>();
    var response = await agent.ChatAsync(question, Guid.NewGuid().ToString("N"));

    if (json)
    {
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(response,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
    else
    {
        Console.WriteLine(response?.Content ?? "");
    }
});

// ── run command ───────────────────────────────────────────────
var runCmd = new Command("run", "Run prompt from file or stdin");
var runFileOpt = new Option<FileInfo?>("--file", "Path to prompt file");
var runOutputOpt = new Option<FileInfo?>("--output", "Save response to file");
runCmd.Add(runFileOpt);
runCmd.Add(runOutputOpt);

runCmd.Handler = CommandHandler.Create<FileInfo?, FileInfo?>(async (file, output) =>
{
    string prompt;
    if (file != null)
    {
        if (!file.Exists) { AnsiConsole.MarkupLine("[red]File not found[/]"); return; }
        prompt = await File.ReadAllTextAsync(file.FullName);
    }
    else
    {
        prompt = await Console.In.ReadToEndAsync();
    }

    var agent = provider.GetRequiredService<SkClawAgent>();
    var response = await agent.ChatAsync(prompt.Trim(), "run");

    if (output != null)
    {
        await File.WriteAllTextAsync(output.FullName, response?.Content ?? "");
        AnsiConsole.MarkupLine($"[green]Response saved to:[/] {output.FullName}");
    }
    else
    {
        Console.WriteLine(response?.Content ?? "");
    }
});

// ── tools command ─────────────────────────────────────────────
var toolsCmd = new Command("tools", "List available tools");
toolsCmd.Handler = CommandHandler.Create(() =>
{
    var kernel = provider.GetRequiredService<Microsoft.SemanticKernel.Kernel>();
    var table = new Table().Border(TableBorder.Rounded).AddColumn("Plugin").AddColumn("Function").AddColumn("Description");
    foreach (var plugin in kernel.Plugins)
        foreach (var fn in plugin)
            table.AddRow(plugin.Name, fn.Name, fn.Description ?? "—");
    AnsiConsole.Write(table);
});

// ── status command ────────────────────────────────────────────
var statusCmd = new Command("status", "Show status");
statusCmd.Handler = CommandHandler.Create(() =>
{
    AnsiConsole.MarkupLine($"[bold]SKClaw[/] v{appConfig.App.Version}");
});

// ── Add commands ──────────────────────────────────────────────
rootCmd.Add(chatCmd);
rootCmd.Add(askCmd);
rootCmd.Add(runCmd);
rootCmd.Add(toolsCmd);
rootCmd.Add(statusCmd);

rootCmd.Handler = CommandHandler.Create(async () =>
{
    PrintBanner();
    await rootCmd.InvokeAsync("--help");
});

return await rootCmd.InvokeAsync(args);

static void PrintBanner()
{
    AnsiConsole.Clear();
    AnsiConsole.Write(new FigletText("SKClaw").Color(Color.Purple));
}

static async Task<CliResult> HandleCliCommand(string input, SkClawAgent agent, AppConfiguration config)
{
    if (input == "/exit") return CliResult.Exit;
    return CliResult.Continue;
}

enum CliResult { Continue, Exit }
