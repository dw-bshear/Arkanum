using System.Security.Cryptography;
using System.Text;
using Arkanum.Go.Core;
using Arkanum.Go.Proof.Protocol;

namespace Arkanum.Go.Proof.Windows;

internal static class SelfTest
{
    internal static int Run()
    {
        int checks = 0;
        void Require(bool condition) { if (!condition) throw new InvalidOperationException("Portable assertion failed"); checks++; }
        void Reject(Action action) { try { action(); } catch (Exception e) when (e is CryptographicException or System.Text.Json.JsonException or InvalidDataException) { checks++; return; } throw new InvalidOperationException("Expected rejection"); }
        using var signer = RSA.Create(2048);
        var now = DateTimeOffset.Parse("2026-11-04T12:00:00Z");
        var config = new Settings("synthetic-device", "https://example.invalid/sprint1/sync");
        var response = new ProofSyncResponse(ProofWire.ProtocolId, config.DeviceId, "synthetic-nonce", 1, now, [], false, []);
        response = response with { Signature = signer.SignData(ProofWire.EncodeUnsigned(response), HashAlgorithmName.SHA256, RSASignaturePadding.Pss) };
        Broker.ValidateResponse(response, config, response.Nonce, signer, 0); checks++;
        Reject(() => Broker.ValidateResponse(response, config, "wrong-nonce", signer, 0));
        Reject(() => Broker.ValidateResponse(response, config with { DeviceId = "other-device" }, response.Nonce, signer, 0));
        Reject(() => Broker.ValidateResponse(response, config, response.Nonce, signer, 1));
        Reject(() => Broker.ValidateResponse(response with { Denied = true }, config, response.Nonce, signer, 0));
        var denial = response with { Sequence = 2, Denied = true };
        denial = denial with { Signature = signer.SignData(ProofWire.EncodeUnsigned(denial), HashAlgorithmName.SHA256, RSASignaturePadding.Pss) };
        Broker.ValidateResponse(denial, config, denial.Nonce, signer, 1); checks++;
        var denied = Broker.Transition(new Durable(1, false, null, []), denial, null);
        Require(denied.Denied && denied.Sequence == 2 && denied.Packages.Length == 0);
        Require(Broker.Transition(denied, response, null) == denied);
        var sealedState = Broker.SealState(denied, signer);
        Require(Broker.UnsealState(sealedState, signer).Denied);
        sealedState.Tag[0] ^= 1;
        Reject(() => Broker.UnsealState(sealedState, signer));
        sealedState = Broker.SealState(denied, signer);
        using (var foreignUnwrap = RSA.Create(2048)) Reject(() => Broker.UnsealState(sealedState, foreignUnwrap));
        var anchored = Broker.Transition(new Durable(0, false, null, []), response,
            new ClockReading("synthetic-boot", now.AddSeconds(-5), TimeSpan.FromSeconds(10)));
        var advanced = LeaseClock.Advance(anchored.Lease!,
            new ClockReading("synthetic-boot", now.AddSeconds(-4), TimeSpan.FromSeconds(11)));
        Require(advanced.Allowed && advanced.EffectiveUtc >= now.AddSeconds(1));
        using var wrongSigner = RSA.Create(2048);
        Reject(() => Broker.ValidateResponse(response, config, response.Nonce, wrongSigner, 0));
        Require(Program.ParseRequest("{\"Command\":\"read\"}"u8.ToArray()).Command == "read");
        Reject(() => Program.ParseRequest("{\"Command\":\"read\",\"Sid\":\"forged\"}"u8.ToArray()));
        Reject(() => Program.ParseRequest("{\"Command\":\"renew\"}"u8.ToArray()));
        const string user = "S-1-5-21-111-222-333-1101", domain = "S-1-5-21-111-222-333";
        byte[] clear = Encoding.UTF8.GetBytes("synthetic-proof-value");
        byte[] key = RandomNumberGenerator.GetBytes(32), nonce = RandomNumberGenerator.GetBytes(12), tag = new byte[16], cipher = new byte[clear.Length];
        var claims = new Grant("arkanum-go-offline-grant", 1, "synthetic-grant", config.DeviceId, "synthetic-vault", user, domain,
            1, Convert.ToHexString(SHA256.HashData(clear)), now, now.AddHours(48), 1, 0, false);
        var signed = new SignedGrant(claims, signer.SignData(GrantCanonical.Encode(claims), HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
        using var aes = new AesGcm(key, 16); aes.Encrypt(nonce, clear, cipher, tag, GrantCanonical.Encode(claims));
        using var reader = new SnapshotProofReader(signer.ExportSubjectPublicKeyInfo());
        var lease = new LeaseState("synthetic-boot", now, TimeSpan.Zero, now, TimeSpan.Zero);
        var context = new OpenContext(user, config.DeviceId, claims.VaultId, 1, 1, 0);
        var snapshot = new EncryptedSnapshot(nonce, cipher, tag);
        OpenResult Open(OpenContext ctx, ClockReading clock) => reader.Open(signed, snapshot, key, ctx, lease, clock);
        var good = Open(context, new("synthetic-boot", now.AddMinutes(1), TimeSpan.FromMinutes(1)));
        Require(good.Plaintext is not null && good.Plaintext.AsSpan().SequenceEqual(clear));
        if (good.Plaintext is not null) CryptographicOperations.ZeroMemory(good.Plaintext);
        Require(Open(context with { UserSid = "S-1-5-21-111-222-333-1102" }, new("synthetic-boot", now, TimeSpan.Zero)).Plaintext is null);
        Require(Open(context, new("synthetic-boot", now.AddHours(48), TimeSpan.FromHours(48))).Plaintext is null);
        var rollback = Open(context, new("synthetic-boot", now.AddSeconds(-1), TimeSpan.FromSeconds(1)));
        Require(rollback.Plaintext is null && rollback.NextState.NeedsOnlineRevalidation);
        var subsequent = reader.Open(signed, snapshot, key, context, rollback.NextState, new("synthetic-boot", now.AddMinutes(2), TimeSpan.FromMinutes(2)));
        Require(subsequent.Plaintext is null);
        tag[0] ^= 1;
        Require(Open(context, new("synthetic-boot", now, TimeSpan.Zero)).Plaintext is null);
        CryptographicOperations.ZeroMemory(clear); CryptographicOperations.ZeroMemory(key);
        Console.WriteLine($"PASS portable-proof-windows checks={checks}; native-windows=not-run");
        return 0;
    }
}
