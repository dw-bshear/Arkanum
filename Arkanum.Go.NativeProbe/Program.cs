using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

internal static class Program
{
    const uint FILE_FLAG_FIRST_PIPE_INSTANCE = 0x00080000, FILE_FLAG_OVERLAPPED = 0x40000000;
    const uint PIPE_REJECT_REMOTE_CLIENTS = 0x8;
    static Process? child;
    static readonly List<object> results = [];
    static bool success = true;
    static readonly string[] Limitations = [
        "Synthetic primitive probe, not a production service or production security proof.",
        "Same-user self-spawn or elevated WinRM is not cross-user isolation or AD mapping acceptance.",
        "Does not prove service SID, CNG persisted key ACLs, logged-out refresh, or lease/sleep/reboot behavior.",
        "DACL permits only this process user; authorization assignment is trusted server test configuration."
    ];

    static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine("Windows required."); return 2; }
        using var watchdog = new Timer(_ => { KillChild(); Environment.Exit(124); }, null,
            TimeSpan.FromSeconds(55), Timeout.InfiniteTimeSpan);
        using var deadline = new CancellationTokenSource();
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        if (args.Length == 3 && args[0] == "--pipe-client")
        {
            try { return await Client(args[1], uint.Parse(args[2]), deadline.Token); }
            catch { return 24; }
        }
        if (args.Length != 2 || args[0] is not ("--self-test" or "--identity") ||
            !Path.IsPathFullyQualified(args[1]))
        { Console.Error.WriteLine("Usage: --self-test <absolute-output.json> | --identity <absolute-output.json>"); return 2; }
        object? processIdentity = null;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            string sid = identity.User?.Value ?? throw new InvalidOperationException("No process SID");
            processIdentity = Identity(identity);
            if (args[0] == "--self-test")
            {
                Check("windows_identity", () => Require(identity.IsAuthenticated && identity.User != null));
                Check("canonical_sid_round_trip", () => {
                    var original = new SecurityIdentifier(sid); byte[] binary = new byte[original.BinaryLength];
                    original.GetBinaryForm(binary, 0); Require(new SecurityIdentifier(binary, 0).Value == sid);
                });
                Crypto();
                string otherSid = sid == "S-1-5-18" ? "S-1-5-19" : "S-1-5-18";
                await PipeCheck("pipe_same_caller_allowed", sid, sid, false, deadline.Token);
                await PipeCheck("pipe_unassigned_denied", sid, null, false, deadline.Token);
                await PipeCheck("pipe_forged_sid_denied", sid, otherSid, false, deadline.Token);
                await PipeCheck("pipe_wrong_server_pid_rejected", sid, sid, true, deadline.Token);
                Check("pipe_first_instance_collision_rejected", () => {
                    string name = "ArkanumGoProbe-" + Guid.NewGuid().ToString("N");
                    // Allow a second instance by count, so only FIRST_PIPE_INSTANCE
                    // causes rejection rather than the ordinary one-instance limit.
                    using var first = CreateServer(name, sid, maxInstances: 2);
                    try { using var second = CreateServer(name, sid, maxInstances: 2); }
                    catch (Win32Exception ex) when (ex.NativeErrorCode == 5) { return; }
                    throw new InvalidOperationException("Duplicate first instance was not rejected");
                });
            }
        }
        catch (Exception ex) { Failure("probe_execution", ex); }
        finally { KillChild(); }
        try
        {
            var report = new { schemaVersion = 1, probeVersion = "1.0.0-beta.1", mode = args[0],
                utc = DateTimeOffset.UtcNow, success, processIdentity, results, limitations = Limitations };
            using var evidenceBudget = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await File.WriteAllTextAsync(args[1], JsonSerializer.Serialize(report,
                new JsonSerializerOptions { WriteIndented = true }), evidenceBudget.Token);
        }
        catch (Exception ex) { Console.Error.WriteLine("Evidence write failed: " + ex.GetType().Name); return 1; }
        return success ? 0 : 1;
    }

    static object Identity(WindowsIdentity identity)
    {
        if (!GetTokenInformation(identity.AccessToken, 20, out int elevated, sizeof(int), out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return new { pid = Environment.ProcessId, name = identity.Name, sid = identity.User!.Value,
            authenticationType = identity.AuthenticationType, impersonationLevel = identity.ImpersonationLevel.ToString(),
            elevated = elevated != 0, administratorRoleEnabled = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator),
            context = "Actual probe process identity/privilege, not production service identity" };
    }
    static void Require(bool condition) { if (!condition) throw new InvalidOperationException("Assertion failed"); }
    static void Check(string name, Action action)
    { try { action(); results.Add(new { name, passed = true }); } catch (Exception ex) { Failure(name, ex); } }
    static void Failure(string name, Exception ex)
    { success = false; results.Add(new { name, passed = false, errorType = ex.GetType().Name, hresult = ex.HResult }); }
    static void Crypto()
    {
        Check("cng_signature_valid", () => RsaProbe(false));
        Check("cng_signature_tamper_rejected", () => RsaProbe(true));
        Check("aes_gcm_round_trip", () => AesProbe(false));
        Check("aes_gcm_tamper_rejected", () => AesProbe(true));
    }
    static void RsaProbe(bool tamper)
    {
        byte[] data = Encoding.UTF8.GetBytes("Arkanum Go synthetic primitive data only");
        using var key = CngKey.Create(CngAlgorithm.Rsa, null, new CngKeyCreationParameters {
            KeyCreationOptions = CngKeyCreationOptions.None,
            Parameters = { new CngProperty("Length", BitConverter.GetBytes(2048), CngPropertyOptions.None) }
        });
        using var rsa = new RSACng(key);
        byte[] signature = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        Require(key.IsEphemeral && rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
        if (tamper) { signature[0] ^= 1; Require(!rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)); }
    }
    static void AesProbe(bool tamper)
    {
        byte[] data = Encoding.UTF8.GetBytes("Arkanum Go synthetic primitive data only");
        byte[] aesKey = RandomNumberGenerator.GetBytes(32), nonce = RandomNumberGenerator.GetBytes(12);
        byte[] cipher = new byte[data.Length], tag = new byte[16], clear = new byte[data.Length];
        try
        {
            using var aes = new AesGcm(aesKey, 16);
            aes.Encrypt(nonce, data, cipher, tag);
            aes.Decrypt(nonce, cipher, tag, clear); Require(clear.AsSpan().SequenceEqual(data));
            if (!tamper) return;
            tag[0] ^= 1;
            try { aes.Decrypt(nonce, cipher, tag, clear); }
            catch (CryptographicException) { return; }
            throw new InvalidOperationException("Tampered authentication tag accepted");
        }
        finally { CryptographicOperations.ZeroMemory(aesKey); CryptographicOperations.ZeroMemory(clear); }
    }

    static NamedPipeServerStream CreateServer(string name, string sid, uint maxInstances = 1)
    {
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW("D:P(A;;GA;;;" + sid + ")", 1, out var descriptor, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var attributes = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = descriptor };
            var handle = CreateNamedPipeW(@"\\.\pipe\" + name,
                3 | FILE_FLAG_OVERLAPPED | FILE_FLAG_FIRST_PIPE_INSTANCE, PIPE_REJECT_REMOTE_CLIENTS,
                maxInstances, 512, 512, 0, ref attributes);
            if (handle.IsInvalid) { int error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error); }
            try { return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle); }
            catch { handle.Dispose(); throw; }
        }
        finally { LocalFree(descriptor); }
    }
    static async Task PipeCheck(string name, string processSid, string? allowedSid, bool wrongPid, CancellationToken global)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(global);
        budget.CancelAfter(TimeSpan.FromSeconds(7));
        var token = budget.Token;
        try
        {
            string pipeName = "ArkanumGoProbe-" + Guid.NewGuid().ToString("N");
            using var server = CreateServer(pipeName, processSid);
            uint expectedPid = wrongPid ? 0 : (uint)Environment.ProcessId;
            child = StartClient(pipeName, expectedPid);
            await server.WaitForConnectionAsync(token);
            byte[] request = await ReadRequest(server, token);
            if (wrongPid)
            {
                await child.WaitForExitAsync(token);
                Require(request.Length == 0 && child.ExitCode == 23);
                results.Add(new { name, passed = true, expectedServerPid = expectedPid, sentPayload = false });
                return;
            }
            string? authenticatedSid = null;
            server.RunAsClient(() => { using var caller = WindowsIdentity.GetCurrent(true);
                authenticatedSid = caller?.User?.Value; });
            Require(authenticatedSid == processSid);
            using var payload = JsonDocument.Parse(request);
            string? claimedSid = payload.RootElement.GetProperty("sid").GetString();
            Require(claimedSid != authenticatedSid); // Every payload deliberately forges another SID.
            bool allowed = authenticatedSid != null && authenticatedSid == allowedSid;
            await server.WriteAsync(new byte[] { allowed ? (byte)1 : (byte)0 }, token);
            await child.WaitForExitAsync(token);
            Require(child.ExitCode == (allowed ? 0 : 10));
            Require(allowed == (name == "pipe_same_caller_allowed"));
            results.Add(new { name, passed = true, authenticatedSid, claimedSid, configuredAllowedSid = allowedSid,
                authorized = allowed, childPid = child.Id, expectedServerPid = expectedPid });
        }
        catch (Exception ex) { Failure(name, ex); }
        finally { KillChild(); }
    }
    static async Task<byte[]> ReadRequest(NamedPipeServerStream server, CancellationToken token)
    {
        using var bytes = new MemoryStream(); byte[] one = new byte[1];
        while (bytes.Length < 512)
        {
            if (await server.ReadAsync(one, token) == 0) return bytes.ToArray();
            if (one[0] == (byte)'\n') return bytes.ToArray();
            bytes.WriteByte(one[0]);
        }
        throw new InvalidDataException("Request exceeds synthetic frame limit");
    }
    static Process StartClient(string pipeName, uint expectedPid)
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("No executable path");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--pipe-client"); start.ArgumentList.Add(pipeName); start.ArgumentList.Add(expectedPid.ToString());
        return Process.Start(start) ?? throw new InvalidOperationException("Child did not start");
    }
    static async Task<int> Client(string name, uint expectedPid, CancellationToken token)
    {
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous,
            TokenImpersonationLevel.Impersonation);
        await client.ConnectAsync(5000, token); // Bounded native connect retries until the server is available.
        if (!GetNamedPipeServerProcessId(client.SafePipeHandle, out uint actualPid))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (actualPid != expectedPid) return 23; // Before sending ANY request bytes.
        using var identity = WindowsIdentity.GetCurrent();
        string forgedSid = identity.User!.Value == "S-1-5-18" ? "S-1-5-19" : "S-1-5-18";
        byte[] request = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { sid = forgedSid, synthetic = true }) + "\n");
        await client.WriteAsync(request, token);
        byte[] response = new byte[1];
        if (await client.ReadAsync(response, token) != 1) return 24;
        return response[0] == 1 ? 0 : 10;
    }
    static void KillChild()
    {
        var process = Interlocked.Exchange(ref child, null);
        if (process == null) return;
        try { if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(1000); } }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
        finally { process.Dispose(); }
    }
    [StructLayout(LayoutKind.Sequential)]
    struct SecurityAttributes { public int Length; public IntPtr Descriptor; public int InheritHandle; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafePipeHandle CreateNamedPipeW(string name, uint openMode, uint pipeMode, uint maxInstances,
        uint outBuffer, uint inBuffer, uint timeout, ref SecurityAttributes attributes);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string sddl, uint revision, out IntPtr descriptor, out uint size);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverPid);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass, out int information, int length, out int returnLength);
}
