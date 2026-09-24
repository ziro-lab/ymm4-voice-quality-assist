using System.IO;
using System.Collections;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ymm4VoiceQualityAssist.Effects;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4VoiceQualityAssist.Core;

public sealed record SourceFingerprintHelperRule(
    HelperMoraKind Kind,
    string Helper,
    int Position,
    string Left,
    string Right);

public sealed record SourceFingerprintAssistProfile(
    ProsodyGesture Prosody,
    IReadOnlyList<SourceFingerprintHelperRule> HelperRules);

public sealed record SourceFingerprintInput(
    string? CharacterName,
    string? Serif,
    string? Hatsuon,
    IReadOnlyList<SourceFingerprintAssistProfile> AssistProfiles);

public sealed record SourceFingerprintResult(
    string Fingerprint,
    string CanonicalJson);

public static class SourceFingerprint
{
    public static bool TryCreate(
        VoiceItem voice,
        out SourceFingerprintResult? result,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(voice);

        var profiles =
            new List<SourceFingerprintAssistProfile>();

        if (voice.JimakuVideoEffects
            is IEnumerable effects)
        {
            foreach (var value in effects)
            {
                if (value
                    is not PronunciationAssistEffect
                    {
                        IsEnabled: true,
                    } effect)
                {
                    continue;
                }

                if (!HelperRuleCodec.TryDecode(
                    effect.HelperRulesJson,
                    out var decoded,
                    out var decodeError))
                {
                    result = null;
                    error =
                        decodeError
                        ?? "Enabled Assist Effect has invalid helper-rule JSON.";
                    return false;
                }

                profiles.Add(
                    new SourceFingerprintAssistProfile(
                        effect.Prosody,
                        CanonicalizeHelperRules(
                            decoded.Rules)));
            }
        }

        result = Create(
            new SourceFingerprintInput(
                voice.CharacterName,
                voice.Serif,
                voice.Hatsuon,
                profiles));

        error = null;
        return true;
    }

    public static SourceFingerprintResult Create(
        SourceFingerprintInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(
            input.AssistProfiles);

        var profiles = input.AssistProfiles
            .Select(CanonicalizeProfile)
            .OrderBy(
                ProfileSortKey,
                StringComparer.Ordinal)
            .ToArray();

        var buffer =
            new MemoryStream();

        using (var writer =
            new Utf8JsonWriter(
                buffer,
                new JsonWriterOptions
                {
                    Indented = false,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                }))
        {
            writer.WriteStartObject();

            WriteNullableString(
                writer,
                "characterName",
                input.CharacterName);

            WriteNullableString(
                writer,
                "serif",
                input.Serif);

            WriteNullableString(
                writer,
                "hatsuon",
                input.Hatsuon);

            writer.WritePropertyName(
                "assistSource");

            writer.WriteStartObject();

            writer.WriteBoolean(
                "enabled",
                profiles.Length > 0);

            writer.WritePropertyName(
                "profiles");

            writer.WriteStartArray();

            foreach (var profile in profiles)
                WriteProfile(writer, profile);

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.Flush();
        }

        var bytes = buffer.ToArray();
        var json =
            Encoding.UTF8.GetString(bytes);

        var hash =
            SHA256.HashData(bytes);

        return new SourceFingerprintResult(
            "sha256:"
            + Convert.ToHexString(hash)
                .ToLowerInvariant(),
            json);
    }

    static SourceFingerprintAssistProfile
        CanonicalizeProfile(
            SourceFingerprintAssistProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(
            profile.HelperRules);

        return new SourceFingerprintAssistProfile(
            profile.Prosody,
            profile.HelperRules
                .OrderBy(
                    x => x.Position)
                .ThenBy(
                    x => x.Left,
                    StringComparer.Ordinal)
                .ThenBy(
                    x => x.Right,
                    StringComparer.Ordinal)
                .ThenBy(
                    x => x.Kind)
                .ThenBy(
                    x => x.Helper,
                    StringComparer.Ordinal)
                .ToArray());
    }

    static IReadOnlyList<SourceFingerprintHelperRule>
        CanonicalizeHelperRules(
            IReadOnlyList<HelperMoraRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        return rules
            .Select(x =>
                new SourceFingerprintHelperRule(
                    x.Kind,
                    x.Helper,
                    x.Anchor.Position,
                    x.Anchor.Left,
                    x.Anchor.Right))
            .OrderBy(x => x.Position)
            .ThenBy(
                x => x.Left,
                StringComparer.Ordinal)
            .ThenBy(
                x => x.Right,
                StringComparer.Ordinal)
            .ThenBy(x => x.Kind)
            .ThenBy(
                x => x.Helper,
                StringComparer.Ordinal)
            .ToArray();
    }

    static string ProfileSortKey(
        SourceFingerprintAssistProfile profile)
    {
        var buffer =
            new MemoryStream();

        using (var writer =
            new Utf8JsonWriter(
                buffer,
                new JsonWriterOptions
                {
                    Indented = false,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                }))
        {
            WriteProfile(writer, profile);
            writer.Flush();
        }

        return Encoding.UTF8.GetString(
            buffer.ToArray());
    }

    static void WriteProfile(
        Utf8JsonWriter writer,
        SourceFingerprintAssistProfile profile)
    {
        writer.WriteStartObject();

        writer.WriteString(
            "prosody",
            ToCanonicalProsody(
                profile.Prosody));

        writer.WritePropertyName(
            "helperRules");

        writer.WriteStartArray();

        foreach (var rule
            in profile.HelperRules)
        {
            writer.WriteStartObject();

            writer.WriteString(
                "kind",
                rule.Kind switch
                {
                    HelperMoraKind.ZeroVowel =>
                        "zeroVowel",
                    HelperMoraKind.ZeroConsonant =>
                        "zeroConsonant",
                    _ => throw new InvalidDataException(
                        "Unsupported helper kind: "
                        + rule.Kind),
                });

            writer.WriteString(
                "helper",
                rule.Helper);

            writer.WriteNumber(
                "position",
                rule.Position);

            writer.WriteString(
                "left",
                rule.Left);

            writer.WriteString(
                "right",
                rule.Right);

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    static string ToCanonicalProsody(
        ProsodyGesture gesture) =>
        gesture switch
        {
            ProsodyGesture.None =>
                "none",
            ProsodyGesture.LightRise =>
                "lightRise",
            ProsodyGesture.LightFall =>
                "lightFall",
            ProsodyGesture.Hold =>
                "hold",
            _ => throw new InvalidDataException(
                "Unsupported prosody gesture: "
                + gesture),
        };

    static void WriteNullableString(
        Utf8JsonWriter writer,
        string propertyName,
        string? value)
    {
        writer.WritePropertyName(
            propertyName);

        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value);
    }
}

public sealed record ReviewTargetLocator(
    int? Frame,
    int? Layer,
    string? CharacterName,
    string? PreviousSerif,
    string? NextSerif);

public sealed record ReviewTargetCandidate<T>(
    T Item,
    string SourceFingerprint,
    int Frame,
    int Layer,
    string? CharacterName,
    string? PreviousSerif,
    string? NextSerif)
    where T : class;

public enum ReviewTargetResolutionStatus
{
    ExactFingerprintMatch,
    Stale,
    Missing,
    Ambiguous,
}

public sealed record ReviewTargetResolution<T>(
    ReviewTargetResolutionStatus Status,
    T? Item,
    int CandidateCount,
    string? Message)
    where T : class
{
    public bool CanAutoApply =>
        Status
        == ReviewTargetResolutionStatus
            .ExactFingerprintMatch;
}

public static class ReviewTargetResolver
{
    public static ReviewTargetResolution<T>
        Resolve<T>(
            string expectedFingerprint,
            ReviewTargetLocator locator,
            IReadOnlyList<
                ReviewTargetCandidate<T>>
                candidates)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            expectedFingerprint);
        ArgumentNullException.ThrowIfNull(
            locator);
        ArgumentNullException.ThrowIfNull(
            candidates);

        var exact = candidates
            .Where(x =>
                string.Equals(
                    x.SourceFingerprint,
                    expectedFingerprint,
                    StringComparison.Ordinal))
            .ToArray();

        if (exact.Length == 1)
        {
            return new ReviewTargetResolution<T>(
                ReviewTargetResolutionStatus
                    .ExactFingerprintMatch,
                exact[0].Item,
                1,
                null);
        }

        if (exact.Length > 1)
        {
            return new ReviewTargetResolution<T>(
                ReviewTargetResolutionStatus
                    .Ambiguous,
                default,
                exact.Length,
                "More than one item has the exact source fingerprint.");
        }

        var located = candidates
            .Where(x =>
                MatchesLocator(
                    x,
                    locator))
            .ToArray();

        if (located.Length == 1)
        {
            return new ReviewTargetResolution<T>(
                ReviewTargetResolutionStatus
                    .Stale,
                located[0].Item,
                1,
                "Locator identifies one item, but the source fingerprint changed.");
        }

        if (located.Length == 0)
        {
            return new ReviewTargetResolution<T>(
                ReviewTargetResolutionStatus
                    .Missing,
                default,
                0,
                "No item matches the source fingerprint or locator.");
        }

        return new ReviewTargetResolution<T>(
            ReviewTargetResolutionStatus
                .Ambiguous,
            default,
            located.Length,
            "Locator matches more than one item.");
    }

    static bool MatchesLocator<T>(
        ReviewTargetCandidate<T> candidate,
        ReviewTargetLocator locator)
        where T : class
    {
        if (locator.Frame is { } frame
            && candidate.Frame != frame)
        {
            return false;
        }

        if (locator.Layer is { } layer
            && candidate.Layer != layer)
        {
            return false;
        }

        if (locator.CharacterName
            is { } characterName
            && !string.Equals(
                candidate.CharacterName,
                characterName,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (locator.PreviousSerif
            is { } previous
            && !string.Equals(
                candidate.PreviousSerif,
                previous,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (locator.NextSerif
            is { } next
            && !string.Equals(
                candidate.NextSerif,
                next,
                StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }
}
