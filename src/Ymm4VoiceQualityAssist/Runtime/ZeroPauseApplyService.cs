using System.IO;
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
    InvalidHelperRules,
    InvalidProsodyConfiguration,
    HelperResolutionFailed,
    HelperMutationFailed,
    ProsodyMutationFailed,
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

    public bool HasHelperConfiguration(VoiceItem voice) =>
        EnumerateAssistEffects(voice)
            .Any(effect =>
                !string.IsNullOrWhiteSpace(effect.HelperRulesJson));

    public bool HasProsodyConfiguration(VoiceItem voice) =>
        EnumerateAssistEffects(voice)
            .Any(effect =>
                effect.Prosody != ProsodyGesture.None);

    public bool TryGetHelperRuleSet(
        VoiceItem voice,
        out HelperRuleSet ruleSet,
        out string? error)
    {
        var rules = new List<HelperMoraRule>();

        foreach (var effect in EnumerateAssistEffects(voice)
            .Where(x => x.IsEnabled))
        {
            if (!HelperRuleCodec.TryDecode(
                effect.HelperRulesJson,
                out var decoded,
                out error))
            {
                ruleSet = HelperRuleSet.Empty;
                return false;
            }

            rules.AddRange(decoded.Rules);
        }

        ruleSet = new HelperRuleSet(
            HelperRuleSet.CurrentVersion,
            rules);
        error = null;
        return true;
    }

    public bool TryGetProsodyGesture(
        VoiceItem voice,
        out ProsodyGesture gesture,
        out string? error)
    {
        var configured = EnumerateAssistEffects(voice)
            .Where(x => x.IsEnabled)
            .Select(x => x.Prosody)
            .Where(x => x != ProsodyGesture.None)
            .Distinct()
            .ToArray();

        if (configured.Length > 1)
        {
            gesture = ProsodyGesture.None;
            error =
                "More than one enabled Assist Effect specifies a different prosody gesture.";
            return false;
        }

        gesture = configured.Length == 1
            ? configured[0]
            : ProsodyGesture.None;
        error = null;
        return true;
    }

    public async Task<AssistApplyResult> ApplyAsync(VoiceItem voice)
    {
        ArgumentNullException.ThrowIfNull(voice);

        var markerSource = ParseMarkers(voice);

        if (!TryGetHelperRuleSet(
            voice,
            out var helperRules,
            out var helperRuleError))
        {
            return new AssistApplyResult(
                AssistApplyStatus.InvalidHelperRules,
                0,
                null,
                helperRuleError ?? "Helper rule JSON is invalid.");
        }

        if (!TryGetProsodyGesture(
            voice,
            out var prosody,
            out var prosodyError))
        {
            return new AssistApplyResult(
                AssistApplyStatus.InvalidProsodyConfiguration,
                0,
                null,
                prosodyError
                    ?? "Prosody configuration is invalid.");
        }

        var hasMarkers =
            markerSource.ZeroWaitPositions.Count > 0;
        var hasHelpers =
            helperRules.Rules.Count > 0;
        var hasProsody =
            prosody != ProsodyGesture.None;

        if (!hasMarkers && !hasHelpers && !hasProsody)
        {
            return new AssistApplyResult(
                AssistApplyStatus.NoMarkers,
                0,
                null,
                "No zero-pause markers, helper-mora rules, or prosody gesture were found.");
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

        HelperReadingPlan? helperPlan = null;
        var synthesisText = hatsuon;

        if (hasHelpers)
        {
            var helperPlanResult =
                await SameSpeakerHelperReadingPlanner.ResolveAsync(
                    speaker!,
                    parameter!,
                    markerSource.CleanText,
                    hatsuon,
                    helperRules.Rules);

            if (!helperPlanResult.IsSuccess
                || helperPlanResult.Plan is null)
            {
                return new AssistApplyResult(
                    AssistApplyStatus.HelperResolutionFailed,
                    0,
                    null,
                    helperPlanResult.Message
                        ?? "Helper reading plan could not be resolved.");
            }

            helperPlan = helperPlanResult.Plan;
            synthesisText = helperPlan.AugmentedReading;
        }

        var analysisPath = CreateTempWavePath();
        var correctedPath = CreateTempWavePath();

        try
        {
            IVoicePronounce? freshPronounce;
            try
            {
                freshPronounce = await speaker!.CreateVoiceAsync(
                    synthesisText,
                    pronounce: null,
                    parameter,
                    analysisPath);
            }
            catch (Exception ex)
            {
                return SynthesisFailure(
                    "Fresh analysis/synthesis failed.",
                    ex);
            }

            if (freshPronounce is null)
            {
                return new AssistApplyResult(
                    AssistApplyStatus.SynthesisFailed,
                    0,
                    null,
                    "The active voice provider returned no Pronounce.");
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
                    projectionError
                        ?? "VOICEVOX Pronounce projection failed.");
            }

            var mutatedCount = 0;
            BoundaryResolutionStatus? boundaryStatus = null;

            // Validate and mutate helper moras only on the detached fresh
            // Pronounce. Nothing reaches VoiceItem/WAV until every correction
            // step and the final synthesis have succeeded.
            if (helperPlan is not null)
            {
                var helperMutation = HelperMoraMutator.Apply(
                    projection,
                    helperPlan);

                if (!helperMutation.IsSuccess)
                {
                    return new AssistApplyResult(
                        AssistApplyStatus.HelperMutationFailed,
                        0,
                        null,
                        helperMutation.Message);
                }

                mutatedCount += helperMutation.MutatedMoraCount;
            }

            if (hasMarkers)
            {
                var resolution = helperPlan is null
                    ? await SameSpeakerBoundaryResolver.ResolveAsync(
                        speaker!,
                        parameter!,
                        markerSource,
                        hatsuon,
                        freshPronounce)
                    : await AugmentedZeroPauseBoundaryResolver.ResolveAsync(
                        speaker!,
                        parameter!,
                        markerSource,
                        helperPlan,
                        freshPronounce);

                if (!resolution.IsSuccess)
                {
                    return new AssistApplyResult(
                        AssistApplyStatus.ResolutionFailed,
                        0,
                        resolution.Status,
                        resolution.Message);
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

                mutatedCount += mutation.MutatedPhraseCount;
                boundaryStatus = resolution.Status;
            }

            if (hasProsody)
            {
                var prosodyMutation = ProsodyMoraMutator.Apply(
                    projection,
                    prosody);

                if (!prosodyMutation.IsSuccess)
                {
                    return new AssistApplyResult(
                        AssistApplyStatus.ProsodyMutationFailed,
                        0,
                        boundaryStatus,
                        prosodyMutation.Message);
                }

                mutatedCount += prosodyMutation.MutatedMoraCount;
            }

            IVoicePronounce? regenerated;
            try
            {
                regenerated = await speaker!.CreateVoiceAsync(
                    synthesisText,
                    freshPronounce,
                    parameter,
                    correctedPath);
            }
            catch (Exception ex)
            {
                return SynthesisFailure(
                    "Corrected synthesis failed.",
                    ex);
            }

            if (regenerated is null
                || !File.Exists(correctedPath)
                || new FileInfo(correctedPath).Length <= 44)
            {
                return new AssistApplyResult(
                    AssistApplyStatus.SynthesisFailed,
                    0,
                    boundaryStatus,
                    "Corrected synthesis did not produce a usable WAV.");
            }

            File.Copy(
                correctedPath,
                targetPath,
                overwrite: true);
            voice.ClearVoiceCache();
            voice.Pronounce = regenerated!;

            return new AssistApplyResult(
                AssistApplyStatus.Applied,
                mutatedCount,
                boundaryStatus,
                null);
        }
        finally
        {
            TryDelete(analysisPath);
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
            voice.Pronounce = baseline!;

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
