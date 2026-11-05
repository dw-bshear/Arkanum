using System.Buffers.Binary;
using System.IO.Pipes;
using System.Reflection;

internal static class PipeDeliveryTests
{
    internal static async Task Run()
    {
        var method = typeof(Arkanum.Go.Proof.Windows.Native).Assembly
            .GetType("Arkanum.Go.Proof.Windows.Program")!
            .GetMethod("SendReply", BindingFlags.Static | BindingFlags.NonPublic);
        if (method is null) throw new InvalidOperationException("Missing acknowledged reply lifecycle");
        string name = "arkanum-synthetic-delivery-" + Guid.NewGuid().ToString("N");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        for (int i = 0; i < 12; i++)
        {
            using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            var connection = server.WaitForConnectionAsync(deadline.Token);
            await client.ConnectAsync(deadline.Token);
            await connection;
            using var sendBudget = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            sendBudget.CancelAfter(TimeSpan.FromSeconds(2));
            byte[] fixture = [11, 22, 33, (byte)i];
            var sent = (Task)method.Invoke(null, [server, fixture, sendBudget.Token])!;
            // Delay the reader deliberately: server write/FlushAsync completion is NOT consumption.
            await Task.Delay(15, deadline.Token);
            if (sent.IsCompleted) throw new InvalidOperationException("Reply completed before client consumption");
            byte[] prefix = new byte[4], received = new byte[fixture.Length];
            await client.ReadExactlyAsync(prefix, deadline.Token);
            if (BinaryPrimitives.ReadInt32LittleEndian(prefix) != fixture.Length) throw new InvalidDataException();
            await client.ReadExactlyAsync(received, deadline.Token);
            if (!received.AsSpan().SequenceEqual(fixture)) throw new InvalidDataException();
            if (i == 10)
            {
                await client.WriteAsync(new byte[] { 0 }, deadline.Token);
                try { await sent; throw new InvalidOperationException("Invalid acknowledgement accepted"); }
                catch (InvalidDataException) { }
            }
            else if (i == 11)
            {
                sendBudget.Cancel();
                try { await sent; throw new InvalidOperationException("Silent client ignored cancellation"); }
                catch (OperationCanceledException) { }
            }
            else
            {
                await client.WriteAsync(new byte[] { 1 }, deadline.Token);
                await client.FlushAsync(deadline.Token);
                await sent.WaitAsync(deadline.Token);
            }
            server.Disconnect();
        }
    }
}
