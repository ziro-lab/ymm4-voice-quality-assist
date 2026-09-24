using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ymm4VoiceQualityAssist.Core;

public enum HelperMoraKind
{
    ZeroVowel,
    ZeroConsonant,
}

public sealed record HelperAnchor(
    int Position,
    string Left,
    string Right);

public sealed record HelperMoraRule(
    HelperMoraKind Kind,
    string Helper,
    HelperAnchor Anchor);

public sealed record HelperRuleSet(
    int Version,
    IReadOnlyList<HelperMoraRule> Rules)
{
    public const int CurrentVersion = 1;

    public static HelperRuleSet Empty { get; } =
        new(CurrentVersion, []);
}

public static class HelperRuleCodec
{
    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
        },
    };

    public static string Encode(HelperRuleSet value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Validate(value);
        return JsonSerializer.Serialize(value, Options);
    }

    public static bool TryDecode(
        string? json,
        out HelperRuleSet ruleSet,
        out string? error)
    {
        ruleSet = HelperRuleSet.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
            return true;

        try
        {
            var decoded = JsonSerializer.Deserialize<HelperRuleSet>(
                json,
                Options);

            if (decoded is null)
            {
                error = "Helper rule JSON resolved to null.";
                return false;
            }

            Validate(decoded);
            ruleSet = decoded;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetBaseException().Message;
            return false;
        }
    }

    static void Validate(HelperRuleSet value)
    {
        if (value.Version != HelperRuleSet.CurrentVersion)
        {
            throw new InvalidDataException(
                $"Unsupported helper rule version: {value.Version}.");
        }

        if (value.Rules is null)
            throw new InvalidDataException("Helper rules collection is null.");

        foreach (var rule in value.Rules)
        {
            if (rule is null)
                throw new InvalidDataException("Helper rule is null.");

            if (string.IsNullOrWhiteSpace(rule.Helper))
                throw new InvalidDataException("Helper text is empty.");

            if (rule.Anchor is null)
                throw new InvalidDataException("Helper anchor is null.");

            if (rule.Anchor.Position < 0)
                throw new InvalidDataException("Helper anchor position is negative.");

            if (rule.Anchor.Left is null || rule.Anchor.Right is null)
                throw new InvalidDataException("Helper anchor context is null.");

            if (rule.Anchor.Left.Length == 0
                && rule.Anchor.Right.Length == 0)
            {
                throw new InvalidDataException(
                    "Helper anchor must contain left or right context.");
            }
        }
    }
}

public static class HelperRuleFactory
{
    public const int DefaultContextLength = 8;

    public static HelperMoraRule Create(
        string cleanSerif,
        int position,
        string helper,
        HelperMoraKind kind,
        int contextLength = DefaultContextLength)
    {
        ArgumentNullException.ThrowIfNull(cleanSerif);
        ArgumentNullException.ThrowIfNull(helper);

        if (position < 0 || position > cleanSerif.Length)
            throw new ArgumentOutOfRangeException(nameof(position));

        if (contextLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(contextLength));

        var leftStart = Math.Max(0, position - contextLength);
        var rightLength = Math.Min(
            contextLength,
            cleanSerif.Length - position);

        return new HelperMoraRule(
            kind,
            helper,
            new HelperAnchor(
                position,
                cleanSerif[leftStart..position],
                cleanSerif.Substring(position, rightLength)));
    }
}
