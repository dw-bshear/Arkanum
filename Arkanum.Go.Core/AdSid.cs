using System.Buffers.Binary;
using System.Globalization;

namespace Arkanum.Go.Core;

public static class AdSid
{
    public static string ParseDomain(string value)
    {
        Validate(value, 7);
        return value;
    }

    public static string ParseUser(string value, string domainSid)
    {
        ParseDomain(domainSid);
        Validate(value, 8);
        if (!value.StartsWith(domainSid + "-", StringComparison.Ordinal)) throw new FormatException("SID domain mismatch.");
        return value;
    }

    private static void Validate(string value, int count)
    {
        var parts = value?.Split('-');
        if (parts is null || parts.Length != count || parts[0] != "S" || parts[1] != "1" || parts[2] != "5" || parts[3] != "21")
            throw new FormatException("Not a canonical AD SID.");
        foreach (var part in parts.Skip(4))
            if (!uint.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ||
                part != number.ToString(CultureInfo.InvariantCulture)) throw new FormatException("Noncanonical SID subauthority.");
    }

    public static string DecodeObjectSid(ReadOnlySpan<byte> bytes, string domainSid)
    {
        if (bytes.Length != 28 || bytes[0] != 1 || bytes[1] != 5 ||
            !bytes.Slice(2, 6).SequenceEqual(new byte[] { 0, 0, 0, 0, 0, 5 })) throw new FormatException("Invalid AD objectSid.");
        var parts = new string[5];
        for (var i = 0; i < 5; i++) parts[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(8 + i * 4, 4)).ToString(CultureInfo.InvariantCulture);
        return ParseUser("S-1-5-" + string.Join('-', parts), domainSid);
    }
}
