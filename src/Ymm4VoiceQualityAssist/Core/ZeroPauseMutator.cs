namespace Ymm4VoiceQualityAssist.Core;

public sealed record ZeroPauseMutationResult(
    bool Applied,
    int MutatedPhraseCount,
    string? Error);

public static class ZeroPauseMutator
{
    public static ZeroPauseMutationResult Apply(
        VoiceVoxPronounceProjection projection,
        BoundaryResolutionResult resolution)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(resolution);

        if (!resolution.IsSuccess)
        {
            return new ZeroPauseMutationResult(
                false,
                0,
                resolution.Message ?? "Boundary resolution did not succeed.");
        }

        // Fail closed atomically: validate the complete target set before
        // mutating any VOICEVOX object. A stale/replaced Pronounce must never
        // leave only the first half of a multi-marker correction applied.
        var targets = new List<YukkuriMovieMaker.Voice.VOICEVOXAccentPhrase>();

        foreach (var boundary in resolution.Boundaries)
        {
            if (boundary.PhraseIndex < 0
                || boundary.PhraseIndex >= projection.AccentPhrases.Count)
            {
                return new ZeroPauseMutationResult(
                    false,
                    0,
                    $"Phrase index {boundary.PhraseIndex} is outside the AudioQuery.");
            }

            var phrase = projection.AccentPhrases[boundary.PhraseIndex];
            if (phrase.PauseMora is null)
            {
                return new ZeroPauseMutationResult(
                    false,
                    0,
                    $"Phrase {boundary.PhraseIndex} no longer has a PauseMora.");
            }

            targets.Add(phrase);
        }

        foreach (var phrase in targets)
            phrase.PauseMora!.VowelLength = 0.0;

        return new ZeroPauseMutationResult(
            true,
            targets.Count,
            null);
    }
}
