namespace Ymm4VoiceQualityAssist.Core;

public enum BoundaryResolutionStatus
{
    Success,
    EmptyFullReading,
    CurrentReadingMismatch,
    MoraStreamMismatch,
    InvalidMarkerPosition,
    EmptyPrefixReading,
    PrefixNotInFullReading,
    NoUniquePhraseBoundary,
    MissingPauseMora,
    DuplicateTarget,
}

public sealed record PhraseReadingProjection(
    int PhraseIndex,
    IReadOnlyList<string> MoraTexts,
    bool HasPauseMora);

public sealed record MarkerReading(
    int MarkerPosition,
    string PrefixReading);

public sealed record ResolvedBoundary(
    int MarkerPosition,
    int PhraseIndex,
    string NormalizedPrefixReading);

public sealed record BoundaryResolutionResult(
    BoundaryResolutionStatus Status,
    IReadOnlyList<ResolvedBoundary> Boundaries,
    string? Message)
{
    public bool IsSuccess => Status == BoundaryResolutionStatus.Success;

    public static BoundaryResolutionResult Success(
        IReadOnlyList<ResolvedBoundary> boundaries) =>
        new(BoundaryResolutionStatus.Success, boundaries, null);

    public static BoundaryResolutionResult Failure(
        BoundaryResolutionStatus status,
        string message) =>
        new(status, [], message);
}

public static class ReadingBoundaryResolver
{
    public static BoundaryResolutionResult Resolve(
        string fullSpeakerReading,
        string currentHatsuon,
        IReadOnlyList<MarkerReading> markerReadings,
        IReadOnlyList<PhraseReadingProjection> phrases)
    {
        var normalizedFull = ReadingNormalizer.Normalize(fullSpeakerReading);
        if (normalizedFull.Length == 0)
        {
            return BoundaryResolutionResult.Failure(
                BoundaryResolutionStatus.EmptyFullReading,
                "The voice provider returned an empty full reading.");
        }

        var normalizedCurrent = ReadingNormalizer.Normalize(currentHatsuon);
        if (!string.Equals(
            normalizedCurrent,
            normalizedFull,
            StringComparison.Ordinal))
        {
            return BoundaryResolutionResult.Failure(
                BoundaryResolutionStatus.CurrentReadingMismatch,
                "Current Hatsuon does not exactly match the same-speaker full reading.");
        }

        var normalizedMoraStream = ReadingNormalizer.Normalize(
            string.Concat(
                phrases.SelectMany(x => x.MoraTexts)));

        if (!string.Equals(
            normalizedMoraStream,
            normalizedFull,
            StringComparison.Ordinal))
        {
            return BoundaryResolutionResult.Failure(
                BoundaryResolutionStatus.MoraStreamMismatch,
                "VOICEVOX mora stream does not exactly match the full reading.");
        }

        var resolved = new List<ResolvedBoundary>();
        var usedPhraseIndexes = new HashSet<int>();

        foreach (var marker in markerReadings.OrderBy(x => x.MarkerPosition))
        {
            if (marker.MarkerPosition <= 0)
            {
                return BoundaryResolutionResult.Failure(
                    BoundaryResolutionStatus.InvalidMarkerPosition,
                    $"Marker position {marker.MarkerPosition} is not an interior boundary.");
            }

            var normalizedPrefix = ReadingNormalizer.Normalize(
                marker.PrefixReading);

            if (normalizedPrefix.Length == 0)
            {
                return BoundaryResolutionResult.Failure(
                    BoundaryResolutionStatus.EmptyPrefixReading,
                    $"Marker at {marker.MarkerPosition} resolved to an empty reading.");
            }

            if (normalizedPrefix.Length >= normalizedFull.Length
                || !normalizedFull.StartsWith(
                    normalizedPrefix,
                    StringComparison.Ordinal))
            {
                return BoundaryResolutionResult.Failure(
                    BoundaryResolutionStatus.PrefixNotInFullReading,
                    $"Marker at {marker.MarkerPosition} does not resolve to a strict prefix of the full reading.");
            }

            var candidates = FindPhraseEndCandidates(
                normalizedPrefix,
                phrases);

            if (candidates.Count != 1)
            {
                return BoundaryResolutionResult.Failure(
                    BoundaryResolutionStatus.NoUniquePhraseBoundary,
                    $"Marker at {marker.MarkerPosition} resolved to {candidates.Count} phrase-end candidates.");
            }

            var target = candidates[0];
            if (!target.HasPauseMora)
            {
                return BoundaryResolutionResult.Failure(
                    BoundaryResolutionStatus.MissingPauseMora,
                    $"Phrase {target.PhraseIndex} has no PauseMora.");
            }

            if (!usedPhraseIndexes.Add(target.PhraseIndex))
            {
                return BoundaryResolutionResult.Failure(
                    BoundaryResolutionStatus.DuplicateTarget,
                    $"More than one marker resolved to phrase {target.PhraseIndex}.");
            }

            resolved.Add(new ResolvedBoundary(
                marker.MarkerPosition,
                target.PhraseIndex,
                normalizedPrefix));
        }

        return BoundaryResolutionResult.Success(resolved);
    }

    static List<PhraseReadingProjection> FindPhraseEndCandidates(
        string normalizedPrefix,
        IReadOnlyList<PhraseReadingProjection> phrases)
    {
        var candidates = new List<PhraseReadingProjection>();
        var cumulative = string.Empty;

        foreach (var phrase in phrases)
        {
            cumulative += string.Concat(phrase.MoraTexts);
            var normalizedCumulative = ReadingNormalizer.Normalize(cumulative);

            if (string.Equals(
                normalizedCumulative,
                normalizedPrefix,
                StringComparison.Ordinal))
            {
                candidates.Add(phrase);
            }
        }

        return candidates;
    }
}
