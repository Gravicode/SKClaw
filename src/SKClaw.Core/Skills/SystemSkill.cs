using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.SemanticKernel;

namespace SKClaw.Core.Skills;

/// <summary>
/// SystemSkill — OS/runtime info, environment variables, processes,
/// disk usage, network interfaces, and system diagnostics.
/// </summary>
public class SystemSkill
{
    // ── System Info ────────────────────────────────────────────

    [KernelFunction, Description("Get detailed system information: OS, CPU, memory, runtime, hostname")]
    public string GetSystemInfo()
    {
        var mem = GC.GetGCMemoryInfo();
        long totalRamMb = mem.TotalAvailableMemoryBytes / 1024 / 1024;
        return $"""
            OS              : {RuntimeInformation.OSDescription}
            Architecture    : {RuntimeInformation.OSArchitecture}
            Hostname        : {Dns.GetHostName()}
            CPU Cores       : {Environment.ProcessorCount}
            .NET Runtime    : {RuntimeInformation.FrameworkDescription}
            Process Arch    : {RuntimeInformation.ProcessArchitecture}
            Total RAM (avail): {totalRamMb:N0} MB
            Machine Name    : {Environment.MachineName}
            User Name       : {Environment.UserName}
            User Domain     : {Environment.UserDomainName}
            System Uptime   : {TimeSpan.FromMilliseconds(Environment.TickCount64):d' days 'hh':'mm':'ss}
            UTC Time        : {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}
            """;
    }

    [KernelFunction, Description("Get current process memory and CPU usage statistics")]
    public string GetProcessStats()
    {
        var proc = Process.GetCurrentProcess();
        proc.Refresh();
        return $"""
            Process ID      : {proc.Id}
            Process Name    : {proc.ProcessName}
            Working Set     : {proc.WorkingSet64 / 1024 / 1024:N0} MB
            Private Memory  : {proc.PrivateMemorySize64 / 1024 / 1024:N0} MB
            Virtual Memory  : {proc.VirtualMemorySize64 / 1024 / 1024:N0} MB
            Peak Working Set: {proc.PeakWorkingSet64 / 1024 / 1024:N0} MB
            GC Gen0/1/2     : {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}
            Thread Count    : {proc.Threads.Count}
            CPU Time (total): {proc.TotalProcessorTime:hh':'mm':'ss}
            Started At      : {proc.StartTime:yyyy-MM-dd HH:mm:ss}
            Uptime          : {DateTime.Now - proc.StartTime:hh':'mm':'ss}
            """;
    }

    [KernelFunction, Description("Get disk drive information and free space")]
    public string GetDiskInfo()
    {
        var sb = new StringBuilder();
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            double usedGb  = (drive.TotalSize - drive.AvailableFreeSpace) / 1073741824.0;
            double totalGb = drive.TotalSize / 1073741824.0;
            double freeGb  = drive.AvailableFreeSpace / 1073741824.0;
            double pct     = usedGb / totalGb * 100;
            sb.AppendLine($"Drive: {drive.Name} [{drive.DriveType}] Label={drive.VolumeLabel}");
            sb.AppendLine($"  Total: {totalGb:F1} GB  Used: {usedGb:F1} GB  Free: {freeGb:F1} GB  ({pct:F1}% used)");
            sb.AppendLine($"  Format: {drive.DriveFormat}  RootDir: {drive.RootDirectory}");
        }
        return sb.ToString().Trim();
    }

    [KernelFunction, Description("List, find, or kill OS processes")]
    public string ManageProcesses(
        [Description("Action: list, find, info")] string action,
        [Description("Process name filter (for find/info) or empty for all")] string nameFilter = "",
        [Description("Max processes to return")] int maxResults = 20)
    {
        var procs = string.IsNullOrEmpty(nameFilter)
            ? Process.GetProcesses()
            : Process.GetProcessesByName(nameFilter);

        if (action.ToLower() == "list" || action.ToLower() == "find")
        {
            var sorted = procs
                .OrderByDescending(p => { try { return p.WorkingSet64; } catch { return 0; } })
                .Take(maxResults)
                .Select(p =>
                {
                    try { return $"  PID={p.Id,-6} Mem={p.WorkingSet64/1024/1024,5}MB  {p.ProcessName}"; }
                    catch { return $"  PID={p.Id,-6} [access denied]  {p.ProcessName}"; }
                });
            return $"Processes ({procs.Length} total, showing {Math.Min(procs.Length, maxResults)}):\n" + string.Join("\n", sorted);
        }

        if (action.ToLower() == "info" && procs.Length > 0)
        {
            var p = procs[0];
            try
            {
                return $"""
                    PID      : {p.Id}
                    Name     : {p.ProcessName}
                    Memory   : {p.WorkingSet64/1024/1024:N0} MB
                    Threads  : {p.Threads.Count}
                    Started  : {p.StartTime:yyyy-MM-dd HH:mm:ss}
                    CPU Time : {p.TotalProcessorTime:hh':'mm':'ss}
                    """;
            }
            catch (Exception ex) { return $"Access denied: {ex.Message}"; }
        }

        return $"Action '{action}' not found or no process matched '{nameFilter}'.";
    }

    // ── Environment ────────────────────────────────────────────

    [KernelFunction, Description("Get an environment variable value")]
    public string GetEnvVar(
        [Description("Environment variable name")] string name,
        [Description("Default value if not found")] string defaultValue = "")
    {
        return Environment.GetEnvironmentVariable(name) ?? defaultValue;
    }

    [KernelFunction, Description("List environment variables matching a search prefix")]
    public string ListEnvVars(
        [Description("Search prefix or empty for all (max 50 shown)")] string prefix = "")
    {
        var vars = Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .Where(e => string.IsNullOrEmpty(prefix) ||
                        e.Key.ToString()!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Key.ToString())
            .Take(50)
            .Select(e => $"  {e.Key} = {e.Value}");
        return string.Join("\n", vars);
    }

    [KernelFunction, Description("Get common system paths: home, temp, desktop, documents, appdata")]
    public string GetSystemPaths()
    {
        return $"""
            Home        : {Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}
            Temp        : {Path.GetTempPath()}
            AppData     : {Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}
            LocalApp    : {Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}
            Desktop     : {Environment.GetFolderPath(Environment.SpecialFolder.Desktop)}
            Documents   : {Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}
            System      : {Environment.GetFolderPath(Environment.SpecialFolder.System)}
            ProgramFiles: {Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)}
            CurrentDir  : {Directory.GetCurrentDirectory()}
            Executable  : {Environment.ProcessPath}
            """;
    }

    // ── Network ────────────────────────────────────────────────

    [KernelFunction, Description("Get all local network interfaces and their IP addresses")]
    public string GetNetworkInterfaces()
    {
        var sb = new StringBuilder();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()
                     .Where(n => n.OperationalStatus == OperationalStatus.Up))
        {
            sb.AppendLine($"Interface: {ni.Name} [{ni.NetworkInterfaceType}] Speed={ni.Speed/1_000_000:N0} Mbps");
            foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                sb.AppendLine($"  IP: {addr.Address}  Mask: {addr.IPv4Mask}");
            var stats = ni.GetIPStatistics();
            sb.AppendLine($"  Sent: {stats.BytesSent/1024:N0} KB  Recv: {stats.BytesReceived/1024:N0} KB");
        }
        return sb.ToString().Trim();
    }

    [KernelFunction, Description("Get the public IP address of this machine")]
    public async Task<string> GetPublicIpAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            var ip = await client.GetStringAsync("https://api.ipify.org");
            var info = await client.GetStringAsync($"https://ipapi.co/{ip.Trim()}/json/");
            var doc = System.Text.Json.JsonDocument.Parse(info);
            var city    = doc.RootElement.TryGetProperty("city", out var c) ? c.GetString() : "?";
            var country = doc.RootElement.TryGetProperty("country_name", out var cn) ? cn.GetString() : "?";
            var org     = doc.RootElement.TryGetProperty("org", out var o) ? o.GetString() : "?";
            return $"Public IP: {ip.Trim()}\nLocation : {city}, {country}\nISP/Org  : {org}";
        }
        catch { return "Could not retrieve public IP."; }
    }

    [KernelFunction, Description("Check if a TCP port is open on a host")]
    public async Task<string> CheckPortAsync(
        [Description("Host or IP address")] string host,
        [Description("TCP port number")] int port,
        [Description("Timeout in milliseconds")] int timeoutMs = 3000)
    {
        try
        {
            using var tcp = new TcpClient();
            var cts = new CancellationTokenSource(timeoutMs);
            await tcp.ConnectAsync(host, port, cts.Token);
            return $"Port {port} on {host}: OPEN";
        }
        catch { return $"Port {port} on {host}: CLOSED or unreachable"; }
    }

    [KernelFunction, Description("Run a shell command and return its output (stdout + stderr). Use with caution.")]
    public async Task<string> RunCommandAsync(
        [Description("Command to execute")] string command,
        [Description("Arguments")] string arguments = "",
        [Description("Working directory (empty = current)")] string workingDir = "",
        [Description("Timeout in seconds")] int timeoutSeconds = 30)
    {
        try
        {
            var shell = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd" : "/bin/bash";
            var shellArgs = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? $"/c {command} {arguments}"
                : $"-c \"{command} {arguments}\"";

            var psi = new ProcessStartInfo(shell, shellArgs)
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute = false,
                WorkingDirectory = string.IsNullOrEmpty(workingDir) ? Directory.GetCurrentDirectory() : workingDir
            };

            using var proc = Process.Start(psi)!;
            var outTask = proc.StandardOutput.ReadToEndAsync();
            var errTask = proc.StandardError.ReadToEndAsync();
            var finished = proc.WaitForExit(timeoutSeconds * 1000);

            if (!finished) { proc.Kill(); return "Command timed out."; }

            var stdout = await outTask;
            var stderr = await errTask;
            var result = new StringBuilder();
            if (!string.IsNullOrEmpty(stdout)) result.Append(stdout);
            if (!string.IsNullOrEmpty(stderr)) result.AppendLine($"\n[STDERR]\n{stderr}");
            result.AppendLine($"\n[Exit Code: {proc.ExitCode}]");
            var output = result.ToString();
            return output.Length > 8000 ? output[..8000] + "...[truncated]" : output;
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }
}
