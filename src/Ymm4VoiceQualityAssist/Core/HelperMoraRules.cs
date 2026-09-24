namespace Ymm4VoiceQualityAssist.Core;

public enum HelperMoraKind
{
    ZeroVowel,
    ZeroConsonant,
}

public sealed record HelperAnchor(
    int Position,
    string LeftContext,
    string RightContext);

public sealed record HelperMoraRule(
    string Id,
    HelperMoraKind Kind,
    string Helper,
    HelperAnchor Anchor);

public sealed record HelperMoraRuleSet(
    int Version,
    HelperMoraRule[] Rules)
{
    public const int CurrentVersion = 1;

    public static HelperMoraRuleSet Empty { get; } =
        new(CurrentVersion, []);
}
