using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Arkanum.Go.Proof.Windows;

internal sealed record WireRequest(string Command);
internal static class Program
{
    static readonly JsonSerializerOptions Strict = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    internal static WireRequest ParseRequest(byte[] request)
    {
        var parsed = JsonSerializer.Deserialize<WireRequest>(request, Strict) ?? throw new InvalidDataException();
        if (parsed.Command is not ("read" or "status")) throw new InvalidDataException();
        return parsed;
    }
    static async Task<int> Main(string[] args)
    {
        if (args.SequenceEqual(new[] { "--self-test" })) return SelfTest.Run();
        if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine("Windows required."); return 2; }
        try
        {
            if (args.SequenceEqual(new[] { "--provision" })) { Provisioning.Run(Broker.Root); return 0; }
            if (args.SequenceEqual(new[] { "--service" })) return Native.RunService(Service);
            if (args.SequenceEqual(new[] { "--diagnostics" })) { Console.WriteLine(JsonSerializer.Serialize(Native.ReadDiagnostics())); return 0; }
            if (args.Length is < 1 or > 2 || args[0] is not ("--read" or "--status") ||
                (args.Length == 2 && !Path.IsPathFullyQualified(args[1])))
            { Console.Error.WriteLine("Usage: --provision | --service | --diagnostics | --read [absolute-outcome.json] | --status [absolute-outcome.json]"); return 2; }
            return await Client(args[0] == "--status", args.Length == 2 ? args[1] : null);
        }
        catch { Console.Error.WriteLine("Proof operation failed (no diagnostic metadata)."); return 1; }
    }
    static async Task Service(Action ready, CancellationToken token)
    {
        using var broker = new Broker(Broker.Root);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        // Create once and reuse: no name-squatting gap between legitimate instances.
        using var pipe = Native.Server();
        ready();
        var sync = SyncLoop(broker, stop.Token);
        try
        {
            while (!token.IsCancellationRequested)
            {
                await HandleConnection(pipe, async connectionToken => {
                    using var budget = CancellationTokenSource.CreateLinkedTokenSource(connectionToken);
                    budget.CancelAfter(TimeSpan.FromSeconds(8));
                    string requestStage = "parse-request";
                    try
                    {
                        var request = ParseRequest(await Receive(pipe, 512, budget.Token));
                        requestStage = "authenticate-caller";
                        var caller = Native.CallerOf(pipe);
                        requestStage = "dispatch";
                        await broker.Dispatch(caller.Sid, request.Command == "status",
                            () => caller == Native.CallerOf(pipe),
                            (data, dispatchToken) => SendReply(pipe, data, dispatchToken), budget.Token);
                        // No response at all for unauthorized/no-grant/expired/denied users.
                    }
                    catch (Exception error) when (!token.IsCancellationRequested &&
                        (error is JsonException or InvalidDataException or EndOfStreamException or
                            TruncatedFrameException or UnauthorizedAccessException or OperationCanceledException || IsPeerClosure(error))) {
                        Native.WriteStartupFailure("pipe-" + requestStage, error);
                    }
                }, token);
            }
        }
        finally { stop.Cancel(); await sync; }
    }
    internal static bool IsPeerClosure(Exception error) => error switch {
        System.ComponentModel.Win32Exception native => native.NativeErrorCode is 109 or 232 or 233,
        IOException io => io.HResult is unchecked((int)0x8007006D) or unchecked((int)0x800700E8) or unchecked((int)0x800700E9),
        _ => false
    };
    internal static Task HandleConnection(NamedPipeServerStream pipe,
        Func<CancellationToken, Task> handle, CancellationToken token) =>
        HandleConnectionCore(pipe.WaitForConnectionAsync, handle, accepted => {
            if (accepted) pipe.Disconnect();
            else Native.DisconnectUnacceptedPipe(pipe);
        }, token);
    internal static async Task HandleConnectionCore(Func<CancellationToken, Task> accept,
        Func<CancellationToken, Task> handle, Action<bool> disconnect, CancellationToken token)
    {
        bool cleanup = false, accepted = false;
        try {
            try { await accept(token); cleanup = accepted = true; }
            catch (Exception error) when (IsPeerClosure(error)) { cleanup = true; return; }
            await handle(token);
        }
        catch (Exception error) when (IsPeerClosure(error)) { }
        finally {
            // A failed accept has not advanced .NET's state: Disconnect() would throw
            // InvalidOperationException. Reset its native handle without replacing it.
            // A canceled pending accept has no peer to disconnect.
            if (cleanup) {
                try { disconnect(accepted); }
                catch (Exception error) when (IsPeerClosure(error)) { }
            }
        }
    }
    static async Task SyncLoop(Broker broker, CancellationToken token)
    {
        int retry = 5;
        while (!token.IsCancellationRequested)
        {
            try { await broker.Refresh(token); retry = 60; }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception error) {
                Native.WriteStartupFailure("refresh-" + broker.RefreshStage, error);
                retry = Math.Min(retry * 2, 60);
            }
            try { await Task.Delay(TimeSpan.FromSeconds(retry), token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
        }
    }
    static async Task<int> Client(bool status, string? outcome)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var pipe = await Native.Client(budget.Token);
        Native.ValidateServer(pipe); // Before any request, including status. No PID from command line.
        await Send(pipe, JsonSerializer.SerializeToUtf8Bytes(new WireRequest(status ? "status" : "read")), budget.Token);
        byte[]? data = null;
        try
        {
            data = await Receive(pipe, 4 * 1024 * 1024, budget.Token);
            Native.ValidateServer(pipe);
            await Acknowledge(pipe, budget.Token);
            object report = status ? new { allowed = true, mode = "status" } :
                new { allowed = true, mode = "read", bytes = data.Length, sha256 = Convert.ToHexString(SHA256.HashData(data)) };
            string json = JsonSerializer.Serialize(report);
            Console.WriteLine(json);
            if (outcome is not null) await File.WriteAllTextAsync(outcome, json, budget.Token);
            return 0;
        }
        catch (NoResponseException)
        {
            const string denied = "{\"allowed\":false}";
            Console.WriteLine(denied);
            if (outcome is not null) await File.WriteAllTextAsync(outcome, denied, budget.Token);
            return 10;
        }
        finally { if (data is not null) CryptographicOperations.ZeroMemory(data); }
    }
    static async Task Send(Stream stream, byte[] data, CancellationToken token)
    {
        byte[] length = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(length, data.Length);
        await stream.WriteAsync(length, token); await stream.WriteAsync(data, token); await stream.FlushAsync(token);
    }
    internal static async Task SendReply(Stream stream, byte[] data, CancellationToken token)
    {
        await Send(stream, data, token);
        // DisconnectNamedPipe discards unread data. FlushAsync only completes the write,
        // so retain this instance until the validated reader acknowledges consumption.
        // The existing release deadline bounds silent/malicious clients; never synchronously drain.
        byte[] acknowledgement = new byte[1];
        await stream.ReadExactlyAsync(acknowledgement, token);
        if (acknowledgement[0] != 1) throw new InvalidDataException();
    }
    static async Task Acknowledge(Stream stream, CancellationToken token)
    {
        await stream.WriteAsync(new byte[] { 1 }, token);
        await stream.FlushAsync(token);
    }
    internal sealed class NoResponseException : EndOfStreamException { }
    internal sealed class TruncatedFrameException : IOException { }
    internal static async Task<byte[]> Receive(Stream stream, int limit, CancellationToken token)
    {
        byte[] length = new byte[4];
        if (await stream.ReadAsync(length.AsMemory(0, 1), token) == 0) throw new NoResponseException();
        try { await stream.ReadExactlyAsync(length.AsMemory(1), token); }
        catch (EndOfStreamException) { throw new TruncatedFrameException(); }
        int count = BinaryPrimitives.ReadInt32LittleEndian(length);
        if (count is < 1 || count > limit) throw new InvalidDataException();
        byte[] data = new byte[count];
        try {
            try { await stream.ReadExactlyAsync(data, token); return data; }
            catch (EndOfStreamException) { throw new TruncatedFrameException(); }
        }
        catch { CryptographicOperations.ZeroMemory(data); throw; }
    }
}
