namespace Ymm4VoiceQualityAssist.Core;

/// <summary>
/// Resolves only phrase ends created for plugin-injected transient commas.
///
/// Source punctuation is never enumerated as a correction target. If an
/// injected prefix can map to zero or multiple phrase ends, or if the target
/// has no PauseMora, the existing atomic boundary resolver fails closed.
/// </summary>
public static class InjectedPauseResolver
{
    public static BoundaryResolutionResult Resolve(
        ForcedBoundaryAnalysisPlan plan,
        VoiceVoxPronounceProjection projection)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(projection);

        var markerReadings = plan.Boundaries
            .Select(x => new MarkerReading(
                x.MarkerPosition,
                x.PrefixReading))
            .ToArray();

        return ReadingBoundaryResolver.Resolve(
            plan.TransientReading,
            plan.TransientReading,
            markerReadings,
            projection.Readings);
    }
}
