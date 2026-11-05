using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Text.Json;

namespace Arkanum.Go.Proof.Windows;

internal static class Provisioning
{
    internal static void Run(string directory)
    {
        Native.RequireAdministrator(); // Must precede even keystore existence probes.
        Native.RequireTrustedTree(directory);
        byte[] config = File.ReadAllBytes(Path.Combine(directory, "config.json"));
        var settings = JsonSerializer.Deserialize<Settings>(config) ?? throw new InvalidDataException();
        if (string.IsNullOrWhiteSpace(settings.DeviceId) || !Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != "https" || endpoint.AbsolutePath != "/sprint1/sync" || !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.Fragment)) throw new InvalidDataException();
        bool authExists = CngKey.Exists("ArkanumGoSyncProof-auth", CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.MachineKey);
        bool unwrapExists = CngKey.Exists("ArkanumGoSyncProof-unwrap", CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.MachineKey);
        bool marker = File.Exists(Path.Combine(directory, "initialized.marker"));
        bool stateFile = File.Exists(Path.Combine(directory, "state.bin"));
        string receiptPath = Path.Combine(directory, BootstrapReceipt.FileName);
        bool receipt = File.Exists(receiptPath);
        if (authExists || unwrapExists || marker || stateFile || receipt) {
            if (!authExists || !unwrapExists) throw new CryptographicException("Incomplete provisioning; explicit admin recovery required");
            using var existingAuth = Broker.OpenKey("ArkanumGoSyncProof-auth", CngKeyUsages.Signing);
            using var existingUnwrap = Broker.OpenKey("ArkanumGoSyncProof-unwrap", CngKeyUsages.Decryption);
            Broker.ValidateDistinctKeys(existingAuth, existingUnwrap);
            if (Broker.EstablishmentAllowed(marker, true, stateFile) && !receipt) {
                // Authenticate only. Never save, reset sequence/deny/clocks, renew, or rewrite marker.
                _ = Broker.UnsealState(JsonSerializer.Deserialize<SealedState>(File.ReadAllBytes(Path.Combine(directory, "state.bin")))
                    ?? throw new InvalidDataException(), existingUnwrap);
                Console.WriteLine("Existing authenticated state and keys preserved.");
                return;
            }
            if (Broker.BootstrapAllowed(marker, stateFile, true, receipt)) {
                Native.RequireProtectedReceipt(receiptPath);
                BootstrapReceipt.Validate(File.ReadAllBytes(receiptPath), existingAuth.ExportSubjectPublicKeyInfo(),
                    existingUnwrap.ExportSubjectPublicKeyInfo(), config);
                Console.WriteLine("Existing unconsumed provisioning receipt preserved.");
                return;
            }
            throw new CryptographicException("Orphan keys or established state loss; explicit admin recovery required");
        }
        // Enrollment leftovers without authenticated state are not a new installation.
        if (new[] { "auth.csr.pem", "unwrap.spki", "client.cer", "service-status.json" }
            .Any(name => File.Exists(Path.Combine(directory, name)))) throw new CryptographicException("Orphan enrollment files");
        // Never overwrite/delete keys, including on partial failure. No automatic lab recovery.
        CreateKey("ArkanumGoSyncProof-auth", CngKeyUsages.Signing);
        CreateKey("ArkanumGoSyncProof-unwrap", CngKeyUsages.Decryption);
        using var auth = Broker.OpenKey("ArkanumGoSyncProof-auth", CngKeyUsages.Signing);
        using var unwrap = Broker.OpenKey("ArkanumGoSyncProof-unwrap", CngKeyUsages.Decryption);
        Broker.ValidateDistinctKeys(auth, unwrap);
        byte[] bytes = BootstrapReceipt.Create(auth.ExportSubjectPublicKeyInfo(), unwrap.ExportSubjectPublicKeyInfo(), config);
        // CreateNew + protected ACL at creation, no overwrite window. The root itself is protected.
        var acl = new FileSecurity();
        acl.SetSecurityDescriptorSddlForm($"O:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FA;;;{Native.ServiceSid})");
        using (var file = new FileInfo(receiptPath).Create(FileMode.CreateNew, FileSystemRights.FullControl,
            FileShare.None, 4096, FileOptions.WriteThrough, acl)) {
            file.Write(bytes); file.Flush(true);
        }
        Native.RequireProtectedReceipt(receiptPath);
        BootstrapReceipt.Validate(File.ReadAllBytes(receiptPath), auth.ExportSubjectPublicKeyInfo(), unwrap.ExportSubjectPublicKeyInfo(), config);
        Console.WriteLine("Machine keys provisioned; one-use public bootstrap receipt committed.");
    }
    static void CreateKey(string name, CngKeyUsages usage)
    {
        byte[] descriptor = Native.Descriptor($"O:BAD:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;{Native.ServiceSid})");
        using var key = CngKey.Create(CngAlgorithm.Rsa, name, new CngKeyCreationParameters {
            Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
            KeyCreationOptions = CngKeyCreationOptions.MachineKey,
            ExportPolicy = CngExportPolicies.None, KeyUsage = usage,
            Parameters = {
                new CngProperty("Length", BitConverter.GetBytes(3072), CngPropertyOptions.None),
                // DACL_SECURITY_INFORMATION=4; do not confuse it with CngPropertyOptions enum flags.
                new CngProperty("Security Descr", descriptor, (CngPropertyOptions)4 | CngPropertyOptions.Persist)
            }
        });
        // OWNER_SECURITY_INFORMATION=1: a trusted group owner, never the installing user's SID.
        key.SetProperty(new CngProperty("Security Descr", descriptor, (CngPropertyOptions)1 | CngPropertyOptions.Persist));
    }
}
