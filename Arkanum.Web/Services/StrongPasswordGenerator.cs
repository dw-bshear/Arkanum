using System.Security.Cryptography;
using Arkanum.Web.Domain;

namespace Arkanum.Web.Services;

public sealed class StrongPasswordGenerator
{
    private const string Uppercase = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lowercase = "abcdefghijkmnopqrstuvwxyz";
    private const string Numbers = "23456789";
    private const string Ambiguous = "O0Il1";

    public string Generate(PasswordGeneratorPolicy policy)
    {
        if (policy.DefaultLength < policy.MinimumLength || policy.DefaultLength > policy.MaximumLength)
        {
            throw new ArgumentOutOfRangeException(nameof(policy), "Default length must be within the configured policy bounds.");
        }
        var requiredSets = BuildRequiredSets(policy);
        if (requiredSets.Count == 0)
        {
            throw new InvalidOperationException("Password policy must allow at least one character class.");
        }
        var allowed = string.Concat(requiredSets);
        var chars = new List<char>();
        foreach (var set in requiredSets) chars.Add(RandomChar(set));
        while (chars.Count < policy.DefaultLength) chars.Add(RandomChar(allowed));
        Shuffle(chars);
        return new string(chars.ToArray());
    }

    private static List<string> BuildRequiredSets(PasswordGeneratorPolicy policy)
    {
        var sets = new List<string>();
        if (policy.IncludeUppercase) sets.Add(Filter(Uppercase, policy));
        if (policy.IncludeLowercase) sets.Add(Filter(Lowercase, policy));
        if (policy.IncludeNumbers) sets.Add(Filter(Numbers, policy));
        if (policy.IncludeSymbols) sets.Add(Filter(policy.SymbolSet, policy));
        return sets.Where(s => s.Length > 0).ToList();
    }

    private static string Filter(string value, PasswordGeneratorPolicy policy) =>
        policy.ExcludeAmbiguousCharacters
            ? new string(value.Where(c => !Ambiguous.Contains(c)).Distinct().ToArray())
            : new string(value.Distinct().ToArray());

    private static char RandomChar(string chars) => chars[RandomNumberGenerator.GetInt32(chars.Length)];

    private static void Shuffle(IList<char> chars)
    {
        for (var i = chars.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
    }
}
