using System.Security.Cryptography;

namespace Arkanum.Web.Services;

public sealed class LocalPasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 210_000;

    public string Hash(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
        {
            throw new ArgumentException("Password must be at least 12 characters long.", nameof(password));
        }

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(hash)) return false;
        var parts = hash.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256") return false;
        if (!int.TryParse(parts[1], out var iterations)) return false;

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
