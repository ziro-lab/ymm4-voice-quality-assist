using System.Text;
using YukkuriMovieMaker.Plugin.Voice;

namespace Ymm4VoiceQualityAssist.Core;

public sealed record ResolvedForcedBoundaryInsertion(
    int MarkerPosition,
    int HatsuonBoundary,
    int AnalysisBoundary,
    string PrefixReading);

public sealed record ForcedBoundaryAnalysisPlan(
    string CleanSerif,
    string OriginalHatsuon,
    string BaseReading,
    string TransientReading,
    IReadOnlyList<ResolvedForcedBoundaryInsertion> Boundaries);

public sealed record ForcedBoundaryPlanResult(
    BoundaryResolutionStatus Status,
    ForcedBoundaryAnalysisPlan? Plan,
    string? Message)
{
    public bool IsSuccess =>
        Status == BoundaryResolutionStatus.Success
        && Plan is not null;

    public static ForcedBoundaryPlanResult Success(
        ForcedBoundaryAnalysisPlan plan) =>
        new(BoundaryResolutionStatus.Success, plan, null);

    public static ForcedBoundaryPlanResult Failure(
        BoundaryResolutionStatus status,
        string message) =>
        new(status, null, message);
}

/// <summary>
/// Builds the analysis-only reading used by vNext forced boundaries.
///
/// Durable <w0> markers stay in Serif. The planner maps each clean-text
/// marker to a unique boundary in the current same-speaker reading, then
/// injects a Japanese comma into the detached analysis reading. When helper
/// moras are configured the comma is inserted into the already-augmented
/// helper reading; helper text and forced punctuation therefore share one
/// transient analysis input without changing durable Hatsuon.
/// </summary>
public static class ForcedBoundaryReadingPlanner
{
    public static async Task<ForcedBoundaryPlanResult> ResolveAsync(
        IVoiceSpeaker speaker,
        IVoiceParameter parameter,
        BoundaryMarkerParseResult markerSource,
        string currentHatsuon,
        HelperReadingPlan? helperPlan = null)
    {
        ArgumentNullException.ThrowIfNull(speaker);
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(markerSource);
        ArgumentNullException.ThrowIfNull(currentHatsuon);

        if (markerSource.ZeroWaitPositions.Count == 0)
        {
            return ForcedBoundaryPlanResult.Success(
                new ForcedBoundaryAnalysisPlan(
                    markerSource.CleanText,
                    currentHatsuon,
                    helperPlan?.AugmentedReading ?? currentHatsuon,
                    helperPlan?.AugmentedReading ?? currentHatsuon,
                    []));
        }

        if (helperPlan is null)
        {
            string fullReading;
            try
            {
                fullReading = await speaker.ConvertKanjiToYomiAsync(
                    markerSource.CleanText,
                    parameter);
            }
            catch (Exception ex)
            {
                return ForcedBoundaryPlanResult.Failure(
                    BoundaryResolutionStatus.EmptyFullReading,
                    $"Full reading conversion failed: {ex.GetBaseException().Message}");
            }

            var normalizedFull = ReadingNormalizer.Normalize(fullReading);
            if (normalizedFull.Length == 0)
            {
                return ForcedBoundaryPlanResult.Failure(
                    BoundaryResolutionStatus.EmptyFullReading,
                    "The active speaker returned an empty full reading.");
            }

            if (!string.Equals(
                normalizedFull,
                ReadingNormalizer.Normalize(currentHatsuon),
                StringComparison.Ordinal))
            {
                return ForcedBoundaryPlanResult.Failure(
                    BoundaryResolutionStatus.CurrentReadingMismatch,
                    "Current Hatsuon does not exactly match the active speaker full reading.");
            }
        }
        else
        {
            if (!string.Equals(
                    helperPlan.CleanSerif,
                    markerSource.CleanText,
                    StringComparison.Ordinal)
                || !string.Equals(
                    ReadingNormalizer.Normalize(helperPlan.OriginalHatsuon),
                    ReadingNormalizer.Normalize(currentHatsuon),
                    StringComparison.Ordinal))
            {
                return ForcedBoundaryPlanResult.Failure(
                    BoundaryResolutionStatus.CurrentReadingMismatch,
                    "Helper reading plan does not match the current forced-boundary source.");
            }
        }

        var baseReading = helperPlan?.AugmentedReading ?? currentHatsuon;
        var resolved = new List<ResolvedForcedBoundaryInsertion>();
        var usedAnalysisBoundaries = new HashSet<int>();

        foreach (var position in markerSource.ZeroWaitPositions.OrderBy(x => x))
        {
            if (position <= 0 || position >= markerSource.CleanText.Length)
            {
                return ForcedBoundaryPlanResult.Failure(
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
                return ForcedBoundaryPlanResult.Failure(
                    BoundaryResolutionStatus.EmptyPrefixReading,
                    $"Prefix reading conversion failed at {position}: {ex.GetBaseException().Message}");
            }

            var candidates = ReadingBoundaryLocator.FindCandidates(
                currentHatsuon,
                prefixReading);

            if (candidates.Count != 1)
            {
                return ForcedBoundaryPlanResult.Failure(
                    BoundaryResolutionStatus.NoUniquePhraseBoundary,
                    $"Marker at {position} resolved to {candidates.Count} reading boundaries.");
            }

            var hatsuonBoundary = candidates[0];

            if (helperPlan?.Insertions.Any(
                x => x.HatsuonBoundary == hatsuonBoundary) == true)
            {
                return ForcedBoundaryPlanResult.Failure(
                    BoundaryResolutionStatus.HelperBoundaryConflict,
                    $"Marker at {position} shares a reading boundary with a helper insertion.");
            }

            var helperUtf16Before = helperPlan?.Insertions
                .Where(x => x.HatsuonBoundary < hatsuonBoundary)
                .Sum(x => x.Rule.Helper.Length) ?? 0;

            var analysisBoundary = hatsuonBoundary + helperUtf16Before;

            if (analysisBoundary <= 0
                || analysisBoundary >= baseReading.Length)
            {
                return ForcedBoundaryPlanResult.Failure(
                    BoundaryResolutionStatus.PrefixNotInFullReading,
                    $"Marker at {position} does not resolve to an interior transient-reading boundary.");
            }

            if (!usedAnalysisBoundaries.Add(analysisBoundary))
            {
                return ForcedBoundaryPlanResult.Failure(
                    BoundaryResolutionStatus.DuplicateTarget,
                    $"More than one marker resolved to transient-reading boundary {analysisBoundary}.");
            }

            resolved.Add(new ResolvedForcedBoundaryInsertion(
                position,
                hatsuonBoundary,
                analysisBoundary,
                baseReading[..analysisBoundary]));
        }

        var transient = new StringBuilder(baseReading);
        foreach (var boundary in resolved
            .OrderByDescending(x => x.AnalysisBoundary))
        {
            transient.Insert(boundary.AnalysisBoundary, '、');
        }

        return ForcedBoundaryPlanResult.Success(
            new ForcedBoundaryAnalysisPlan(
                markerSource.CleanText,
                currentHatsuon,
                baseReading,
                transient.ToString(),
                resolved));
    }
}
