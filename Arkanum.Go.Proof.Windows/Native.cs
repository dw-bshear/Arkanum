using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Arkanum.Go.Core;

namespace Arkanum.Go.Proof.Windows;

internal static class Native
{
    internal const string ServiceName = "ArkanumGoSyncProof", PipeName = "ArkanumGoSyncProof-v1";
    internal const uint FILE_FLAG_FIRST_PIPE_INSTANCE = 0x00080000, PIPE_REJECT_REMOTE_CLIENTS = 8;
    // FILE_GENERIC_READ | FILE_GENERIC_WRITE minus FILE_APPEND_DATA / FILE_CREATE_PIPE_INSTANCE.
    internal const uint ClientAccess = 0x0012019B;
    internal const int TokenSessionId = 12;
    internal static string ServiceSid => ((SecurityIdentifier)new NTAccount("NT SERVICE", ServiceName)
        .Translate(typeof(SecurityIdentifier))).Value;
    // Virtual accounts can carry their service SID as primary identity without a duplicate group.
    // A SYSTEM token merely containing the service group is deliberately not accepted.
    internal static bool ServiceTokenMatches(string? primarySid, string expectedSid) =>
        !string.IsNullOrWhiteSpace(expectedSid) && string.Equals(primarySid, expectedSid, StringComparison.Ordinal);
    internal static void RequireServiceIdentity()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!ServiceTokenMatches(identity.User?.Value, ServiceSid)) throw new UnauthorizedAccessException();
    }
    internal static bool TrustedDescriptorPolicy(string? owner, bool protectedDacl, IEnumerable<string> allowedPrincipals, IEnumerable<string> trusted)
    {
        var expected = trusted.ToHashSet(StringComparer.Ordinal);
        var actual = allowedPrincipals.ToArray();
        return owner is not null && expected.Contains(owner) && protectedDacl && actual.Length > 0 &&
            actual.All(expected.Contains) && expected.All(x => actual.Contains(x, StringComparer.Ordinal));
    }
    internal static void ValidateTrustedDescriptor(byte[] bytes)
    {
        var descriptor = new System.Security.AccessControl.RawSecurityDescriptor(bytes, 0);
        var acl = descriptor.DiscretionaryAcl ?? throw new System.Security.Cryptography.CryptographicException();
        var principals = new List<string>();
        foreach (var ace in acl) {
            if (ace is not System.Security.AccessControl.CommonAce common || common.IsCallback ||
                common.AceQualifier != System.Security.AccessControl.AceQualifier.AccessAllowed)
                throw new System.Security.Cryptography.CryptographicException();
            principals.Add(common.SecurityIdentifier.Value);
        }
        if (!TrustedDescriptorPolicy(descriptor.Owner?.Value,
            descriptor.ControlFlags.HasFlag(System.Security.AccessControl.ControlFlags.DiscretionaryAclProtected), principals,
            new[] { ServiceSid, "S-1-5-18", "S-1-5-32-544" })) throw new System.Security.Cryptography.CryptographicException();
    }
    internal static void RequireAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) throw new UnauthorizedAccessException();
    }
    internal static void RequireTrustedTree(string directory)
    {
        // Only the fixed installer root is accepted by command dispatch. Reject ancestor junctions too.
        var root = new DirectoryInfo(directory);
        for (DirectoryInfo? parent = root; parent is not null; parent = parent.Parent)
            if ((parent.Attributes & FileAttributes.ReparsePoint) != 0) throw new UnauthorizedAccessException();
        var pending = new Queue<FileSystemInfo>(); pending.Enqueue(root);
        while (pending.Count != 0) {
            var item = pending.Dequeue();
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0) throw new UnauthorizedAccessException();
            System.Security.AccessControl.FileSystemSecurity security = item is DirectoryInfo folder
                ? folder.GetAccessControl(System.Security.AccessControl.AccessControlSections.Owner | System.Security.AccessControl.AccessControlSections.Access)
                : ((FileInfo)item).GetAccessControl(System.Security.AccessControl.AccessControlSections.Owner | System.Security.AccessControl.AccessControlSections.Access);
            byte[] bytes = security.GetSecurityDescriptorBinaryForm();
            var descriptor = new System.Security.AccessControl.RawSecurityDescriptor(bytes, 0);
            // Children may inherit the exact root DACL; all owners and every allow ACE remain trusted.
            if (item.FullName == root.FullName) {
                if (descriptor.Owner?.Value is not ("S-1-5-18" or "S-1-5-32-544")) throw new UnauthorizedAccessException();
                ValidateTrustedDescriptor(bytes);
            } else {
                descriptor.SetFlags(descriptor.ControlFlags | System.Security.AccessControl.ControlFlags.DiscretionaryAclProtected);
                byte[] effective = new byte[descriptor.BinaryLength]; descriptor.GetBinaryForm(effective, 0);
                ValidateTrustedDescriptor(effective);
            }
            // Enumerate only AFTER verifying the directory itself. Never follow a reparse child.
            if (item is DirectoryInfo current) foreach (var child in current.EnumerateFileSystemInfos()) pending.Enqueue(child);
        }
    }
    internal static void RequireProtectedReceipt(string path)
    {
        var file = new FileInfo(path);
        if ((file.Attributes & FileAttributes.ReparsePoint) != 0) throw new UnauthorizedAccessException();
        var security = file.GetAccessControl(System.Security.AccessControl.AccessControlSections.Owner | System.Security.AccessControl.AccessControlSections.Access);
        byte[] bytes = security.GetSecurityDescriptorBinaryForm();
        var descriptor = new System.Security.AccessControl.RawSecurityDescriptor(bytes, 0);
        if (descriptor.Owner?.Value is not ("S-1-5-18" or "S-1-5-32-544")) throw new UnauthorizedAccessException();
        ValidateTrustedDescriptor(bytes);
    }
    internal static object ReadDiagnostics()
    {
        RequireAdministrator(); // Before opening files, SCM, process tokens, or CNG handles.
        object Error(Exception error) => new { Available = false, ExceptionType = error.GetType().FullName, error.HResult,
            NativeErrorCode = error is Win32Exception native ? (int?)native.NativeErrorCode : null };
        object Key(string name) {
            try {
                using var key = System.Security.Cryptography.CngKey.Open(name,
                    System.Security.Cryptography.CngProvider.MicrosoftSoftwareKeyStorageProvider,
                    System.Security.Cryptography.CngKeyOpenOptions.MachineKey);
                byte[] bytes = key.GetProperty("Security Descr", (System.Security.Cryptography.CngPropertyOptions)4).GetValue() ?? throw new InvalidDataException();
                var descriptor = new System.Security.AccessControl.RawSecurityDescriptor(bytes, 0);
                var aces = descriptor.DiscretionaryAcl?.Cast<System.Security.AccessControl.GenericAce>().Select(ace =>
                    ace is System.Security.AccessControl.KnownAce known ? new {
                        Principal = known.SecurityIdentifier.Value == ServiceSid ? "Service" : known.SecurityIdentifier.Value switch {
                            "S-1-5-18" => "System", "S-1-5-32-544" => "Administrators", _ => "Other" },
                        AccessMask = known.AccessMask, Type = ace.AceType.ToString()
                    } : new { Principal = "Other", AccessMask = 0, Type = ace.AceType.ToString() }).ToArray();
                return new { Available = true, Name = name, key.IsMachineKey, ExportPolicy = key.ExportPolicy.ToString(),
                    KeyUsage = key.KeyUsage.ToString(), DaclProtected = descriptor.ControlFlags.HasFlag(System.Security.AccessControl.ControlFlags.DiscretionaryAclProtected), Aces = aces };
            } catch (Exception error) { return Error(error); }
        }
        object Service() {
            try {
                using var scm = OpenSCManagerW(null, null, 1); if (scm.IsInvalid) Fail();
                using var service = OpenServiceW(scm, ServiceName, 4); if (service.IsInvalid) Fail();
                if (!QueryServiceStatusEx(service, 0, out var status, Marshal.SizeOf<ServiceStatusProcess>(), out _)) Fail();
                if (status.State != 4 || status.ProcessId == 0) return new { Available = true, Running = false, State = status.State };
                using var process = OpenProcess(0x1000, false, status.ProcessId); if (process.IsInvalid) Fail();
                if (!OpenProcessToken(process, 8, out var token)) Fail();
                using (token) using (var identity = new WindowsIdentity(token.DangerousGetHandle())) {
                    bool primary = identity.User?.Value == ServiceSid;
                    bool group = identity.Groups?.Any(x => x.Value == ServiceSid) ?? false;
                    uint pid = status.ProcessId;
                    if (!QueryServiceStatusEx(service, 0, out status, Marshal.SizeOf<ServiceStatusProcess>(), out _) || status.State != 4 || status.ProcessId != pid) throw new UnauthorizedAccessException();
                    return new { Available = true, Running = true, ProcessId = pid, PrimaryIdentityMatches = primary, ServiceGroupPresent = group };
                }
            } catch (Exception error) { return Error(error); }
        }
        object Observation() {
            try {
                byte[] statusBytes = File.ReadAllBytes(Path.Combine(Broker.Root, "service-status.json"));
                using var status = System.Text.Json.JsonDocument.Parse(statusBytes);
                string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(Broker.Root, "state.bin"))));
                return new { Available = true, StateHashMatches = hash == status.RootElement.GetProperty("StateSha256").GetString(), Status = status.RootElement.Clone() };
            } catch (Exception error) { return Error(error); }
        }
        return new { Schema = 1, Service = Service(), Keys = new[] { Key("ArkanumGoSyncProof-auth"), Key("ArkanumGoSyncProof-unwrap") }, Observation = Observation() };
    }
    internal static void ExposeVerifierHandles()
    {
        // Standard-user CLI can inspect identity, but cannot duplicate/impersonate the service token.
        GrantQuery(GetCurrentProcess(), 0x1000);
        using var process = OpenProcess(0x1000, false, (uint)Environment.ProcessId);
        if (!OpenProcessToken(process, 0x00020000 | 0x00040000 | 8, out var token)) Fail();
        using (token) GrantQuery(token.DangerousGetHandle(), 8);
    }
    static void GrantQuery(IntPtr handle, int mask)
    {
        GetKernelObjectSecurity(handle, 4, null, 0, out var needed);
        if (needed == 0) Fail();
        byte[] bytes = new byte[needed];
        if (!GetKernelObjectSecurity(handle, 4, bytes, needed, out _)) Fail();
        var descriptor = new System.Security.AccessControl.RawSecurityDescriptor(bytes, 0);
        var acl = descriptor.DiscretionaryAcl ?? throw new UnauthorizedAccessException();
        acl.InsertAce(acl.Count, new System.Security.AccessControl.CommonAce(
            System.Security.AccessControl.AceFlags.None, System.Security.AccessControl.AceQualifier.AccessAllowed,
            mask, new SecurityIdentifier("S-1-5-32-545"), false, null));
        bytes = new byte[descriptor.BinaryLength]; descriptor.GetBinaryForm(bytes, 0);
        if (!SetKernelObjectSecurity(handle, 4, bytes)) Fail();
    }
    internal static byte[] Descriptor(string sddl)
    {
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl, 1, out var ptr, out var size)) Fail();
        try { byte[] data = new byte[size]; Marshal.Copy(ptr, data, 0, data.Length); return data; }
        finally { LocalFree(ptr); }
    }
    internal static NamedPipeServerStream Server()
    {
        byte[] descriptor = Descriptor($"D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;{ServiceSid})(A;;0x0012019B;;;BU)");
        var pinned = GCHandle.Alloc(descriptor, GCHandleType.Pinned);
        try
        {
            var attributes = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = pinned.AddrOfPinnedObject() };
            var handle = CreateNamedPipeW(@"\\.\pipe\" + PipeName,
                3 | 0x40000000 | FILE_FLAG_FIRST_PIPE_INSTANCE, PIPE_REJECT_REMOTE_CLIENTS,
                1, 65536, 4096, 0, ref attributes);
            if (handle.IsInvalid) { handle.Dispose(); Fail(); }
            return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle);
        }
        finally { pinned.Free(); }
    }
    internal static void DisconnectUnacceptedPipe(NamedPipeServerStream pipe)
    {
        // ConnectNamedPipe can report a peer that closed before accept completed.
        // Managed state is still WaitingToConnect/Disconnected; retain the same handle.
        if (!DisconnectNamedPipe(pipe.SafePipeHandle)) Fail();
    }
    internal static Task<NamedPipeClientStream> Client(CancellationToken token) => RetryPipeBusy(() =>
    {
        // Fixed endpoint, and no generic write (which includes pipe-instance creation).
        var handle = CreateFileW(@"\\.\pipe\" + PipeName, ClientAccess, 0, IntPtr.Zero, 3,
            0x40000000 | 0x00100000 | 0x00020000, IntPtr.Zero);
        if (handle.IsInvalid) {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error);
        }
        try { return new NamedPipeClientStream(PipeDirection.InOut, true, true, handle); }
        catch { handle.Dispose(); throw; }
    }, token);
    // The caller's existing ten-second deadline covers opening, validation and the exchange.
    // Only contention is retryable: missing endpoints, access denial and handle faults fail closed.
    internal static async Task<T> RetryPipeBusy<T>(Func<T> open, CancellationToken token)
    {
        while (true) {
            token.ThrowIfCancellationRequested();
            try { return open(); }
            catch (Win32Exception error) when (error.NativeErrorCode == 231) {
                await Task.Delay(TimeSpan.FromMilliseconds(25), token);
            }
        }
    }
    internal sealed record Caller(string Sid, int Session);
    internal static Caller CallerOf(NamedPipeServerStream pipe)
    {
        Caller? caller = null;
        pipe.RunAsClient(() => {
            using var identity = WindowsIdentity.GetCurrent(true);
            if (identity?.User is null || identity.ImpersonationLevel < TokenImpersonationLevel.Impersonation || !identity.IsAuthenticated)
                throw new UnauthorizedAccessException();
            var groups = identity.Groups?.Select(x => x.Value).ToArray() ?? [];
            if (!GetTokenInformation(identity.AccessToken, TokenSessionId, out int session, 4, out _)) Fail();
            bool remote = groups.Contains("S-1-5-14");
            if (!SessionCallerAllowed(identity.User.Value, SessionUserMatches(session), session, SessionUnlocked(session),
                groups.Contains("S-1-5-4"), remote, groups.Contains("S-1-5-2"))) throw new UnauthorizedAccessException();
            caller = new(identity.User.Value, session);
        });
        return caller ?? throw new UnauthorizedAccessException();
    }
    internal static bool SessionCallerAllowed(string tokenSid, string? loggedOnSid, int session, bool unlocked,
        bool interactive, bool remoteInteractive, bool network) =>
        session > 0 && unlocked && !string.IsNullOrWhiteSpace(tokenSid) && tokenSid == loggedOnSid &&
        (interactive || remoteInteractive) && (!network || remoteInteractive);
    internal static string? SessionUserMatches(int session)
    {
        // Resolve the OS WTS logon identity, not any caller-provided name/SID. WTSQueryUserToken
        // requires LocalSystem/SeTcbPrivilege; do not grant that privilege to this virtual account.
        if (session <= 0) return null;
        string Read(int kind) {
            if (!WTSQuerySessionInformationW(IntPtr.Zero, session, kind, out var data, out _)) Fail();
            try { return Marshal.PtrToStringUni(data) ?? ""; } finally { WTSFreeMemory(data); }
        }
        string user = Read(5), domain = Read(7);
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(domain)) return null;
        return ((SecurityIdentifier)new NTAccount(domain, user).Translate(typeof(SecurityIdentifier))).Value;
    }
    internal static int ActiveUnlockedSessionCount()
    {
        if (!WTSEnumerateSessionsW(IntPtr.Zero, 0, 1, out var data, out int count)) return -1;
        try {
            int active = 0, size = Marshal.SizeOf<WtsSessionInfo>();
            for (int i = 0; i < count; i++) {
                var session = Marshal.PtrToStructure<WtsSessionInfo>(IntPtr.Add(data, checked(i * size)));
                if (session.Session > 0 && session.State == 0 && SessionUnlocked(session.Session)) active++;
            }
            return active;
        } finally { WTSFreeMemory(data); }
    }
    internal static bool SessionUnlocked(int session)
    {
        if (!WTSQuerySessionInformationW(IntPtr.Zero, session, 25, out var data, out int bytes)) return false;
        try
        {
            // x64 WTSINFOEXW: DWORD Level + 4 alignment bytes; level-1 begins at +8.
            return bytes >= 20 && Marshal.ReadInt32(data) == 1 && Marshal.ReadInt32(data, 8) == session &&
                Marshal.ReadInt32(data, 12) == 0 && Marshal.ReadInt32(data, 16) == 1;
        }
        finally { WTSFreeMemory(data); }
    }
    internal static void ValidateServer(NamedPipeClientStream pipe)
    {
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var pid) || pid == 0) Fail();
        using var scm = OpenSCManagerW(null, null, 1);
        if (scm.IsInvalid) Fail();
        using var service = OpenServiceW(scm, ServiceName, 4);
        if (service.IsInvalid) Fail();
        if (!QueryServiceStatusEx(service, 0, out var status, Marshal.SizeOf<ServiceStatusProcess>(), out _)) Fail();
        if (status.State != 4 || status.ProcessId != pid) throw new UnauthorizedAccessException();
        using var process = OpenProcess(0x1000, false, pid);
        if (process.IsInvalid) Fail();
        if (!OpenProcessToken(process, 8, out var token)) Fail();
        using (token)
        using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
        {
            if (!ServiceTokenMatches(identity.User?.Value, ServiceSid))
                throw new UnauthorizedAccessException();
        }
        // Re-read SCM while holding the process handle, preventing PID-reuse substitution.
        if (!QueryServiceStatusEx(service, 0, out status, Marshal.SizeOf<ServiceStatusProcess>(), out _) ||
            status.State != 4 || status.ProcessId != pid) throw new UnauthorizedAccessException();
    }
    internal static ClockReading Clock()
    {
        var buffer = Marshal.AllocHGlobal(64);
        try
        {
            if (NtQuerySystemInformation(90, buffer, 64, out _) != 0) throw new InvalidOperationException("Boot identity unavailable");
            byte[] boot = new byte[16]; Marshal.Copy(buffer, boot, 0, 16);
            QueryInterruptTime(out ulong elapsed);
            if (elapsed > long.MaxValue) throw new InvalidOperationException();
            return new(new Guid(boot).ToString("D"), DateTimeOffset.UtcNow, TimeSpan.FromTicks((long)elapsed));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    static ServiceMainDelegate? mainRoot;
    static HandlerDelegate? handlerRoot;
    static IntPtr statusHandle;
    internal static int RunService(Func<Action, CancellationToken, Task> run)
    {
        int exit = 0;
        mainRoot = (_, _) => {
            using var stop = new CancellationTokenSource();
            handlerRoot = (control, _, _, _) => {
                if (control is 1 or 5) { Report(3, 0, 1, 15000); stop.Cancel(); }
                return 0;
            };
            statusHandle = RegisterServiceCtrlHandlerExW(ServiceName, handlerRoot, IntPtr.Zero);
            if (statusHandle == IntPtr.Zero) { exit = 1; return; }
            Report(2, 0, 1, 15000);
            string startupStage = "require-service-identity";
            try {
                RequireServiceIdentity();
                startupStage = "expose-verifier-handles"; ExposeVerifierHandles();
                startupStage = "broker-initialize";
                run(() => { startupStage = "service-loop"; Report(4, 5, 0, 0); }, stop.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (Exception error) { exit = 1; WriteStartupFailure(startupStage == "broker-initialize" ? Broker.StartupStage : startupStage, error); }
            Report(1, 0, 0, 0, exit == 0 ? 0u : 1064u);
        };
        var table = new[] { new ServiceTable { Name = ServiceName, Main = mainRoot }, new ServiceTable() };
        if (!StartServiceCtrlDispatcherW(table)) Fail();
        GC.KeepAlive(mainRoot); GC.KeepAlive(handlerRoot);
        return exit;
    }
    internal static object StartupFailure(string stage, Exception error) => new {
        Stage = stage, ExceptionType = error.GetType().FullName, HResult = error.HResult,
        NativeErrorCode = error is Win32Exception native ? (int?)native.NativeErrorCode : null,
        TopMethod = new System.Diagnostics.StackTrace(error, false).GetFrame(0)?.GetMethod()?.Name,
        ObservedUtc = DateTimeOffset.UtcNow
    };
    internal static void WriteStartupFailure(string stage, Exception error)
    {
        // Fixed protected location; deliberately exclude Message, Data, stack paths and identities.
        try {
            var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(StartupFailure(stage, error));
            string target = Path.Combine(Broker.Root, "startup-failure.json"), temp = target + ".tmp";
            using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { file.Write(bytes); file.Flush(true); }
            File.Move(temp, target, true);
        } catch { /* Never replace the original service failure. */ }
    }
    static void Report(uint state, uint accepts, uint checkpoint, uint wait, uint error = 0)
    {
        var status = new ServiceStatus { Type = 0x10, State = state, Accepts = accepts, Checkpoint = checkpoint, WaitHint = wait, Win32Exit = error };
        if (!SetServiceStatus(statusHandle, ref status)) Fail();
    }
    static void Fail() => throw new Win32Exception(Marshal.GetLastWin32Error());
    [StructLayout(LayoutKind.Sequential)] struct SecurityAttributes { public int Length; public IntPtr Descriptor; public int Inherit; }
    [StructLayout(LayoutKind.Sequential)] struct ServiceStatus { public uint Type, State, Accepts, Win32Exit, SpecificExit, Checkpoint, WaitHint; }
    [StructLayout(LayoutKind.Sequential)] struct ServiceStatusProcess { public uint Type, State, Accepts, Win32Exit, SpecificExit, Checkpoint, WaitHint, ProcessId, Flags; }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] struct ServiceTable { [MarshalAs(UnmanagedType.LPWStr)] public string? Name; public ServiceMainDelegate? Main; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void ServiceMainDelegate(uint argc, IntPtr argv);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate uint HandlerDelegate(uint control, uint eventType, IntPtr data, IntPtr context);
    sealed class ServiceHandle : SafeHandleZeroOrMinusOneIsInvalid { public ServiceHandle() : base(true) { } protected override bool ReleaseHandle() => CloseServiceHandle(handle); }
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool StartServiceCtrlDispatcherW([In] ServiceTable[] table);
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr RegisterServiceCtrlHandlerExW(string name, HandlerDelegate callback, IntPtr context);
    [DllImport("advapi32.dll", SetLastError=true)] static extern bool SetServiceStatus(IntPtr handle, ref ServiceStatus status);
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern ServiceHandle OpenSCManagerW(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern ServiceHandle OpenServiceW(ServiceHandle scm, string name, uint access);
    [DllImport("advapi32.dll", SetLastError=true)] static extern bool QueryServiceStatusEx(ServiceHandle handle, int level, out ServiceStatusProcess status, int size, out int needed);
    [DllImport("advapi32.dll")] static extern bool CloseServiceHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern SafePipeHandle CreateNamedPipeW(string name, uint mode, uint pipeMode, uint max, uint outSize, uint inSize, uint timeout, ref SecurityAttributes attributes);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool DisconnectNamedPipe(SafePipeHandle handle);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern SafePipeHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string sddl, uint revision, out IntPtr data, out uint size);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool GetNamedPipeServerProcessId(SafePipeHandle handle, out uint pid);
    [DllImport("kernel32.dll", SetLastError=true)] static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("advapi32.dll", SetLastError=true)] static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", SetLastError=true)] static extern bool GetTokenInformation(SafeAccessTokenHandle token, int kind, out int data, int size, out int needed);
    [DllImport("wtsapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool WTSQuerySessionInformationW(IntPtr server, int session, int kind, out IntPtr data, out int size);
    [StructLayout(LayoutKind.Sequential)] struct WtsSessionInfo { public int Session; public IntPtr Station; public int State; }
    [DllImport("wtsapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool WTSEnumerateSessionsW(IntPtr server, int reserved, int version, out IntPtr data, out int count);
    [DllImport("wtsapi32.dll")] static extern void WTSFreeMemory(IntPtr data);
    [DllImport("api-ms-win-core-realtime-l1-1-2.dll", ExactSpelling=true)] static extern void QueryInterruptTime(out ulong time);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("advapi32.dll", SetLastError=true)] static extern bool GetKernelObjectSecurity(IntPtr handle, uint info, byte[]? descriptor, uint size, out uint needed);
    [DllImport("advapi32.dll", SetLastError=true)] static extern bool SetKernelObjectSecurity(IntPtr handle, uint info, byte[] descriptor);
    [DllImport("ntdll.dll")] static extern int NtQuerySystemInformation(int kind, IntPtr data, int size, out int needed);
}
