namespace Ymm4VoiceQualityAssist.Core;

/// <summary>
/// vNext semantic wrapper around the atomic PauseMora mutation.
///
/// The supplied resolution must originate from InjectedPauseResolver, so only
/// plugin-injected transient punctuation is eligible for mutation.
/// </summary>
public static class ForcedBoundaryMutator
{
    public static ZeroPauseMutationResult Apply(
        VoiceVoxPronounceProjection projection,
        BoundaryResolutionResult resolution) =>
        ZeroPauseMutator.Apply(projection, resolution);
}
