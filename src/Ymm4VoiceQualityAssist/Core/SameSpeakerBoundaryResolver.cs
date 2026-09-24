using YukkuriMovieMaker.Plugin.Voice;

namespace Ymm4VoiceQualityAssist.Core;

public static class SameSpeakerBoundaryResolver
{
    public static async Task<BoundaryResolutionResult> ResolveAsync(
        IVoiceSpeaker speaker,
        IVoiceParameter voiceParameter,
        BoundaryMarkerParseResult markerSource,
        string currentHatsuon,
        IVoicePronounce pronounce)
    {
        ArgumentNullException.ThrowIfNull(speaker);
        ArgumentNullException.ThrowIfNull(voiceParameter);
        ArgumentNullException.ThrowIfNull(markerSource);
        ArgumentNullException.ThrowIfNull(currentHatsuon);
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

        string fullReading;
        try
        {
            fullReading = await speaker.ConvertKanjiToYomiAsync(
                markerSource.CleanText,
                voiceParameter);
        }
        catch (Exception ex)
        {
            return BoundaryResolutionResult.Failure(
                BoundaryResolutionStatus.EmptyFullReading,
                $"Full reading conversion failed: {ex.GetBaseException().Message}");
        }

        var markerReadings = new List<MarkerReading>();

        foreach (var position in markerSource.ZeroWaitPositions)
        {
            if (position <= 0 || position >= markerSource.CleanText.Length)
            {
                return BoundaryResolutionResult.Failure(
                    BoundaryResolutionStatus.InvalidMarkerPosition,
                    $"Marker position {position} is outside the supported interior range.");
            }

            var prefix = markerSource.CleanText[..position];

            string prefixReading;
            try
            {
                prefixReading = await speaker.ConvertKanjiToYomiAsync(
                    prefix,
                    voiceParameter);
            }
            catch (Exception ex)
            {
                return BoundaryResolutionResult.Failure(
                    BoundaryResolutionStatus.EmptyPrefixReading,
                    $"Prefix reading conversion failed at {position}: {ex.GetBaseException().Message}");
            }

            markerReadings.Add(new MarkerReading(
                position,
                prefixReading));
        }

        return ReadingBoundaryResolver.Resolve(
            fullReading,
            currentHatsuon,
            markerReadings,
            projection.Readings);
    }
}
