using System.Collections;
using Ymm4VoiceQualityAssist.Core;
using Ymm4VoiceQualityAssist.Effects;
using YukkuriMovieMaker.Plugin.Voice;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4VoiceQualityAssist.Runtime;

public enum AssistApplyStatus
{
    Applied,
    RestoredBaseline,
    NoVoiceProvider,
    MissingVoiceParameter,
    EmptyHatsuon,
    NoMarkers,
    ResolutionFailed,
    MutationFailed,
    SynthesisFailed,
}

public sealed record AssistApplyResult(
    AssistApplyStatus Status,
    int MutatedPhraseCount,
    BoundaryResolutionStatus? ResolutionStatus,
    string? Message)
{
    public bool IsApplied => Status == AssistApplyStatus.Applied;
    public bool IsBaseline => Status == AssistApplyStatus.RestoredBaseline;
}

public sealed class ZeroPauseApplyService
{
    public bool HasAssistEffect(VoiceItem voice) =>
        EnumerateAssistEffects(voice).Any();

    public bool IsAssistEnabled(VoiceItem voice) =>
        EnumerateAssistEffects(voice).Any(effect => effect.IsEnabled);

    public BoundaryMarkerParseResult ParseMarkers(VoiceItem voice) =>
        BoundaryMarkerParser.Parse(voice.Serif ?? string.Empty);

    public async Task<AssistApplyResult> ApplyAsync(VoiceItem voice)
    {
        ArgumentNullException.ThrowIfNull(voice);

        var markerSource = ParseMarkers(voice);
        if (markerSource.ZeroWaitPositions.Count == 0)
        {
            return new AssistApplyResult(
                AssistApplyStatus.NoMarkers,
                0,
                null,
                "No official <w0> boundary markers were found.");
        }

        if (!TryResolveVoiceContext(
            voice,
            out var speaker,
            out var parameter,
            out var contextError))
        {
            return contextError!;
        }

        var hatsuon = voice.Hatsuon ?? string.Empty;
        if (string.IsNullOrWhiteSpace(hatsuon))
        {
            return new AssistApplyResult(
                AssistApplyStatus.EmptyHatsuon,
                0,
                null,
                "VoiceItem.Hatsuon is empty.");
        }

        var targetPath = await EnsureVoicePathAsync(voice);
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return new AssistApplyResult(
                AssistApplyStatus.SynthesisFailed,
                0,
                null,
                "VoiceItem.FilePath is unavailable.");
        }

        var baselinePath = CreateTempWavePath();
        var correctedPath = CreateTempWavePath();

        try
        {
            IVoicePronounce? freshPronounce;
            try
            {
                freshPronounce = await speaker!.CreateVoiceAsync(
                    hatsuon,
                    pronounce: null,
                    parameter,
                    baselinePath);
            }
            catch (Exception ex)
            {
                return SynthesisFailure("Baseline analysis/synthesis failed.", ex);
            }

            if (freshPronounce is null)
            {
                return new AssistApplyResult(
                    AssistApplyStatus.SynthesisFailed,
                    0,
                    null,
                    "The active voice provider returned no Pronounce.");
            }

            var resolution = await SameSpeakerBoundaryResolver.ResolveAsync(
                speaker!,
                parameter!,
                markerSource,
                hatsuon,
                freshPronounce);

            if (!resolution.IsSuccess)
            {
                return new AssistApplyResult(
                    AssistApplyStatus.ResolutionFailed,
                    0,
                    resolution.Status,
                    resolution.Message);
            }

            if (!VoiceVoxPronounceAdapter.TryProject(
                freshPronounce,
                out var projection,
                out var projectionError)
                || projection is null)
            {
                return new AssistApplyResult(
                    AssistApplyStatus.MutationFailed,
                    0,
                    BoundaryResolutionStatus.MoraStreamMismatch,
                    projectionError ?? "VOICEVOX Pronounce projection failed.");
            }

            var mutation = ZeroPauseMutator.Apply(
                projection,
                resolution);

            if (!mutation.Applied)
            {
                return new AssistApplyResult(
                    AssistApplyStatus.MutationFailed,
                    0,
                    resolution.Status,
                    mutation.Error);
            }

            IVoicePronounce? regenerated;
            try
            {
                regenerated = await speaker!.CreateVoiceAsync(
                    hatsuon,
                    freshPronounce,
                    parameter,
                    correctedPath);
            }
            catch (Exception ex)
            {
                return SynthesisFailure("Corrected synthesis failed.", ex);
            }

            if (regenerated is null
                || !File.Exists(correctedPath)
                || new FileInfo(correctedPath).Length <= 44)
            {
                return new AssistApplyResult(
                    AssistApplyStatus.SynthesisFailed,
                    0,
                    resolution.Status,
                    "Corrected synthesis did not produce a usable WAV.");
            }

            // Commit only after the complete correction path succeeded.
            // This keeps resolver/synthesis failure non-destructive.
            File.Copy(correctedPath, targetPath, overwrite: true);
            voice.ClearVoiceCache();
            voice.Pronounce = regenerated;

            return new AssistApplyResult(
                AssistApplyStatus.Applied,
                mutation.MutatedPhraseCount,
                resolution.Status,
                null);
        }
        finally
        {
            TryDelete(baselinePath);
            TryDelete(correctedPath);
        }
    }

    public async Task<AssistApplyResult> RestoreBaselineAsync(VoiceItem voice)
    {
        ArgumentNullException.ThrowIfNull(voice);

        if (!TryResolveVoiceContext(
            voice,
            out var speaker,
            out var parameter,
            out var contextError))
        {
            return contextError!;
        }

        var hatsuon = voice.Hatsuon ?? string.Empty;
        if (string.IsNullOrWhiteSpace(hatsuon))
        {
            return new AssistApplyResult(
                AssistApplyStatus.EmptyHatsuon,
                0,
                null,
                "VoiceItem.Hatsuon is empty.");
        }

        var targetPath = await EnsureVoicePathAsync(voice);
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return new AssistApplyResult(
                AssistApplyStatus.SynthesisFailed,
                0,
                null,
                "VoiceItem.FilePath is unavailable.");
        }

        var baselinePath = CreateTempWavePath();

        try
        {
            IVoicePronounce? baseline;
            try
            {
                baseline = await speaker!.CreateVoiceAsync(
                    hatsuon,
                    pronounce: null,
                    parameter,
                    baselinePath);
            }
            catch (Exception ex)
            {
                return SynthesisFailure("Baseline synthesis failed.", ex);
            }

            if (baseline is null
                || !File.Exists(baselinePath)
                || new FileInfo(baselinePath).Length <= 44)
            {
                return new AssistApplyResult(
                    AssistApplyStatus.SynthesisFailed,
                    0,
                    null,
                    "Baseline synthesis did not produce a usable WAV.");
            }

            File.Copy(baselinePath, targetPath, overwrite: true);
            voice.ClearVoiceCache();
            voice.Pronounce = baseline;

            return new AssistApplyResult(
                AssistApplyStatus.RestoredBaseline,
                0,
                null,
                null);
        }
        finally
        {
            TryDelete(baselinePath);
        }
    }

    static bool TryResolveVoiceContext(
        VoiceItem voice,
        out IVoiceSpeaker? speaker,
        out IVoiceParameter? parameter,
        out AssistApplyResult? error)
    {
        speaker = voice.Character?.Voice?.Speaker;
        parameter = voice.VoiceParameter;
        error = null;

        if (speaker is null)
        {
            error = new AssistApplyResult(
                AssistApplyStatus.NoVoiceProvider,
                0,
                null,
                "The VoiceItem has no active voice speaker.");
            return false;
        }

        if (parameter is null)
        {
            error = new AssistApplyResult(
                AssistApplyStatus.MissingVoiceParameter,
                0,
                null,
                "The VoiceItem has no voice parameter.");
            return false;
        }

        return true;
    }

    static async Task<string?> EnsureVoicePathAsync(VoiceItem voice)
    {
        if (!string.IsNullOrWhiteSpace(voice.FilePath))
            return voice.FilePath;

        try
        {
            await voice.CreateVoiceFileAsync();
        }
        catch
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(voice.FilePath)
            ? null
            : voice.FilePath;
    }

    static IEnumerable<PronunciationAssistEffect> EnumerateAssistEffects(
        VoiceItem voice)
    {
        if (voice.JimakuVideoEffects is not IEnumerable effects)
            yield break;

        foreach (var value in effects)
        {
            if (value is PronunciationAssistEffect effect)
                yield return effect;
        }
    }

    static string CreateTempWavePath() =>
        Path.Combine(
            Path.GetTempPath(),
            $"ymm4-vqa-{Guid.NewGuid():N}.wav");

    static AssistApplyResult SynthesisFailure(
        string message,
        Exception ex) =>
        new(
            AssistApplyStatus.SynthesisFailed,
            0,
            null,
            $"{message} {ex.GetBaseException().Message}");

    static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Temp cleanup must never turn a successful correction into failure.
        }
    }
}
