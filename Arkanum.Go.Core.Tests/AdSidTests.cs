using System.Buffers.Binary;
using Arkanum.Go.Core;

namespace Arkanum.Go.Core.Tests;

public class AdSidTests
{
    public const string Domain = "S-1-5-21-100-200-4294967295";
    public const string User = Domain + "-1001";

    [Fact]
    public void Canonical_domain_and_user_are_preserved()
    {
        Assert.Equal(Domain, AdSid.ParseDomain(Domain));
        Assert.Equal(User, AdSid.ParseUser(User, Domain));
    }

    [Theory]
    [InlineData("s-1-5-21-100-200-300")]
    [InlineData("S-1-5-21-0100-200-300")]
    [InlineData(" S-1-5-21-100-200-300")]
    [InlineData("S-1-5-21-100-200-300 ")]
    [InlineData("S-1-5-21-+100-200-300")]
    [InlineData("S-1-5-21-100-200-4294967296")]
    [InlineData("S-1-5-21-100-200")]
    [InlineData("S-1-5-21-100-200-300-400")]
    [InlineData("S-2-5-21-100-200-300")]
    [InlineData("S-1-6-21-100-200-300")]
    [InlineData("S-1-5-22-100-200-300")]
    [InlineData("S-1-5-21-١-200-300")]
    public void Invalid_domains_are_rejected(string value) => Assert.Throws<FormatException>(() => AdSid.ParseDomain(value));

    [Theory]
    [InlineData("S-1-5-21-100-200-4294967295")]
    [InlineData("S-1-5-21-100-200-4294967295-01")]
    [InlineData("S-1-5-21-100-200-4294967295-4294967296")]
    [InlineData("S-1-5-21-100-200-4294967295-1-2")]
    [InlineData("S-1-5-21-100-200-300-1001")]
    public void Invalid_or_other_domain_users_are_rejected(string value) => Assert.Throws<FormatException>(() => AdSid.ParseUser(value, Domain));

    internal static byte[] ObjectSid()
    {
        byte[] bytes = new byte[28];
        bytes[0] = 1; bytes[1] = 5; bytes[7] = 5;
        uint[] values = [21, 100, 200, uint.MaxValue, 1001];
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8 + i * 4), values[i]);
        return bytes;
    }

    [Fact]
    public void Binary_objectSid_decodes_without_repurposing_ExternalId() => Assert.Equal(User, AdSid.DecodeObjectSid(ObjectSid(), Domain));

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(7)] [InlineData(27)]
    public void Truncated_objectSid_is_rejected(int length) => Assert.Throws<FormatException>(() => AdSid.DecodeObjectSid(ObjectSid()[..length], Domain));

    [Theory]
    [InlineData(0, 2)] [InlineData(1, 4)] [InlineData(1, 6)] [InlineData(2, 1)] [InlineData(7, 6)] [InlineData(8, 22)]
    public void Malformed_binary_sid_is_rejected(int index, byte value)
    {
        var bytes = ObjectSid(); bytes[index] = value;
        Assert.Throws<FormatException>(() => AdSid.DecodeObjectSid(bytes, Domain));
    }

    [Fact]
    public void Extra_bytes_or_mismatched_binary_domain_are_rejected()
    {
        Assert.Throws<FormatException>(() => AdSid.DecodeObjectSid([.. ObjectSid(), 0], Domain));
        Assert.Throws<FormatException>(() => AdSid.DecodeObjectSid(ObjectSid(), "S-1-5-21-100-200-300"));
    }
}
