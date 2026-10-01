using System.IO.Pipes;
using System.Text;
using OpenDynamic.Core.AgentApprovals;

namespace OpenDynamic.Hook;

public static class Program
{
    public const string DefaultPipeName = "openDynamic-agent-v1";
    public const int DefaultConnectionTimeoutMs = 300;
    public const int DefaultWaitMs = 90_000;

    public static async Task<int> Main(string[] args)
    {
        // Golden Rule 12: Fail safe to "ask" under ALL circumstances. Exit code MUST always be 0.
        try
        {
            return await ExecuteHookAsync(args).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            EmitAsk($"Excepción inesperada en cliente hook: {ex.Message}");
            return 0;
        }
    }

    private static async Task<int> ExecuteHookAsync(string[] args)
    {
        int waitMs = DefaultWaitMs;
        int connectionTimeoutMs = DefaultConnectionTimeoutMs;
        string pipeName = DefaultPipeName;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.Equals("--version", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("{\"name\": \"openDynamic-hook\", \"version\": \"1.0.0\", \"protocol\": 1}");
                return 0;
            }

            if (arg.Equals("--selftest", StringComparison.OrdinalIgnoreCase))
            {
                return RunSelfTest();
            }

            if ((arg.Equals("--wait", StringComparison.OrdinalIgnoreCase) ||
                 arg.Equals("--timeout", StringComparison.OrdinalIgnoreCase)) &&
                i + 1 < args.Length && int.TryParse(args[i + 1], out int parsedWaitSec))
            {
                waitMs = Math.Max(50, parsedWaitSec * 1000);
                i++;
            }
            else if ((arg.Equals("--wait-ms", StringComparison.OrdinalIgnoreCase) ||
                      arg.Equals("--timeout-ms", StringComparison.OrdinalIgnoreCase)) &&
                     i + 1 < args.Length && int.TryParse(args[i + 1], out int parsedWaitMs))
            {
                waitMs = Math.Max(50, parsedWaitMs);
                i++;
            }
            else if ((arg.Equals("--connect-timeout", StringComparison.OrdinalIgnoreCase) ||
                      arg.Equals("--connect-timeout-ms", StringComparison.OrdinalIgnoreCase)) &&
                     i + 1 < args.Length && int.TryParse(args[i + 1], out int parsedConnTimeout))
            {
                connectionTimeoutMs = Math.Max(50, parsedConnTimeout);
                i++;
            }
            else if (arg.Equals("--pipe", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                pipeName = args[i + 1].Trim();
                i++;
            }
        }

        // Read hook payload strictly from standard input with non-blocking line reading
        using var stdinCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(Math.Min(waitMs, 2000)));
        string? inputJson = await ReadStdinSafelyAsync(stdinCts.Token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(inputJson))
        {
            EmitAsk("Entrada vacía o ausente recibida en stdin.");
            return 0;
        }

        ApprovalRequest request;
        try
        {
            request = ApprovalRequest.FromHookPayload(inputJson);
        }
        catch (Exception ex)
        {
            EmitAsk($"Payload JSON no válido en stdin: {ex.Message}");
            return 0;
        }

        // Connect to local Named Pipe server with fast timeout (300 ms) and CurrentUserOnly security
        using var clientStream = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);

        try
        {
            using var connectCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(connectionTimeoutMs));
            await clientStream.ConnectAsync(connectionTimeoutMs, connectCts.Token).ConfigureAwait(false);
        }
        catch
        {
            // Server not running, island hidden, or connection timeout -> Fail safe to native dialog immediately
            EmitAsk("openDynamic no está activo o disponible.");
            return 0;
        }

        // Wait for response envelope up to waitMs
        using var waitCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(waitMs));

        try
        {
            // Send request envelope
            var requestMsg = PipeMessage.CreateRequest(request);
            var bytes = Encoding.UTF8.GetBytes(requestMsg.Serialize() + "\n");
            await clientStream.WriteAsync(bytes.AsMemory(), waitCts.Token).ConfigureAwait(false);
            await clientStream.FlushAsync(waitCts.Token).ConfigureAwait(false);

            string? responseJson = await ReadLineWithLimitAsync(clientStream, PipeMessage.MaxPayloadSizeBytes, waitCts.Token).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(responseJson))
            {
                EmitAsk("El servidor cerró la conexión sin emitir respuesta.");
                return 0;
            }

            var pipeResponse = PipeMessage.Deserialize(responseJson);
            var approvalResponse = ApprovalResponse.FromJsonSafe(pipeResponse.Payload);
            Console.WriteLine(approvalResponse.ToJson());
            return 0;
        }
        catch (OperationCanceledException)
        {
            EmitAsk($"Tiempo de espera agotado ({waitMs} ms).");
            return 0;
        }
        catch (Exception ex)
        {
            EmitAsk($"Error durante la comunicación con openDynamic: {ex.Message}");
            return 0;
        }
    }

    private static int RunSelfTest()
    {
        try
        {
            // 1. Verify response models
            var allow = ApprovalResponse.Allow();
            var deny = ApprovalResponse.Deny("Test");
            var ask = ApprovalResponse.AskNative("Test");

            if (allow.Decision != "allow" || deny.Decision != "deny" || ask.Decision != "ask")
            {
                Console.WriteLine("{\"selftest\": \"failed\", \"reason\": \"ApprovalResponse factory mismatch\"}");
                return 0;
            }

            // 2. Verify protocol envelope round-trip
            var testReq = new ApprovalRequest("selftest-id", "conv", 1, "run_command", "git status");
            var msg = PipeMessage.CreateRequest(testReq);
            var serialized = msg.Serialize();
            var deserialized = PipeMessage.Deserialize(serialized);

            if (deserialized.Version != 1 || deserialized.Type != "request")
            {
                Console.WriteLine("{\"selftest\": \"failed\", \"reason\": \"PipeMessage protocol roundtrip mismatch\"}");
                return 0;
            }

            Console.WriteLine("{\"selftest\": \"passed\", \"protocol\": 1, \"status\": \"ok\"}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{{\"selftest\": \"failed\", \"reason\": \"{ex.Message}\"}}");
            return 0;
        }
    }

    private static async Task<string?> ReadStdinSafelyAsync(CancellationToken ct)
    {
        try
        {
            if (!Console.IsInputRedirected)
            {
                return null;
            }

            using var reader = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
            var sb = new StringBuilder();

            string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line == null) return null;

            line = line.Trim().Trim('\uFEFF');
            if (string.IsNullOrWhiteSpace(line)) return null;

            sb.Append(line);

            // Fast path: single-line JSON payload (standard Antigravity hook invocation)
            if (line.StartsWith('{') && line.EndsWith('}'))
            {
                return line;
            }

            // Multi-line JSON payload
            while (!ct.IsCancellationRequested)
            {
                var nextLine = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (nextLine == null) break;
                sb.AppendLine(nextLine);
                var current = sb.ToString().Trim();
                if (current.StartsWith('{') && current.EndsWith('}'))
                {
                    break;
                }
                if (sb.Length > PipeMessage.MaxPayloadSizeBytes)
                {
                    return null;
                }
            }

            return sb.ToString().Trim().Trim('\uFEFF');
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string> ReadLineWithLimitAsync(Stream stream, int maxBytes, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[512];
        int totalBytes = 0;

        while (!ct.IsCancellationRequested)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
            if (read == 0) break;

            for (int i = 0; i < read; i++)
            {
                byte b = buffer[i];
                if (b == (byte)'\n')
                {
                    return Encoding.UTF8.GetString(ms.ToArray()).TrimEnd('\r');
                }

                ms.WriteByte(b);
                totalBytes++;

                if (totalBytes > maxBytes)
                {
                    throw new InvalidOperationException("Incoming response exceeded maximum allowed payload size.");
                }
            }
        }

        return Encoding.UTF8.GetString(ms.ToArray()).TrimEnd('\r');
    }

    private static void EmitAsk(string reason)
    {
        Console.WriteLine(ApprovalResponse.AskNative(reason).ToJson());
    }
}
