using System.Buffers.Binary;
using System.ComponentModel;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

internal static class TransportTests
{
    static readonly Type ProgramType = typeof(Arkanum.Go.Proof.Windows.Native).Assembly.GetType("Arkanum.Go.Proof.Windows.Program")!;
    static MethodInfo Method(Type type, string name) => type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic) ?? throw new InvalidOperationException("Missing " + name);
    static void Require(bool value) { if (!value) throw new InvalidOperationException(); }
    internal static Task<byte[]> Receive(Stream stream, CancellationToken token = default) => (Task<byte[]>)Method(ProgramType, "Receive").Invoke(null, [stream, 512, token])!;
    static Task<T> Retry<T>(Func<T> open, CancellationToken token) => (Task<T>)Method(typeof(Arkanum.Go.Proof.Windows.Native), "RetryPipeBusy").MakeGenericMethod(typeof(T)).Invoke(null, [open, token])!;
    static Task Connection(NamedPipeServerStream pipe, Func<CancellationToken, Task> handler, CancellationToken token) => (Task)Method(ProgramType, "HandleConnection").Invoke(null, [pipe, handler, token])!;

    internal static async Task Frames()
    {
        foreach (byte[] frame in new byte[][] { [1], [1, 0], [1, 0, 0], [3, 0, 0, 0], [3, 0, 0, 0, 42], [3, 0, 0, 0, 42, 43] }) {
            using var stream = new CapturingStream(frame);
            try { await Receive(stream); throw new InvalidOperationException("Truncation accepted"); }
            catch (IOException error) { Require(error is not EndOfStreamException); }
            if (frame.Length >= 4) Require(stream.Payload.HasValue && stream.Payload.Value.Span.ToArray().All(x => x == 0));
        }
        using var empty = new MemoryStream();
        try { await Receive(empty); throw new InvalidOperationException(); }
        catch (EndOfStreamException error) { Require(error.GetType().Name == "NoResponseException"); }
        using var complete = new MemoryStream(new byte[] { 2, 0, 0, 0, 42, 43 });
        Require((await Receive(complete)).SequenceEqual(new byte[] { 42, 43 }));
    }
    sealed class CapturingStream(byte[] bytes) : MemoryStream(bytes)
    {
        internal Memory<byte>? Payload { get; private set; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) {
            if (Position >= 4) Payload ??= buffer;
            return base.ReadAsync(buffer, token);
        }
    }
    internal static async Task Busy()
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        int attempts = 0;
        Require(await Retry(() => ++attempts < 3 ? throw new Win32Exception(231) : 42, budget.Token) == 42 && attempts == 3);
        foreach (int code in new[] { 5, 2, 109, 232, 233, 6 }) {
            attempts = 0;
            try { await Retry<int>(() => { attempts++; throw new Win32Exception(code); }, budget.Token); throw new InvalidOperationException(); }
            catch (Win32Exception error) { Require(error.NativeErrorCode == code && attempts == 1); }
        }
        using var cancel = new CancellationTokenSource();
        attempts = 0;
        var pending = Retry<int>(() => { attempts++; cancel.Cancel(); throw new Win32Exception(231); }, cancel.Token);
        try { await pending; throw new InvalidOperationException(); } catch (OperationCanceledException) { Require(attempts == 1); }
        attempts = 0;
        try { await Retry(() => ++attempts, cancel.Token); throw new InvalidOperationException(); } catch (OperationCanceledException) { Require(attempts == 0); }
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(90));
        try { await Retry<int>(() => throw new Win32Exception(231), timeout.Token); throw new InvalidOperationException(); } catch (OperationCanceledException) { Require(timeout.IsCancellationRequested); }
    }
    internal static void ClosurePolicy()
    {
        var method = Method(ProgramType, "IsPeerClosure");
        foreach (int code in new[] { 109, 232, 233, 5, 6, 87, 995 }) {
            bool expected = code is 109 or 232 or 233;
            Require((bool)method.Invoke(null, [new IOException("synthetic", unchecked((int)0x80070000) | code)])! == expected);
            Require((bool)method.Invoke(null, [new Win32Exception(code)])! == expected);
        }
        Require(!(bool)method.Invoke(null, [new UnauthorizedAccessException()])!);
        Require(!(bool)method.Invoke(null, [new InvalidOperationException()])!);
    }
    internal static async Task AcceptAndCleanupFaults()
    {
        var method = Method(ProgramType, "HandleConnectionCore");
        Task Run(Func<CancellationToken, Task> accept, Func<CancellationToken, Task> handle, Action<bool> reset) =>
            (Task)method.Invoke(null, [accept, handle, reset, CancellationToken.None])!;
        foreach (int code in new[] { 109, 232, 233 }) {
            int resets = 0; bool dispatched = false;
            await Run(_ => Task.FromException(new Win32Exception(code)), _ => { dispatched = true; return Task.CompletedTask; }, accepted => { Require(!accepted); resets++; });
            Require(resets == 1 && !dispatched);
            await Run(_ => Task.CompletedTask, _ => Task.CompletedTask, accepted => { Require(accepted); throw new Win32Exception(code); });
        }
        foreach (int code in new[] { 5, 6, 87 }) {
            int resets = 0;
            try { await Run(_ => Task.FromException(new Win32Exception(code)), _ => Task.CompletedTask, _ => resets++); throw new InvalidOperationException(); }
            catch (Win32Exception error) { Require(error.NativeErrorCode == code && resets == 0); }
            try { await Run(_ => Task.CompletedTask, _ => Task.CompletedTask, _ => throw new Win32Exception(code)); throw new InvalidOperationException(); }
            catch (Win32Exception error) { Require(error.NativeErrorCode == code); }
        }
        try { await Run(_ => Task.CompletedTask, _ => Task.FromException(new UnauthorizedAccessException()), _ => { }); throw new InvalidOperationException(); }
        catch (UnauthorizedAccessException) { }
    }
    internal static async Task Lifecycle()
    {
        string name = "arkanum-synthetic-recovery-" + Guid.NewGuid().ToString("N");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        // Force connect/close BEFORE accept, not merely a read-side disconnect.
        using (var early = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous)) {
            await early.ConnectAsync(deadline.Token);
        }
        await Connection(server, async token => { try { await Receive(server, token); } catch (IOException) { } }, deadline.Token);
        for (int mode = 0; mode < 3; mode++) {
            using var requestCancel = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var bad = Connection(server, async token => {
                connected.TrySetResult();
                try { await Receive(server, token); }
                catch (IOException) { }
            }, requestCancel.Token);
            using (var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous)) {
                await client.ConnectAsync(deadline.Token);
                if (mode == 1) await client.WriteAsync(new byte[] { 3, 0, 0, 0, 42 }, deadline.Token);
                if (mode == 2) {
                    await connected.Task.WaitAsync(deadline.Token);
                    requestCancel.Cancel();
                    // Observe cancellation while the peer is still open, not an EOF race.
                    try { await bad.WaitAsync(deadline.Token); throw new InvalidOperationException("Request cancellation ignored"); }
                    catch (OperationCanceledException) when (requestCancel.IsCancellationRequested) { }
                }
            }
            try { await bad.WaitAsync(deadline.Token); } catch (OperationCanceledException) when (requestCancel.IsCancellationRequested) { }
            byte[]? received = null;
            var good = Connection(server, async token => { received = await Receive(server, token); }, deadline.Token);
            using (var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous)) {
                await client.ConnectAsync(deadline.Token);
                await client.WriteAsync(new byte[] { 1, 0, 0, 0, 99 }, deadline.Token);
                await good.WaitAsync(deadline.Token);
            }
            Require(received is not null && received.SequenceEqual(new byte[] { 99 }));
        }
        // Cancellation of a pending accept must also leave this exact instance usable.
        using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token)) {
            var waiting = Connection(server, _ => Task.CompletedTask, cancel.Token);
            cancel.Cancel();
            try { await waiting; throw new InvalidOperationException(); } catch (OperationCanceledException) { }
        }
        var final = Connection(server, token => Receive(server, token), deadline.Token);
        using var last = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await last.ConnectAsync(deadline.Token);
        await last.WriteAsync(new byte[] { 1, 0, 0, 0, 77 }, deadline.Token);
        await final.WaitAsync(deadline.Token);
    }
    internal static async Task NativeBusy()
    {
        if (!OperatingSystem.IsWindows()) { Console.WriteLine("SKIP native-pipe-busy (Windows required)"); return; }
        string name = "arkanum-synthetic-busy-" + Guid.NewGuid().ToString("N");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var first = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        var accept = server.WaitForConnectionAsync(deadline.Token);
        await first.ConnectAsync(deadline.Token); await accept;
        int busy = 0;
        var next = Retry(() => {
            var handle = CreateFileW(@"\\.\pipe\" + name, Arkanum.Go.Proof.Windows.Native.ClientAccess, 0, IntPtr.Zero, 3, 0x40000000 | 0x00100000 | 0x00020000, IntPtr.Zero);
            if (!handle.IsInvalid) return handle;
            int code = Marshal.GetLastWin32Error(); handle.Dispose(); if (code == 231) busy++;
            throw new Win32Exception(code);
        }, deadline.Token);
        Require(busy == 1 && !next.IsCompleted);
        first.Dispose(); server.Disconnect();
        accept = server.WaitForConnectionAsync(deadline.Token);
        using var second = await next; await accept;
        Require(!second.IsInvalid && busy >= 1);
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafePipeHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
}
