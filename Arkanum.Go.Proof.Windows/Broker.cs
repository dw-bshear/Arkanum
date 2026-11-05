using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Arkanum.Go.Core;
using Arkanum.Go.Proof.Protocol;

namespace Arkanum.Go.Proof.Windows;

internal sealed record Settings(string DeviceId, string Endpoint);
internal sealed record Durable(long Sequence, bool Denied, LeaseState? Lease, ProofPackage[] Packages)
{
    public DateTimeOffset? LastSuccessfulRefreshUtc { get; init; }
    public DateTimeOffset? OriginalGrantExpiresUtc { get; init; }
}
internal sealed record SealedState(byte[] Key, byte[] Nonce, byte[] Cipher, byte[] Tag);
internal sealed class Broker : IDisposable
{
    internal static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ArkanumGoSyncProof");
    readonly RSACng auth, unwrap;
    readonly string root;
    readonly SemaphoreSlim gate = new(1, 1);
    Durable state;
    bool storageFault;
    internal string RefreshStage { get; private set; } = "not-started";
    internal static string StartupStage = "broker-require-identity";
    internal Broker(string directory)
    {
        StartupStage = "broker-require-identity";
        Native.RequireServiceIdentity(); root = directory;
        // Install creates and protects root before service startup. Never accept a caller-selected cache path.
        if (!Directory.Exists(root)) throw new InvalidOperationException("Install required");
        StartupStage = "validate-protected-root";
        Native.RequireTrustedTree(root);
        bool marker = File.Exists(Path.Combine(root, "initialized.marker"));
        bool established = File.Exists(Path.Combine(root, "state.bin"));
        string receiptPath = Path.Combine(root, BootstrapReceipt.FileName);
        bool receipt = File.Exists(receiptPath);
        bool bothKeys = CngKey.Exists("ArkanumGoSyncProof-auth", CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.MachineKey) &&
            CngKey.Exists("ArkanumGoSyncProof-unwrap", CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.MachineKey);
        bool bootstrap = BootstrapAllowed(marker, established, bothKeys, receipt);
        if (!bootstrap && (!EstablishmentAllowed(marker, bothKeys, established) || receipt))
            throw new CryptographicException("Initialization incomplete or durable state lost; admin recovery required");
        StartupStage = "open-auth-key";
        auth = OpenKey("ArkanumGoSyncProof-auth", CngKeyUsages.Signing);
        try {
            StartupStage = "open-unwrap-key";
            unwrap = OpenKey("ArkanumGoSyncProof-unwrap", CngKeyUsages.Decryption);
            try {
                StartupStage = "validate-distinct-keys";
                ValidateDistinctKeys(auth, unwrap);
                if (bootstrap) {
                    StartupStage = "consume-bootstrap-receipt";
                    Native.RequireProtectedReceipt(receiptPath);
                    BootstrapReceipt.Validate(File.ReadAllBytes(receiptPath), auth.ExportSubjectPublicKeyInfo(),
                        unwrap.ExportSubjectPublicKeyInfo(), File.ReadAllBytes(Path.Combine(root, "config.json")));
                    // Consume BEFORE creating any state: a crash can require admin recovery, never replay enrollment.
                    File.Delete(receiptPath);
                    Atomic("initialized.marker", "ArkanumGoSyncProof-initialized-v1"u8.ToArray());
                }
                StartupStage = "load-state";
                state = established ? Load() : new(0, false, null, []);
                // Retain authenticated legacy migration; no marker is written before successful UnsealState.
                if (!marker && established) Atomic("initialized.marker", "ArkanumGoSyncProof-initialized-v1"u8.ToArray());
                if (bootstrap) { StartupStage = "commit-initial-state"; Save(); }
                StartupStage = "write-enrollment";
                WriteEnrollment();
                StartupStage = "create-pipe";
            } catch { unwrap.Dispose(); throw; }
        } catch { auth.Dispose(); throw; }
    }
    internal static bool EstablishmentAllowed(bool marker, bool bothKeys, bool stateFile) => bothKeys && stateFile;
    internal static bool BootstrapAllowed(bool marker, bool stateFile, bool bothKeys, bool receipt) =>
        !marker && !stateFile && bothKeys && receipt;
    internal static void ValidateDistinctKeys(RSA authKey, RSA unwrapKey)
    {
        if (authKey.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(unwrapKey.ExportSubjectPublicKeyInfo())) throw new CryptographicException();
    }
    static void ValidateKeySecurity(CngKey key, CngKeyUsages usage)
    {
        if (key.ExportPolicy != CngExportPolicies.None || !key.IsMachineKey || key.KeyUsage != usage || key.Algorithm != CngAlgorithm.Rsa)
            throw new CryptographicException();
        Native.ValidateTrustedDescriptor(key.GetProperty("Security Descr", (CngPropertyOptions)5).GetValue() ?? throw new CryptographicException());
    }
    internal static RSACng OpenKey(string name, CngKeyUsages usage)
    {
        // Opening is the ONLY service path. Absence/permission failures must never recreate a key.
        CngKey key = CngKey.Open(name, CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.MachineKey);
        try {
            ValidateKeySecurity(key, usage);
            var rsa = new RSACng(key);
            if (rsa.KeySize != 3072) { rsa.Dispose(); throw new CryptographicException(); }
            return rsa;
        }
        finally { key.Dispose(); } // RSACng duplicates the native handle.
    }
    void WriteEnrollment()
    {
        var request = new CertificateRequest("CN=ArkanumGoSyncProof", auth, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new System.Security.Cryptography.OidCollection {
            new("1.3.6.1.5.5.7.3.2") }, true));
        Atomic("auth.csr.pem", System.Text.Encoding.ASCII.GetBytes(request.CreateSigningRequestPem()));
        Atomic("unwrap.spki", unwrap.ExportSubjectPublicKeyInfo());
    }
    internal static void ValidateResponse(ProofSyncResponse response, Settings settings, string nonce, RSA signer, long sequence)
    {
        if (response.Protocol != ProofWire.ProtocolId || response.DeviceId != settings.DeviceId || response.Nonce != nonce ||
            response.Sequence <= sequence || response.Packages is null || response.Signature is null ||
            !signer.VerifyData(ProofWire.EncodeUnsigned(response), response.Signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
            throw new CryptographicException();
        if (response.Packages.Length > 256 || response.Packages.Any(p => p.Grant.Claims.DeviceId != settings.DeviceId))
            throw new CryptographicException();
    }
    internal static Durable Transition(Durable current, ProofSyncResponse response, ClockReading? reading, TimeSpan requestElapsed = default)
    {
        if (current.Denied) return current;
        // Signed complete denial is committed even when clock acquisition is unavailable.
        if (response.Denied) return current with { Sequence = response.Sequence, Denied = true, Packages = [], LastSuccessfulRefreshUtc = response.ServerUtc };
        if (reading is null) throw new InvalidOperationException();
        if (requestElapsed < TimeSpan.Zero || requestElapsed > TimeSpan.FromSeconds(30) ||
            (response.ServerUtc - reading.Utc).Duration() > TimeSpan.FromMinutes(5)) throw new InvalidDataException();
        // Full request duration overestimates response transit rather than extending signed expiry
        // when the local wall clock is behind. Never rewrite any signed grant timestamps.
        var receiptUtc = response.ServerUtc.Add(requestElapsed);
        var high = receiptUtc > reading.Utc ? receiptUtc : reading.Utc;
        return new(response.Sequence, false, new LeaseState(reading.BootId, receiptUtc, reading.Elapsed,
            reading.Utc, reading.Elapsed, false, high), response.Packages) {
                LastSuccessfulRefreshUtc = response.ServerUtc,
                OriginalGrantExpiresUtc = current.OriginalGrantExpiresUtc ??
                    current.Packages.Select(p => (DateTimeOffset?)p.Grant.Claims.ExpiresAt).Min() ??
                    response.Packages.Select(p => (DateTimeOffset?)p.Grant.Claims.ExpiresAt).Min()
            };
    }
    internal static Durable PrepareRefresh(Durable current, ProofSyncResponse response, Settings settings, string nonce,
        RSA signer, RSA unwrapKey, ClockReading? reading, TimeSpan requestElapsed)
    {
        ValidateResponse(response, settings, nonce, signer, current.Sequence);
        if (current.Denied) return current;
        if (response.Denied) {
            if (response.Packages.Length != 0) throw new CryptographicException();
            return Transition(current, response, null);
        }
        if (response.Packages.Length == 0 || reading is null) throw new CryptographicException();
        var proposed = Transition(current, response, reading, requestElapsed);
        using var reader = new SnapshotProofReader(signer.ExportSubjectPublicKeyInfo());
        foreach (var package in response.Packages) {
            byte[]? key = null, clear = null;
            try {
                var claims = package.Grant.Claims;
                key = unwrapKey.Decrypt(package.WrappedKey, RSAEncryptionPadding.OaepSHA256);
                var opened = reader.Open(package.Grant, package.Snapshot, key,
                    new OpenContext(claims.UserSid, settings.DeviceId, claims.VaultId, claims.ContentVersion, claims.Revision, claims.DenyWatermark),
                    proposed.Lease!, reading);
                clear = opened.Plaintext;
                if (clear is null) throw new CryptographicException();
            }
            finally {
                if (clear is not null) CryptographicOperations.ZeroMemory(clear);
                if (key is not null) CryptographicOperations.ZeroMemory(key);
            }
        }
        return proposed;
    }
    // Transport only: persisted state/config retain the original PascalCase schema.
    internal static readonly JsonSerializerOptions TransportJson = new(JsonSerializerDefaults.Web) {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        RespectRequiredConstructorParameters = true
    };
    internal static ProofSyncRequest CreateRequest(string deviceId) => new(deviceId, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    internal static ProofSyncResponse ParseResponse(byte[] bytes) => JsonSerializer.Deserialize<ProofSyncResponse>(bytes, TransportJson) ?? throw new InvalidDataException();
    internal async Task Refresh(CancellationToken token)
    {
        RefreshStage = "load-config";
        // No credentials/cookies, no TLS bypass, no redirects (including to another HTTPS origin).
        var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllBytes(Path.Combine(root, "config.json"))) ?? throw new InvalidDataException();
        if (string.IsNullOrWhiteSpace(settings.DeviceId) || !Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != "https" || endpoint.AbsolutePath != "/sprint1/sync" || !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.Fragment)) throw new InvalidDataException();
        RefreshStage = "client-certificate";
        using var publicCert = X509CertificateLoader.LoadCertificateFromFile(Path.Combine(root, "client.cer"));
        using var cert = publicCert.CopyWithPrivateKey(auth);
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, CheckCertificateRevocationList = true };
        handler.ClientCertificates.Add(cert);
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 8 * 1024 * 1024 };
        var request = CreateRequest(settings.DeviceId);
        string nonce = request.Nonce;
        long requestStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        RefreshStage = "transport";
        using var reply = await client.PostAsJsonAsync(endpoint, request, TransportJson, token);
        reply.EnsureSuccessStatusCode();
        byte[] bytes = await reply.Content.ReadAsByteArrayAsync(token);
        RefreshStage = "parse-response";
        var response = ParseResponse(bytes);
        using var signer = LoadSigner();
        await WithGate(gate, () => {
            RefreshStage = "verify-envelope";
            if (storageFault) throw new IOException("Durable storage fault latched");
            ValidateResponse(response, settings, nonce, signer, state.Sequence);
            if (state.Denied) return Task.CompletedTask;
            RefreshStage = "native-clock";
            var reading = response.Denied ? null : Native.Clock();
            RefreshStage = "validate-packages";
            state = PrepareRefresh(state, response, settings, nonce, signer, unwrap,
                reading, System.Diagnostics.Stopwatch.GetElapsedTime(requestStarted));
            RefreshStage = "commit";
            Save(); // Failed sync never reaches or renews durable lease.
            RefreshStage = "complete";
            return Task.CompletedTask;
        }, token);
    }
    RSA LoadSigner()
    {
        byte[] pin = File.ReadAllBytes(Path.Combine(root, "signer.spki"));
        var signer = RSA.Create();
        try { signer.ImportSubjectPublicKeyInfo(pin, out var used); if (used != pin.Length || signer.KeySize < 2048) throw new CryptographicException(); return signer; }
        catch { signer.Dispose(); throw; }
    }
    internal static async Task WithGate(SemaphoreSlim sharedGate, Func<Task> operation, CancellationToken token)
    {
        await sharedGate.WaitAsync(token);
        try { await operation(); }
        finally { sharedGate.Release(); }
    }
    internal static bool CanRelease(Durable current, long expectedSequence, Grant claims, ClockReading reading)
    {
        if (current.Denied || current.Sequence != expectedSequence || current.Lease is null) return false;
        var decision = LeaseClock.Advance(current.Lease, reading);
        return decision.Allowed && claims.IssuedAt <= decision.EffectiveUtc && claims.ExpiresAt > decision.EffectiveUtc;
    }
    // Caller holds the same WithGate scope used by Refresh through this entire await.
    internal static async Task<bool> ReleaseCurrent(Durable current, long generation, Grant claims, byte[] data,
        Func<ClockReading> clock, Func<bool> callerMatches, Func<byte[], CancellationToken, Task> send, CancellationToken token)
    {
        if (!callerMatches()) return false;
        var finalReading = clock();
        if (!CanRelease(current, generation, claims, finalReading) || data.Length > 4 * 1024 * 1024) return false;
        var effective = LeaseClock.Advance(current.Lease!, finalReading).EffectiveUtc;
        using var dispatchBudget = CancellationTokenSource.CreateLinkedTokenSource(token);
        var remaining = claims.ExpiresAt - effective;
        dispatchBudget.CancelAfter(remaining < TimeSpan.FromSeconds(2) ? remaining : TimeSpan.FromSeconds(2));
        token.ThrowIfCancellationRequested();
        await send(data, dispatchBudget.Token);
        return true;
    }
    internal async Task Dispatch(string userSid, bool statusOnly, Func<bool> callerMatches,
        Func<byte[], CancellationToken, Task> send, CancellationToken token)
    {
        await WithGate(gate, async () => {
            if (storageFault || state.Lease is null) return;
            long generation = state.Sequence;
            var reading = Native.Clock();
            var decision = LeaseClock.Advance(state.Lease, reading);
            state = state with { Lease = decision.NextState };
            Save();
            if (state.Denied || !decision.Allowed) return;
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllBytes(Path.Combine(root, "config.json"))) ?? throw new InvalidDataException();
            using var signer = LoadSigner();
            using var reader = new SnapshotProofReader(signer.ExportSubjectPublicKeyInfo());
            foreach (var package in state.Packages.Where(p => p.Grant.Claims.UserSid == userSid)) {
                var claims = package.Grant.Claims;
                byte[]? key = null, clear = null;
                try {
                    key = unwrap.Decrypt(package.WrappedKey, RSAEncryptionPadding.OaepSHA256);
                    var result = reader.Open(package.Grant, package.Snapshot, key,
                        new OpenContext(userSid, settings.DeviceId, claims.VaultId, claims.ContentVersion, claims.Revision, claims.DenyWatermark),
                        state.Lease!, reading);
                    clear = result.Plaintext;
                    state = state with { Lease = result.NextState };
                    Save();
                    if (clear is null) continue;
                    // Persist a fresh clock after decryption; re-read once more AFTER all disk writes
                    // and the OS caller/session check, while still serialized against signed deny.
                    var beforeRelease = LeaseClock.Advance(state.Lease!, Native.Clock());
                    state = state with { Lease = beforeRelease.NextState }; Save();
                    if (!await ReleaseCurrent(state, generation, claims, statusOnly ? new byte[] { 1 } : clear,
                        Native.Clock, callerMatches, send, token)) {
                        state = state with { Lease = LeaseClock.Advance(state.Lease!, Native.Clock()).NextState }; Save();
                    }
                    return; // The gate is held through bounded dispatch; no stale clear result escapes.
                }
                catch (CryptographicException) when (!storageFault) { }
                finally {
                    if (clear is not null) CryptographicOperations.ZeroMemory(clear);
                    if (key is not null) CryptographicOperations.ZeroMemory(key);
                }
            }
        }, token);
    }
    Durable Load()
    {
        string path = Path.Combine(root, "state.bin");
        // This method is called only for established stores. Missing files are never empty state.
        var envelope = JsonSerializer.Deserialize<SealedState>(File.ReadAllBytes(path)) ?? throw new InvalidDataException();
        return UnsealState(envelope, unwrap);
    }
    internal static Durable UnsealState(SealedState envelope, RSA unwrapKey)
    {
        byte[] key = unwrapKey.Decrypt(envelope.Key, RSAEncryptionPadding.OaepSHA256);
        byte[] clear = new byte[envelope.Cipher.Length];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(envelope.Nonce, envelope.Cipher, envelope.Tag, clear, "ArkanumGoSyncProof-state-v1"u8);
            var loaded = JsonSerializer.Deserialize<Durable>(clear) ?? throw new InvalidDataException();
            if (loaded.Sequence < 0 || loaded.Packages is null) throw new InvalidDataException();
            return loaded;
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(clear); }
    }
    internal static void CommitSnapshot(Durable current, RSA unwrapKey, bool primaryIdentityMatches, int sessionCount,
        Action<string, byte[]> write, Action invalidate)
    {
        // No two-file transaction exists: invalidate observation first, then commit encrypted state,
        // then publish a hash-bound observation. Absence is unknown, never claimed success.
        invalidate();
        byte[] sealedBytes = JsonSerializer.SerializeToUtf8Bytes(SealState(current, unwrapKey));
        write("state.bin", sealedBytes);
        write("service-status.json", JsonSerializer.SerializeToUtf8Bytes(new {
            Schema = 1, current.Sequence, current.Denied, current.LastSuccessfulRefreshUtc, current.OriginalGrantExpiresUtc,
            GrantExpiresUtc = current.Packages.Select(p => (DateTimeOffset?)p.Grant.Claims.ExpiresAt).Min(),
            ServiceIdentity = "NT SERVICE\\ArkanumGoSyncProof", PrimaryIdentityMatches = primaryIdentityMatches,
            ActiveUnlockedSessionCount = sessionCount, ProcessId = Environment.ProcessId,
            ObservedUtc = DateTimeOffset.UtcNow, StateSha256 = Convert.ToHexString(SHA256.HashData(sealedBytes))
        }));
    }
    void Save()
    {
        try {
            Native.RequireServiceIdentity();
            CommitSnapshot(state, unwrap, true, Native.ActiveUnlockedSessionCount(), Atomic,
                () => File.Delete(Path.Combine(root, "service-status.json")));
        }
        catch { storageFault = true; throw; }
    }
    internal static SealedState SealState(Durable current, RSA unwrapKey)
    {
        byte[] key = RandomNumberGenerator.GetBytes(32), nonce = RandomNumberGenerator.GetBytes(12);
        byte[] clear = JsonSerializer.SerializeToUtf8Bytes(current), cipher = new byte[clear.Length], tag = new byte[16];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, clear, cipher, tag, "ArkanumGoSyncProof-state-v1"u8);
            return new SealedState(unwrapKey.Encrypt(key, RSAEncryptionPadding.OaepSHA256), nonce, cipher, tag);
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(clear); }
    }
    void Atomic(string name, byte[] data)
    {
        string target = Path.Combine(root, name), temporary = Path.Combine(root, name + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { file.Write(data); file.Flush(true); }
            if (!MoveFileExW(temporary, target, 1 | 8)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public void Dispose() { auth.Dispose(); unwrap.Dispose(); gate.Dispose(); }
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool MoveFileExW(string existing, string replacement, uint flags);
}
