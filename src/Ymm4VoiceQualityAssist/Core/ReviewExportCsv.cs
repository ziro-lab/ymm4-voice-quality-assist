using System.Globalization;
using System.Text;

namespace Ymm4VoiceQualityAssist.Core;

public static class ReviewExportCsv
{
    static readonly string[] Header =
    [
        "schema",
        "exportSessionId",
        "exportedAt",
        "exportRef",
        "exportIndex",
        "frame",
        "layer",
        "characterName",
        "speakerApi",
        "speakerId",
        "previousSerif",
        "serif",
        "hatsuon",
        "nextSerif",
        "cleanText",
        "boundaries",
        "prosody",
        "helperRules",
        "hasGeneratedPronounce",
        "generatedMoraReading",
        "accentPhraseCount",
        "sourceFingerprint",
    ];

    public static string Serialize(
        ReviewExportPackage package)
    {
        ArgumentNullException.ThrowIfNull(
            package);

        var builder =
            new StringBuilder();

        AppendRow(
            builder,
            Header);

        foreach (var voice in package.Voices)
        {
            AppendRow(
                builder,
                [
                    package.Schema,
                    package.ExportSessionId,
                    package.ExportedAt
                        .ToUniversalTime()
                        .ToString(
                            "O",
                            CultureInfo.InvariantCulture),
                    voice.Target.ExportRef,
                    voice.Target.ExportIndex
                        .ToString(
                            CultureInfo.InvariantCulture),
                    voice.Target.Frame
                        .ToString(
                            CultureInfo.InvariantCulture),
                    voice.Target.Layer
                        .ToString(
                            CultureInfo.InvariantCulture),
                    voice.CharacterName
                        ?? string.Empty,
                    voice.Speaker.Api
                        ?? string.Empty,
                    voice.Speaker.Id
                        ?? string.Empty,
                    voice.Context.PreviousSerif
                        ?? string.Empty,
                    voice.Serif
                        ?? string.Empty,
                    voice.Hatsuon
                        ?? string.Empty,
                    voice.Context.NextSerif
                        ?? string.Empty,
                    voice.Controls.CleanText,
                    FormatBoundaries(
                        voice.Controls),
                    FormatProsody(
                        voice.Assist),
                    FormatHelpers(
                        voice.Assist),
                    voice.Pronunciation
                        .HasGeneratedPronounce
                        .ToString(
                            CultureInfo.InvariantCulture),
                    voice.Pronunciation
                        .GeneratedMoraReading
                        ?? string.Empty,
                    voice.Pronunciation
                        .AccentPhraseCount
                        ?.ToString(
                            CultureInfo.InvariantCulture)
                        ?? string.Empty,
                    voice.SourceFingerprint,
                ]);
        }

        return builder.ToString();
    }

    static string FormatBoundaries(
        ReviewVoiceControls controls) =>
        string.Join(
            ";",
            controls.Boundaries
                .Select(x =>
                    $"{x.Position}:{x.Source}"));

    static string FormatProsody(
        ReviewVoiceAssistSettings assist) =>
        string.Join(
            "|",
            assist.Profiles
                .Select(x =>
                    ToCanonicalProsody(
                        x.Prosody)));

    static string FormatHelpers(
        ReviewVoiceAssistSettings assist) =>
        string.Join(
            ";",
            assist.Profiles
                .SelectMany(x =>
                    x.HelperRules)
                .Select(FormatHelper));

    static string FormatHelper(
        SourceFingerprintHelperRule rule)
    {
        var kind = rule.Kind switch
        {
            HelperMoraKind.ZeroVowel =>
                "zeroVowel",
            HelperMoraKind.ZeroConsonant =>
                "zeroConsonant",
            _ => rule.Kind.ToString(),
        };

        return
            $"{kind}:{rule.Helper}@{rule.Position}"
            + $"[{rule.Left}|{rule.Right}]";
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
            _ => gesture.ToString(),
        };

    static void AppendRow(
        StringBuilder builder,
        IReadOnlyList<string> values)
    {
        for (var index = 0;
             index < values.Count;
             index++)
        {
            if (index > 0)
                builder.Append(',');

            builder.Append(
                Escape(values[index]));
        }

        builder.Append("\r\n");
    }

    static string Escape(
        string value) =>
        "\"" + value.Replace(
            "\"",
            "\"\"",
            StringComparison.Ordinal)
        + "\"";
}
