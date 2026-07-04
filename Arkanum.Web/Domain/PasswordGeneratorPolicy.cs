namespace Arkanum.Web.Domain;

public sealed record PasswordGeneratorPolicy
{
    public int MinimumLength { get; init; } = 20;
    public int DefaultLength { get; init; } = 24;
    public int MaximumLength { get; init; } = 128;
    public bool IncludeUppercase { get; init; } = true;
    public bool IncludeLowercase { get; init; } = true;
    public bool IncludeNumbers { get; init; } = true;
    public bool IncludeSymbols { get; init; } = true;
    public bool ExcludeAmbiguousCharacters { get; init; } = true;
    public string SymbolSet { get; init; } = "!@#$%^&*()-_=+[]{}:,.?";

    public static PasswordGeneratorPolicy Default { get; } = new();
}
