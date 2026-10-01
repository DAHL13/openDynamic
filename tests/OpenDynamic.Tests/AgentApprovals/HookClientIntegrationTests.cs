using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using OpenDynamic.Core.AgentApprovals;
using Xunit;

namespace OpenDynamic.Tests.AgentApprovals;

public class HookClientIntegrationTests
{
    private static readonly string HookExePath = ResolveHookBinaryPath();

    private static string ResolveHookBinaryPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "openDynamic.sln")))
        {
            dir = dir.Parent;
        }

        if (dir == null)
        {
            throw new InvalidOperationException("Could not find repository root containing openDynamic.sln.");
        }

        bool isRelease = AppContext.BaseDirectory.Contains("Release", StringComparison.OrdinalIgnoreCase);
        string preferred = isRelease ? "Release" : "Debug";
        string fallback = isRelease ? "Debug" : "Release";

        string[] candidates =
        [
            Path.Combine(dir.FullName, "src", "OpenDynamic.Hook", "bin", preferred, "net10.0", "OpenDynamic.Hook.exe"),
            Path.Combine(dir.FullName, "src", "OpenDynamic.Hook", "bin", preferred, "publish", "OpenDynamic.Hook.exe"),
            Path.Combine(dir.FullName, "src", "OpenDynamic.Hook", "bin", fallback, "net10.0", "OpenDynamic.Hook.exe"),
            Path.Combine(dir.FullName, "src", "OpenDynamic.Hook", "bin", fallback, "publish", "OpenDynamic.Hook.exe")
        ];

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }

        throw new FileNotFoundException($"Could not find OpenDynamic.Hook.exe. Searched paths: {string.Join(", ", candidates)}");
    }

    private static async Task<(int ExitCode, string Stdout)> RunHookProcessAsync(string? stdinContent, params string[] args)
    {
        using var processCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var psi = new ProcessStartInfo
        {
            FileName = HookExePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        process.Start();

        if (stdinContent != null)
        {
            await process.StandardInput.WriteLineAsync(stdinContent.AsMemory(), processCts.Token).ConfigureAwait(false);
        }
        process.StandardInput.Close();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(processCts.Token);
        var exitTask = process.WaitForExitAsync(processCts.Token);

        try
        {
            await Task.WhenAll(stdoutTask, exitTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"OpenDynamic.Hook process exceeded execution limit of 3 seconds. Args: {string.Join(" ", args)}");
        }

        return (process.ExitCode, (await stdoutTask).Trim());
    }

    [Fact(Timeout = 3000)]
    public async Task HookClient_WhenServerAbsent_ReturnsAskWithExitCodeZero()
    {
        string samplePayload = """
        {
          "toolCall": {
            "name": "run_command",
            "args": { "CommandLine": "git status" }
          },
          "conversationId": "c1"
        }
        """;

        string randomPipe = "test-absent-pipe-" + Guid.NewGuid().ToString("N");
        var (exitCode, stdout) = await RunHookProcessAsync(samplePayload, "--pipe", randomPipe, "--connect-timeout-ms", "100");

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(stdout);
        Assert.Equal("ask", doc.RootElement.GetProperty("decision").GetString());
    }

    [Fact(Timeout = 3000)]
    public async Task HookClient_WhenStdinEmpty_ReturnsAskWithExitCodeZero()
    {
        var (exitCode, stdout) = await RunHookProcessAsync("");

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(stdout);
        Assert.Equal("ask", doc.RootElement.GetProperty("decision").GetString());
    }

    [Fact(Timeout = 3000)]
    public async Task HookClient_WhenStdinMalformed_ReturnsAskWithExitCodeZero()
    {
        var (exitCode, stdout) = await RunHookProcessAsync("{ not a valid json !#@$ }");

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(stdout);
        Assert.Equal("ask", doc.RootElement.GetProperty("decision").GetString());
    }

    [Fact(Timeout = 3000)]
    public async Task HookClient_WhenServerAllows_ReturnsAllowWithExitCodeZero()
    {
        string pipeName = "test-allow-pipe-" + Guid.NewGuid().ToString("N");
        string samplePayload = "{\"toolCall\":{\"name\":\"run_command\",\"args\":{\"CommandLine\":\"npm test\"}},\"conversationId\":\"c2\"}";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var serverStream = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);

        var serverTask = Task.Run(async () =>
        {
            await serverStream.WaitForConnectionAsync(cts.Token).ConfigureAwait(false);
            string? requestLine = await ReadLineAsync(serverStream, cts.Token).ConfigureAwait(false);
            Assert.NotNull(requestLine);

            var resp = ApprovalResponse.Allow();
            var respMsg = PipeMessage.CreateResponse(resp);
            await WriteLineAsync(serverStream, respMsg.Serialize(), cts.Token).ConfigureAwait(false);
        }, cts.Token);

        var (exitCode, stdout) = await RunHookProcessAsync(samplePayload, "--pipe", pipeName, "--wait-ms", "2000", "--connect-timeout-ms", "2000");
        await serverTask;

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(stdout);
        Assert.Equal("allow", doc.RootElement.GetProperty("decision").GetString());
    }

    [Fact(Timeout = 3000)]
    public async Task HookClient_WhenServerDenies_ReturnsDenyWithExitCodeZero()
    {
        string pipeName = "test-deny-pipe-" + Guid.NewGuid().ToString("N");
        string samplePayload = "{\"toolCall\":{\"name\":\"run_command\",\"args\":{\"CommandLine\":\"rm -rf /\"}},\"conversationId\":\"c3\"}";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var serverStream = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);

        var serverTask = Task.Run(async () =>
        {
            await serverStream.WaitForConnectionAsync(cts.Token).ConfigureAwait(false);
            string? requestLine = await ReadLineAsync(serverStream, cts.Token).ConfigureAwait(false);
            Assert.NotNull(requestLine);

            var resp = ApprovalResponse.Deny("No: usa otro enfoque");
            var respMsg = PipeMessage.CreateResponse(resp);
            await WriteLineAsync(serverStream, respMsg.Serialize(), cts.Token).ConfigureAwait(false);
        }, cts.Token);

        var (exitCode, stdout) = await RunHookProcessAsync(samplePayload, "--pipe", pipeName, "--wait-ms", "2000", "--connect-timeout-ms", "2000");
        await serverTask;

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(stdout);
        Assert.Equal("deny", doc.RootElement.GetProperty("decision").GetString());
        Assert.Equal("No: usa otro enfoque", doc.RootElement.GetProperty("reason").GetString());
    }

    [Fact(Timeout = 3000)]
    public async Task HookClient_WhenServerTimesOut_ReturnsAskWithExitCodeZero()
    {
        string pipeName = "test-timeout-pipe-" + Guid.NewGuid().ToString("N");
        string samplePayload = "{\"toolCall\":{\"name\":\"run_command\",\"args\":{\"CommandLine\":\"sleep 10\"}},\"conversationId\":\"c4\"}";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var serverStream = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);

        var serverTask = Task.Run(async () =>
        {
            await serverStream.WaitForConnectionAsync(cts.Token).ConfigureAwait(false);
            // Connected, intentionally delay past the client's 150 ms wait budget
            await Task.Delay(500, cts.Token).ConfigureAwait(false);
        }, cts.Token);

        // Client configured to wait only 150 ms
        var (exitCode, stdout) = await RunHookProcessAsync(samplePayload, "--pipe", pipeName, "--wait-ms", "150", "--connect-timeout-ms", "2000");

        cts.Cancel();
        try { await serverTask; } catch { }

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(stdout);
        Assert.Equal("ask", doc.RootElement.GetProperty("decision").GetString());
        Assert.Contains("Tiempo de espera agotado", doc.RootElement.GetProperty("reason").GetString());
    }

    [Fact(Timeout = 3000)]
    public async Task HookClient_WhenServerDisconnectsEarly_ReturnsAskWithExitCodeZero()
    {
        string pipeName = "test-disconnect-pipe-" + Guid.NewGuid().ToString("N");
        string samplePayload = "{\"toolCall\":{\"name\":\"run_command\",\"args\":{\"CommandLine\":\"dotnet build\"}},\"conversationId\":\"c5\"}";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var serverStream = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);

        var serverTask = Task.Run(async () =>
        {
            await serverStream.WaitForConnectionAsync(cts.Token).ConfigureAwait(false);
            await ReadLineAsync(serverStream, cts.Token).ConfigureAwait(false);
            serverStream.Disconnect();
        }, cts.Token);

        var (exitCode, stdout) = await RunHookProcessAsync(samplePayload, "--pipe", pipeName, "--wait-ms", "2000", "--connect-timeout-ms", "2000");
        await serverTask;

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(stdout);
        Assert.Equal("ask", doc.RootElement.GetProperty("decision").GetString());
    }

    private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[256];
        while (!ct.IsCancellationRequested)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
            if (read == 0) break;
            for (int i = 0; i < read; i++)
            {
                if (buffer[i] == (byte)'\n')
                {
                    return Encoding.UTF8.GetString(ms.ToArray()).TrimEnd('\r');
                }
                ms.WriteByte(buffer[i]);
            }
        }
        return ms.Length > 0 ? Encoding.UTF8.GetString(ms.ToArray()).TrimEnd('\r') : null;
    }

    private static async Task WriteLineAsync(Stream stream, string line, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        await stream.WriteAsync(bytes.AsMemory(), ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }
}
