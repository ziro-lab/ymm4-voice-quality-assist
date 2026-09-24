using System.Collections;
using System.Reflection;
using YukkuriMovieMaker.Plugin.Voice;
using YukkuriMovieMaker.Voice;

namespace Ymm4VoiceQualityAssist.Core;

/// <summary>
/// Bounded VOICEVOX-specific adapter.
///
/// YMM4 4.56.1.0 exposes VoiceItem.Pronounce as IVoicePronounce while the concrete
/// VOICEVOXVoicePronounce type itself is internal. Lab PR #116 proves that the
/// concrete type exposes a public AudioQuery property and public AccentPhrase/Mora
/// members. Reflection is intentionally isolated here and all mismatches fail closed.
/// </summary>
public static class VoiceVoxPronounceAdapter
{
    const string VoiceVoxPronounceTypeName =
        "YukkuriMovieMaker.Voice.VOICEVOXVoicePronounce";

    public static bool TryProject(
        IVoicePronounce pronounce,
        out VoiceVoxPronounceProjection? projection,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(pronounce);

        projection = null;
        error = null;

        var pronounceType = pronounce.GetType();
        if (!string.Equals(
            pronounceType.FullName,
            VoiceVoxPronounceTypeName,
            StringComparison.Ordinal))
        {
            error = $"Unsupported pronounce type: {pronounceType.FullName}.";
            return false;
        }

        var audioQueryProperty = pronounceType.GetProperty(
            "AudioQuery",
            BindingFlags.Instance | BindingFlags.Public);

        if (audioQueryProperty?.GetMethod?.IsPublic != true)
        {
            error = "VOICEVOX Pronounce public AudioQuery getter was not found.";
            return false;
        }

        var query = audioQueryProperty.GetValue(pronounce);
        if (query is null)
        {
            error = "VOICEVOX AudioQuery is null.";
            return false;
        }

        var phrasesProperty = query.GetType().GetProperty(
            "AccentPhrases",
            BindingFlags.Instance | BindingFlags.Public);

        if (phrasesProperty?.GetMethod?.IsPublic != true
            || phrasesProperty.GetValue(query) is not IEnumerable phrases)
        {
            error = "VOICEVOX AudioQuery public AccentPhrases surface was not found.";
            return false;
        }

        var concrete = new List<VOICEVOXAccentPhrase>();
        foreach (var item in phrases.Cast<object>())
        {
            if (item is not VOICEVOXAccentPhrase phrase)
            {
                error = $"Unexpected AccentPhrase type: {item.GetType().FullName}.";
                return false;
            }
            concrete.Add(phrase);
        }

        var readings = concrete
            .Select((phrase, index) =>
                new PhraseReadingProjection(
                    index,
                    phrase.Moras.Select(mora => mora.Text ?? string.Empty).ToArray(),
                    phrase.PauseMora is not null))
            .ToArray();

        projection = new VoiceVoxPronounceProjection(
            readings,
            concrete);

        return true;
    }
}

public sealed record VoiceVoxPronounceProjection(
    IReadOnlyList<PhraseReadingProjection> Readings,
    IReadOnlyList<VOICEVOXAccentPhrase> AccentPhrases);
