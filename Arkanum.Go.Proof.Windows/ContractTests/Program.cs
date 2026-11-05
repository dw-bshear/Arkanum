using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arkanum.Go.Core;
using Arkanum.Go.Proof.Protocol;
using Arkanum.Go.Proof.Windows;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

var failures = new List<string>();
void Check(string name, Action test) { try { test(); Console.WriteLine("PASS " + name); } catch (Exception e) { failures.Add(name); Console.WriteLine("FAIL " + name + ": " + e.GetType().Name); } }
void Require(bool condition) { if (!condition) throw new InvalidOperationException(); }
void Reject(Action action) { try { action(); } catch (JsonException) { return; } throw new InvalidOperationException(); }
Check("response-remains-connected-until-validated-client-ack", () => PipeDeliveryTests.Run().GetAwaiter().GetResult());
Check("transport-distinguishes-empty-and-truncated-frames", () => TransportTests.Frames().GetAwaiter().GetResult());
Check("transport-retries-only-busy-with-cancellation-budget", () => TransportTests.Busy().GetAwaiter().GetResult());
Check("transport-recovers-only-known-peer-closure", TransportTests.ClosurePolicy);
Check("transport-accept-and-cleanup-faults-fail-closed", () => TransportTests.AcceptAndCleanupFaults().GetAwaiter().GetResult());
Check("transport-reuses-instance-after-close-partial-and-cancellation", () => TransportTests.Lifecycle().GetAwaiter().GetResult());
if (OperatingSystem.IsWindows())
    Check("transport-native-busy-retry", () => TransportTests.NativeBusy().GetAwaiter().GetResult());
else Console.WriteLine("SKIP transport-native-busy-retry (Windows required)");
using var services = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider();
using var signer = RSA.Create(2048);
var now = DateTimeOffset.Parse("2026-11-04T12:00:00Z");
var settings = new Settings("synthetic-device", "https://example.invalid/sprint1/sync");
var request = Broker.CreateRequest(settings.DeviceId);
Check("request-base64-32-byte-nonce", () => Require(request.Nonce.Length == 44 && Convert.FromBase64String(request.Nonce).Length == 32 && request.Nonce != Broker.CreateRequest(settings.DeviceId).Nonce));
var grant = new Grant("arkanum-go-offline-grant", 1, "synthetic-grant", settings.DeviceId, "synthetic-vault", "S-1-5-21-111-222-333-1101", "S-1-5-21-111-222-333", 1, "synthetic-hash", now, now.AddHours(48), 1, 0, false);
var package = new ProofPackage(new SignedGrant(grant, [1]), new EncryptedSnapshot([1], [2], [3]), [4]);
var response = new ProofSyncResponse(ProofWire.ProtocolId, settings.DeviceId, request.Nonce, 7, now, [package], false, []);
response = response with { Signature = signer.SignData(ProofWire.EncodeUnsigned(response), HashAlgorithmName.SHA256, RSASignaturePadding.Pss) };
// Execute the server's actual ASP.NET Results.Json path, not a hand-written JSON fixture.
var context = new DefaultHttpContext { RequestServices = services };
using var body = new MemoryStream(); context.Response.Body = body;
await Results.Json(response).ExecuteAsync(context);
byte[] bytes = body.ToArray();
Check("server-results-json-shared-record-signature", () => {
    var decoded = Broker.ParseResponse(bytes);
    Broker.ValidateResponse(decoded, settings, request.Nonce, signer, 0);
    Require(ProofWire.EncodeUnsigned(decoded).AsSpan().SequenceEqual(ProofWire.EncodeUnsigned(response)));
    Require(decoded.Packages[0].Grant.Claims == grant);
});
Check("unknown-response-security-field-rejected", () => Reject(() => Broker.ParseResponse(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\"protocol\":", "\"sidOverride\":true,\"protocol\":")))));
Check("unknown-nested-security-field-rejected", () => Reject(() => Broker.ParseResponse(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\"wrappedKey\":", "\"allowOverride\":true,\"wrappedKey\":")))));
Check("missing-security-constructor-field-rejected", () => {
    using var doc = JsonDocument.Parse(bytes);
    var fields = doc.RootElement.EnumerateObject().Where(p => p.Name != "denied").ToDictionary(p => p.Name, p => p.Value.Clone());
    Reject(() => Broker.ParseResponse(JsonSerializer.SerializeToUtf8Bytes(fields)));
});
Check("duplicate-security-field-rejected", () => Reject(() => Broker.ParseResponse(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\"sequence\":7", "\"sequence\":7,\"sequence\":7")))));
Check("case-alias-security-field-rejected", () => Reject(() => Broker.ParseResponse(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\"sequence\":7", "\"sequence\":7,\"Sequence\":7")))));
Check("status-state-commit-before-publication", () => {
    var next = Broker.Transition(new Durable(0, false, null, []), response, new ClockReading("synthetic-boot", now, TimeSpan.Zero));
    Require(next.LastSuccessfulRefreshUtc == now && next.OriginalGrantExpiresUtc == grant.ExpiresAt);
    var writes = new List<string>(); var files = new Dictionary<string, byte[]>();
    Broker.CommitSnapshot(next, signer, true, 2, (name, data) => { writes.Add(name); files[name] = data; }, () => writes.Add("invalidate"));
    Require(writes.SequenceEqual(new[] { "invalidate", "state.bin", "service-status.json" }));
    using var status = JsonDocument.Parse(files["service-status.json"]);
    Require(status.RootElement.GetProperty("Sequence").GetInt64() == 7);
    Require(status.RootElement.GetProperty("StateSha256").GetString() == Convert.ToHexString(SHA256.HashData(files["state.bin"])));
    Require(status.RootElement.GetProperty("ActiveUnlockedSessionCount").GetInt32() == 2);
    var json = Encoding.UTF8.GetString(files["service-status.json"]);
    Require(!json.Contains(settings.DeviceId) && !json.Contains(grant.UserSid) && !json.Contains(grant.VaultId) && !json.Contains(settings.Endpoint));
    var restored = Broker.UnsealState(JsonSerializer.Deserialize<SealedState>(files["state.bin"])!, signer);
    Require(restored.LastSuccessfulRefreshUtc == now && restored.OriginalGrantExpiresUtc == grant.ExpiresAt);
});
Check("failed-state-commit-never-publishes-status", () => {
    var writes = new List<string>();
    try { Broker.CommitSnapshot(new Durable(7, false, null, []), signer, true, 0,
        (name, _) => { writes.Add(name); throw new IOException(); }, () => writes.Add("invalidate")); }
    catch (IOException) { }
    Require(writes.SequenceEqual(new[] { "invalidate", "state.bin" }));
});
Check("renewal-retains-original-expiry-for-observation", () => {
    var first = Broker.Transition(new Durable(0, false, null, []), response, new ClockReading("synthetic-boot", now, TimeSpan.Zero));
    var renewed = response with { Sequence = 8, ServerUtc = now.AddMinutes(1), Packages = [package with { Grant = package.Grant with { Claims = grant with { ExpiresAt = grant.ExpiresAt.AddMinutes(1) } } }] };
    var next = Broker.Transition(first, renewed, new ClockReading("synthetic-boot", now.AddMinutes(1), TimeSpan.FromMinutes(1)));
    Require(next.OriginalGrantExpiresUtc == grant.ExpiresAt && next.LastSuccessfulRefreshUtc == renewed.ServerUtc);
    Require(next.Packages[0].Grant.Claims.ExpiresAt == renewed.Packages[0].Grant.Claims.ExpiresAt);
});
Check("legacy-persisted-schema-remains-readable", () => {
    var old = JsonSerializer.Deserialize<Durable>("{\"Sequence\":3,\"Denied\":false,\"Lease\":null,\"Packages\":[]}")!;
    Require(old.Sequence == 3 && old.LastSuccessfulRefreshUtc is null && old.OriginalGrantExpiresUtc is null);
    var legacyWithPackages = new Durable(3, false, null, [package]);
    var renewed = response with { Packages = [package with { Grant = package.Grant with { Claims = grant with { ExpiresAt = grant.ExpiresAt.AddMinutes(1) } } }] };
    var next = Broker.Transition(legacyWithPackages, renewed, new ClockReading("synthetic-boot", now, TimeSpan.Zero));
    Require(next.OriginalGrantExpiresUtc == grant.ExpiresAt);
});
Check("startup-diagnostic-excludes-message-data", () => {
    var error = new InvalidOperationException("sensitive-message"); error.Data["private"] = "sensitive-data";
    var json = JsonSerializer.Serialize(Native.StartupFailure("synthetic-stage", error));
    Require(!json.Contains("sensitive") && !json.Contains("private") && json.Contains("HResult"));
});
Check("wts-user-binding-and-rdp-policy", () => {
    Require(Native.SessionCallerAllowed("synthetic-a", "synthetic-a", 2, true, false, true, true));
    Require(Native.SessionCallerAllowed("synthetic-a", "synthetic-a", 2, true, true, false, false));
    Require(!Native.SessionCallerAllowed("synthetic-a", "synthetic-b", 2, true, true, false, false));
    Require(!Native.SessionCallerAllowed("synthetic-a", "synthetic-a", 2, false, false, true, false));
    Require(!Native.SessionCallerAllowed("synthetic-a", "synthetic-a", 0, true, true, false, false));
    Require(!Native.SessionCallerAllowed("synthetic-a", "synthetic-a", 2, true, false, false, true));
});
Check("request-rtt-cannot-extend-grant-expiry", () => {
    var next = Broker.Transition(new Durable(0, false, null, []), response,
        new ClockReading("synthetic-boot", now.AddMinutes(-1), TimeSpan.FromSeconds(10)), TimeSpan.FromSeconds(5));
    var advanced = LeaseClock.Advance(next.Lease!, new ClockReading("synthetic-boot", now.AddMinutes(-1), TimeSpan.FromSeconds(10)));
    Require(advanced.EffectiveUtc >= now.AddSeconds(5));
    Require(next.Packages[0].Grant.Claims.ExpiresAt == grant.ExpiresAt);
});
Check("stale-future-or-excessive-rtt-fails-closed", () => {
    foreach (var (stamp, elapsed) in new[] { (now.AddMinutes(-6), TimeSpan.Zero), (now.AddMinutes(6), TimeSpan.Zero), (now, TimeSpan.FromSeconds(31)), (now, TimeSpan.FromSeconds(-1)) }) {
        bool rejected = false;
        try { Broker.Transition(new Durable(0, false, null, []), response with { ServerUtc = stamp }, new ClockReading("synthetic-boot", now, TimeSpan.Zero), elapsed); }
        catch (InvalidDataException) { rejected = true; }
        Require(rejected);
    }
});
using var unwrap = RSA.Create(2048);
ProofSyncResponse Signed(ProofSyncResponse value) => value with { Signature = signer.SignData(ProofWire.EncodeUnsigned(value), HashAlgorithmName.SHA256, RSASignaturePadding.Pss) };
byte[] synthetic = Encoding.UTF8.GetBytes("synthetic-contract-value");
byte[] aesKey = RandomNumberGenerator.GetBytes(32), gcmNonce = RandomNumberGenerator.GetBytes(12), tag = new byte[16], cipher = new byte[synthetic.Length];
var realGrant = grant with { ContentHash = Convert.ToHexString(SHA256.HashData(synthetic)) };
using (var aes = new AesGcm(aesKey, 16)) aes.Encrypt(gcmNonce, synthetic, cipher, tag, GrantCanonical.Encode(realGrant));
var validPackage = new ProofPackage(new SignedGrant(realGrant, signer.SignData(GrantCanonical.Encode(realGrant), HashAlgorithmName.SHA256, RSASignaturePadding.Pss)),
    new EncryptedSnapshot(gcmNonce, cipher, tag), unwrap.Encrypt(aesKey, RSAEncryptionPadding.OaepSHA256));
var validResponse = Signed(response with { Packages = [validPackage] });
var prior = new Durable(1, false, null, []);
Check("proposed-valid-batch-decrypts-before-renewal", () => {
    var next = Broker.PrepareRefresh(prior, validResponse, settings, request.Nonce, signer, unwrap,
        new ClockReading("synthetic-boot", now, TimeSpan.Zero), TimeSpan.FromSeconds(1));
    Require(next.Sequence == validResponse.Sequence && next.Packages.Length == 1 && prior.Sequence == 1 && prior.Lease is null);
});
Check("outer-signed-invalid-batch-preserves-last-valid-state", () => {
    var badTag = validPackage.Snapshot.Tag.ToArray(); badTag[0] ^= 1;
    var candidates = new[] {
        validPackage with { Snapshot = validPackage.Snapshot with { Tag = badTag } },
        validPackage with { Grant = validPackage.Grant with { Signature = new byte[256] } },
        validPackage with { WrappedKey = new byte[256] },
        validPackage with { Grant = validPackage.Grant with { Claims = realGrant with { UserSid = "not-a-sid" } } }
    };
    foreach (var invalid in candidates) {
        bool rejected = false;
        try { Broker.PrepareRefresh(prior, Signed(validResponse with { Packages = [validPackage, invalid] }), settings, request.Nonce, signer, unwrap,
            new ClockReading("synthetic-boot", now, TimeSpan.Zero), TimeSpan.FromSeconds(1)); }
        catch (CryptographicException) { rejected = true; }
        Require(rejected && prior.Sequence == 1 && prior.Lease is null && prior.LastSuccessfulRefreshUtc is null);
    }
});
Check("signed-deny-commits-without-clock-or-package-decryption", () => {
    var denied = Signed(response with { Denied = true, Packages = [] });
    var next = Broker.PrepareRefresh(prior, denied, settings, request.Nonce, signer, unwrap, null, TimeSpan.FromSeconds(99));
    Require(next.Denied && next.Sequence == denied.Sequence && next.Packages.Length == 0);
});
CryptographicOperations.ZeroMemory(aesKey); CryptographicOperations.ZeroMemory(synthetic);
Check("release-expiry-generation-and-deny-guard", () => {
    var lease = new LeaseState("synthetic-boot", now, TimeSpan.Zero, now, TimeSpan.Zero);
    var current = new Durable(7, false, lease, [validPackage]);
    Require(Broker.CanRelease(current, 7, realGrant, new ClockReading("synthetic-boot", now, TimeSpan.Zero)));
    Require(!Broker.CanRelease(current with { Denied = true }, 7, realGrant, new ClockReading("synthetic-boot", now, TimeSpan.Zero)));
    Require(!Broker.CanRelease(current with { Sequence = 8 }, 7, realGrant, new ClockReading("synthetic-boot", now, TimeSpan.Zero)));
    Require(!Broker.CanRelease(current, 7, realGrant, new ClockReading("synthetic-boot", realGrant.ExpiresAt, TimeSpan.FromHours(48))));
});
Check("established-state-loss-does-not-reset-sequence", () => {
    Require(!Broker.EstablishmentAllowed(false, false, false)); // keys now require administrative provisioning
    Require(Broker.EstablishmentAllowed(true, true, true));
    Require(Broker.EstablishmentAllowed(false, true, true)); // authenticated legacy-state migration, never sequence zero
    Require(!Broker.EstablishmentAllowed(true, true, false));
    Require(!Broker.EstablishmentAllowed(false, true, false));
    Require(!Broker.EstablishmentAllowed(false, false, true));
});
Check("persistent-key-security-policy-rejects-untrusted-owner-or-ace", () => {
    var trusted = new[] { "service", "system", "administrators" };
    Require(Native.TrustedDescriptorPolicy("service", true, trusted, trusted));
    Require(!Native.TrustedDescriptorPolicy("other", true, trusted, trusted));
    Require(!Native.TrustedDescriptorPolicy("service", false, trusted, trusted));
    Require(!Native.TrustedDescriptorPolicy("service", true, trusted.Append("user"), trusted));
    Require(!Native.TrustedDescriptorPolicy("service", true, new[] { "administrators" }, trusted));
});
Check("expiry-crossed-during-final-caller-check-does-not-dispatch", () => {
    var lease = new LeaseState("synthetic-boot", now, TimeSpan.Zero, now, TimeSpan.Zero);
    var current = new Durable(7, false, lease, [validPackage]);
    var nearExpiry = realGrant with { ExpiresAt = now.AddSeconds(1) };
    var clock = new ClockReading("synthetic-boot", now, TimeSpan.Zero);
    bool sent = false;
    bool released = Broker.ReleaseCurrent(current, 7, nearExpiry, [1], () => clock,
        () => { clock = new ClockReading("synthetic-boot", now.AddSeconds(1), TimeSpan.FromSeconds(1)); return true; },
        (_, _) => { sent = true; return Task.CompletedTask; }, CancellationToken.None).GetAwaiter().GetResult();
    Require(!released && !sent);
});
Check("persistent-auth-and-unwrap-must-be-distinct", () => {
    Broker.ValidateDistinctKeys(signer, unwrap);
    bool rejected = false;
    try { Broker.ValidateDistinctKeys(signer, signer); } catch (CryptographicException) { rejected = true; }
    Require(rejected);
});
using (var releaseGate = new SemaphoreSlim(1, 1)) {
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    bool denyCommitted = false, sent = false;
    var releaseTask = Broker.WithGate(releaseGate, async () => { entered.SetResult(); await finish.Task; Require(!denyCommitted); sent = true; }, CancellationToken.None);
    await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
    var denialTask = Broker.WithGate(releaseGate, () => { denyCommitted = true; return Task.CompletedTask; }, CancellationToken.None);
    Require(!denialTask.IsCompleted && !denyCommitted);
    finish.SetResult(); await Task.WhenAll(releaseTask, denialTask).WaitAsync(TimeSpan.FromSeconds(3));
    Check("deny-commit-cannot-interleave-with-release", () => Require(sent && denyCommitted));
    bool leaked = false;
    await Broker.WithGate(releaseGate, () => { if (!denyCommitted) leaked = true; return Task.CompletedTask; }, CancellationToken.None);
    Check("committed-deny-prevents-later-release", () => Require(!leaked));
}
// Reflection keeps RED executable before the provisioning API exists.
object? Feature(string type, string method, params object?[] args) {
    var feature = typeof(Broker).Assembly.GetType("Arkanum.Go.Proof.Windows." + type)
        ?? throw new InvalidOperationException("Missing provisioning feature");
    var member = feature.GetMethod(method, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Missing provisioning method");
    return member.Invoke(null, args);
}
Check("bootstrap-requires-pristine-marker-state-and-both-keys", () => {
    foreach (bool marker in new[] { false, true }) foreach (bool stateFile in new[] { false, true })
    foreach (bool bothKeys in new[] { false, true }) foreach (bool receipt in new[] { false, true }) {
        bool allowed = (bool)Feature("Broker", "BootstrapAllowed", marker, stateFile, bothKeys, receipt)!;
        Require(allowed == (!marker && !stateFile && bothKeys && receipt));
    }
});
Check("receipt-binds-distinct-public-keys-and-exact-config", () => {
    byte[] authPublic = signer.ExportSubjectPublicKeyInfo(), unwrapPublic = unwrap.ExportSubjectPublicKeyInfo();
    byte[] config = "synthetic-config-v1"u8.ToArray();
    byte[] receipt = (byte[])Feature("BootstrapReceipt", "Create", authPublic, unwrapPublic, config)!;
    Feature("BootstrapReceipt", "Validate", receipt, authPublic, unwrapPublic, config);
    void MustReject(byte[] data, byte[] a, byte[] u, byte[] c) {
        bool rejected = false;
        try { Feature("BootstrapReceipt", "Validate", data, a, u, c); }
        catch (System.Reflection.TargetInvocationException e) when (e.InnerException is CryptographicException or JsonException) { rejected = true; }
        Require(rejected);
    }
    MustReject(receipt, unwrapPublic, authPublic, config);
    MustReject(receipt, authPublic, authPublic, config);
    MustReject(receipt, authPublic, unwrapPublic, "changed-config"u8.ToArray());
    var json = Encoding.UTF8.GetString(receipt);
    Require(!json.Contains(Convert.ToBase64String(authPublic)) && !json.Contains(Convert.ToBase64String(unwrapPublic)));
    MustReject(Encoding.UTF8.GetBytes(json.Replace("\"Schema\":1", "\"Schema\":2")), authPublic, unwrapPublic, config);
    MustReject(Encoding.UTF8.GetBytes(json.Replace("\"Schema\":1", "\"Schema\":1,\"Schema\":1")), authPublic, unwrapPublic, config);
    MustReject(Encoding.UTF8.GetBytes(json.Replace("\"Schema\":1", "\"Unknown\":true,\"Schema\":1")), authPublic, unwrapPublic, config);
    MustReject("{}"u8.ToArray(), authPublic, unwrapPublic, config);
});
Check("virtual-service-primary-SID-does-not-require-duplicate-group", () => {
    const string service = "S-1-5-80-1-2-3-4-5";
    Require(Native.ServiceTokenMatches(service, service));
    Require(!Native.ServiceTokenMatches("S-1-5-18", service));
    Require(!Native.ServiceTokenMatches(null, service));
    Require(!Native.ServiceTokenMatches("", ""));
});
Check("sleep-inclusive-clock-uses-exported-realtime-api-contract", () => {
    var method = typeof(Native).GetMethod("QueryInterruptTime", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
    var import = (System.Runtime.InteropServices.DllImportAttribute)Attribute.GetCustomAttribute(method, typeof(System.Runtime.InteropServices.DllImportAttribute))!;
    Require(import.Value == "api-ms-win-core-realtime-l1-1-2.dll" && import.ExactSpelling);
});
return failures.Count == 0 ? 0 : 1;
