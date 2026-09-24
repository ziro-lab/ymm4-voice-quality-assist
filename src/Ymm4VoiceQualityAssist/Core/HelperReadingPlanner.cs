using System.Text;
using YukkuriMovieMaker.Plugin.Voice;

namespace Ymm4VoiceQualityAssist.Core;

public enum HelperReadingPlanStatus
{
    Success,
    AnchorResolutionFailed,
    EmptyFullReading,
    CurrentReadingMismatch,
    PrefixConversionFailed,
    NoUniqueReadingBoundary,
    DuplicateReadingBoundary,
    EmptyHelper,
}

public sealed record ResolvedHelperInsertion(
    int RuleIndex,
    HelperMoraRule Rule,
    int SourcePosition,
    int HatsuonBoundary,
    string NormalizedPrefixBefore,
    string NormalizedPrefixThroughHelper);

public sealed record HelperReadingPlan(
    string CleanSerif,
    string OriginalHatsuon,
    string AugmentedReading,
    IReadOnlyList<ResolvedHelperInsertion> Insertions);

public sealed record HelperReadingPlanResult(
    HelperReadingPlanStatus Status,
    HelperReadingPlan? Plan,
    string? Message)
{
    public bool IsSuccess =>
        Status == HelperReadingPlanStatus.Success
        && Plan is not null;

    public static HelperReadingPlanResult Success(
        HelperReadingPlan plan) =>
        new(HelperReadingPlanStatus.Success, plan, null);

    public static HelperReadingPlanResult Failure(
        HelperReadingPlanStatus status,
        string message) =>
        new(status, null, message);
}

public static class SameSpeakerHelperReadingPlanner
{
    public static async Task<HelperReadingPlanResult> ResolveAsync(
        IVoiceSpeaker speaker,
        IVoiceParameter parameter,
        string cleanSerif,
        string currentHatsuon,
        IReadOnlyList<HelperMoraRule> rules)
    {
        ArgumentNullException.ThrowIfNull(speaker);
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(cleanSerif);
        ArgumentNullException.ThrowIfNull(currentHatsuon);
        ArgumentNullException.ThrowIfNull(rules);

        var anchors = HelperAnchorResolver.Resolve(
            cleanSerif,
            rules);

        if (!anchors.IsSuccess)
        {
            return HelperReadingPlanResult.Failure(
                HelperReadingPlanStatus.AnchorResolutionFailed,
                anchors.Message ?? "Helper anchor resolution failed.");
        }

        string fullReading;
        try
        {
            fullReading = await speaker.ConvertKanjiToYomiAsync(
                cleanSerif,
                parameter);
        }
        catch (Exception ex)
        {
            return HelperReadingPlanResult.Failure(
                HelperReadingPlanStatus.EmptyFullReading,
                $"Full reading conversion failed: {ex.GetBaseException().Message}");
        }

        var normalizedFull = ReadingNormalizer.Normalize(fullReading);
        if (normalizedFull.Length == 0)
        {
            return HelperReadingPlanResult.Failure(
                HelperReadingPlanStatus.EmptyFullReading,
                "The active speaker returned an empty full reading.");
        }

        if (!string.Equals(
            normalizedFull,
            ReadingNormalizer.Normalize(currentHatsuon),
            StringComparison.Ordinal))
        {
            return HelperReadingPlanResult.Failure(
                HelperReadingPlanStatus.CurrentReadingMismatch,
                "Current Hatsuon does not exactly match the active speaker full reading.");
        }

        var provisional = new List<(
            int RuleIndex,
            ResolvedHelperAnchor Anchor,
            int HatsuonBoundary)>();

        for (var index = 0; index < anchors.Anchors.Count; index++)
        {
            var anchor = anchors.Anchors[index];
            var helper = ReadingNormalizer.Normalize(
                anchor.Rule.Helper);

            if (helper.Length == 0)
            {
                return HelperReadingPlanResult.Failure(
                    HelperReadingPlanStatus.EmptyHelper,
                    $"Helper rule {index} normalizes to an empty reading.");
            }

            string prefixReading;
            if (anchor.Position == 0)
            {
                prefixReading = string.Empty;
            }
            else
            {
                try
                {
                    prefixReading = await speaker.ConvertKanjiToYomiAsync(
                        cleanSerif[..anchor.Position],
                        parameter);
                }
                catch (Exception ex)
                {
                    return HelperReadingPlanResult.Failure(
                        HelperReadingPlanStatus.PrefixConversionFailed,
                        $"Helper rule {index} prefix conversion failed: {ex.GetBaseException().Message}");
                }
            }

            var candidates = ReadingBoundaryLocator.FindCandidates(
                currentHatsuon,
                prefixReading);

            if (candidates.Count != 1)
            {
                return HelperReadingPlanResult.Failure(
                    HelperReadingPlanStatus.NoUniqueReadingBoundary,
                    $"Helper rule {index} resolved to {candidates.Count} Hatsuon boundaries.");
            }

            provisional.Add((
                index,
                anchor,
                candidates[0]));
        }

        var duplicate = provisional
            .GroupBy(x => x.HatsuonBoundary)
            .FirstOrDefault(x => x.Count() > 1);

        if (duplicate is not null)
        {
            return HelperReadingPlanResult.Failure(
                HelperReadingPlanStatus.DuplicateReadingBoundary,
                $"More than one helper rule resolved to Hatsuon boundary {duplicate.Key}.");
        }

        var ordered = provisional
            .OrderBy(x => x.HatsuonBoundary)
            .ThenBy(x => x.RuleIndex)
            .ToArray();

        var builder = new StringBuilder(
            currentHatsuon.Length
            + ordered.Sum(x => x.Anchor.Rule.Helper.Length));

        var insertions = new List<ResolvedHelperInsertion>();
        var cursor = 0;

        foreach (var entry in ordered)
        {
            builder.Append(
                currentHatsuon,
                cursor,
                entry.HatsuonBoundary - cursor);

            var normalizedBefore = ReadingNormalizer.Normalize(
                builder.ToString());

            builder.Append(entry.Anchor.Rule.Helper);

            var normalizedThrough = ReadingNormalizer.Normalize(
                builder.ToString());

            insertions.Add(new ResolvedHelperInsertion(
                entry.RuleIndex,
                entry.Anchor.Rule,
                entry.Anchor.Position,
                entry.HatsuonBoundary,
                normalizedBefore,
                normalizedThrough));

            cursor = entry.HatsuonBoundary;
        }

        builder.Append(
            currentHatsuon,
            cursor,
            currentHatsuon.Length - cursor);

        return HelperReadingPlanResult.Success(
            new HelperReadingPlan(
                cleanSerif,
                currentHatsuon,
                builder.ToString(),
                insertions));
    }
}

public static class ReadingBoundaryLocator
{
    public static IReadOnlyList<int> FindCandidates(
        string reading,
        string prefixReading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(prefixReading);

        var target = ReadingNormalizer.Normalize(prefixReading);
        var candidates = new List<int>();

        foreach (var boundary in EnumerateUtf16Boundaries(reading))
        {
            if (string.Equals(
                ReadingNormalizer.Normalize(
                    reading[..boundary]),
                target,
                StringComparison.Ordinal))
            {
                candidates.Add(boundary);
            }
        }

        return candidates;
    }

    static IEnumerable<int> EnumerateUtf16Boundaries(
        string value)
    {
        yield return 0;

        for (var index = 1; index < value.Length; index++)
        {
            if (char.IsLowSurrogate(value[index])
                && char.IsHighSurrogate(value[index - 1]))
            {
                continue;
            }

            yield return index;
        }

        if (value.Length > 0)
            yield return value.Length;
    }
}
