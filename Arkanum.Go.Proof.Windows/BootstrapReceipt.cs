using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Arkanum.Go.Proof.Windows;

// Public bindings only. Its authority is the installer-owned protected file, not a secret/signature.
internal sealed record BootstrapReceipt(int Schema, string AuthSha256, string UnwrapSha256, string ConfigSha256)
{
    internal const string FileName = "bootstrap-receipt.json";
    static readonly JsonSerializerOptions Strict = new() {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false, RespectRequiredConstructorParameters = true
    };
    static BootstrapReceipt Bind(byte[] auth, byte[] unwrap, byte[] config)
    {
        if (auth.Length == 0 || unwrap.Length == 0 || config.Length == 0 || auth.AsSpan().SequenceEqual(unwrap))
            throw new CryptographicException();
        return new(1, Convert.ToHexString(SHA256.HashData(auth)), Convert.ToHexString(SHA256.HashData(unwrap)),
            Convert.ToHexString(SHA256.HashData(config)));
    }
    internal static byte[] Create(byte[] auth, byte[] unwrap, byte[] config) => JsonSerializer.SerializeToUtf8Bytes(Bind(auth, unwrap, config));
    internal static void Validate(byte[] bytes, byte[] auth, byte[] unwrap, byte[] config)
    {
        var receipt = JsonSerializer.Deserialize<BootstrapReceipt>(bytes, Strict) ?? throw new CryptographicException();
        if (receipt != Bind(auth, unwrap, config)) throw new CryptographicException();
    }
}
