using YukkuriMovieMaker.Plugin.Voice;

namespace Ymm4VoiceQualityAssist.Core;

public static class AugmentedZeroPauseBoundaryResolver
{
    public static async Task<BoundaryResolutionResult> ResolveAsync(
        IVoiceSpeaker speaker,
        IVoiceParameter parameter,
        BoundaryMarkerParseResult markerSource,
        HelperReadingPlan helperPlan,
        IVoicePronounce pronounce)
    {
        ArgumentNullException.ThrowIfNull(speaker);
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(markerSource);
        ArgumentNullException.ThrowIfNull(helperPlan);
        ArgumentNullException.ThrowIfNull(pronounce);

        if (!VoiceVoxPronounceAdapter.TryProject(
            pronounce,
            out var projection,
            out var adapterError)
            || projection is null)
        {
            return BoundaryResolutionResult.Failure(
                BoundaryResolutionStatus.MoraStreamMismatch,
                adapterError ?? "VOICEVOX Pronounce could not be projected.");
        }

        var markerReadings = new List<MarkerReading>();

        foreach (var position in markerSource.ZeroWaitPositions)
        {
            if (position <= 0
                || position >= markerSource.CleanText.Length)
            {
                return BoundaryResolutionResult.Failure(
                    BoundaryResolutionStatus.InvalidMarkerPosition,
                    $"Marker position {position} is outside the supported interior range.");
            }

            string prefixReading;
            try
            {
                prefixReading = await speaker.ConvertKanjiToYomiAsync(
                    markerSource.CleanText[..position],
                    parameter);
            }
            catch (Exception ex)
            {
                return BoundaryResolutionResult.Failure(
                    BoundaryResolutionStatus.EmptyPrefixReading,
                    $"Prefix reading conversion failed at {position}: {ex.GetBaseException().Message}");
            }

            var candidates = ReadingBoundaryLocator.FindCandidates(
                helperPlan.OriginalHatsuon,
                prefixReading);

            if (candidates.Count != 1)
            {
                return BoundaryResolutionResult.Failure(
                    BoundaryResolutionStatus.NoUniquePhraseBoundary,
                    $"Marker at {position} resolved to {candidates.Count} Hatsuon boundaries before helper augmentation.");
            }

            var hatsuonBoundary = candidates[0];

            if (helperPlan.Insertions.Any(
                x => x.HatsuonBoundary == hatsuonBoundary))
            {
                return BoundaryResolutionResult.Failure(
                    BoundaryResolutionStatus.HelperBoundaryConflict,
                    $"Marker at {position} shares a reading boundary with a helper insertion.");
            }

            markerReadings.Add(new MarkerReading(
                position,
                BuildAugmentedPrefix(
                    helperPlan,
                    hatsuonBoundary)));
        }

        return ReadingBoundaryResolver.Resolve(
            helperPlan.AugmentedReading,
            helperPlan.AugmentedReading,
            markerReadings,
            projection.Readings);
    }

    static string BuildAugmentedPrefix(
        HelperReadingPlan plan,
        int hatsuonBoundary)
    {
        var builder = new System.Text.StringBuilder();
        var cursor = 0;

        foreach (var insertion in plan.Insertions
            .Where(x => x.HatsuonBoundary < hatsuonBoundary)
            .OrderBy(x => x.HatsuonBoundary))
        {
            builder.Append(
                plan.OriginalHatsuon,
                cursor,
                insertion.HatsuonBoundary - cursor);

            builder.Append(insertion.Rule.Helper);
            cursor = insertion.HatsuonBoundary;
        }

        builder.Append(
            plan.OriginalHatsuon,
            cursor,
            hatsuonBoundary - cursor);

        return builder.ToString();
    }
}
