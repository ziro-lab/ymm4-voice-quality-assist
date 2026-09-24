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
    Superseded,
    UnsupportedProvider,
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
            .Any(effect => effect.IsEnabled
                && !string.IsNullOrWhiteSpace(effect.HelperRulesJson));

    public bool HasProsodyConfiguration(VoiceItem voice) =>
        EnumerateAssistEffects(voice)
            .Any(effect => effect.IsEnabled
                && effect.Prosody != ProsodyGesture.None);

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

        if (configured.Any(x => !Enum.IsDefined(x)))
        {
            gesture = ProsodyGesture.None;
            error = "この候補版では未対応の抑揚設定です。";
            return false;
        }

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

    public async Task<AssistApplyResult> ApplyAsync(VoiceItem voice, Func<bool>? isCurrentTarget = null)
    {
        ArgumentNullException.ThrowIfNull(voice);
        using var lease = new VoiceApplyLease(voice, isCurrentTarget);
        if (!lease.IsCurrent) return Superseded();

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
                "No forced-boundary markers, helper-mora rules, or prosody gesture were found.");
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
        if (!lease.IsCurrent) return Superseded();
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return new AssistApplyResult(
                AssistApplyStatus.SynthesisFailed,
                0,
                null,
                "VoiceItem.FilePath is unavailable.");
        }

        lease.PinOutput(targetPath);
        AssistRuntimeStatus.Current.NoteGeneration();

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

            if (!lease.IsCurrent) return Superseded();

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

        ForcedBoundaryAnalysisPlan? forcedBoundaryPlan = null;

        if (hasMarkers)
        {
            var forcedPlanResult =
                await ForcedBoundaryReadingPlanner.ResolveAsync(
                    speaker!,
                    parameter!,
                    markerSource,
                    hatsuon,
                    helperPlan);

            if (!lease.IsCurrent) return Superseded();

            if (!forcedPlanResult.IsSuccess
                || forcedPlanResult.Plan is null)
            {
                return new AssistApplyResult(
                    AssistApplyStatus.ResolutionFailed,
                    0,
                    forcedPlanResult.Status,
                    forcedPlanResult.Message
                        ?? "Forced-boundary analysis plan could not be resolved.");
            }

            forcedBoundaryPlan = forcedPlanResult.Plan;
            synthesisText = forcedBoundaryPlan.TransientReading;
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

            if (!lease.IsCurrent) return Superseded();

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

            if (forcedBoundaryPlan is not null)
            {
                var resolution = InjectedPauseResolver.Resolve(
                    forcedBoundaryPlan,
                    projection);

                if (!resolution.IsSuccess)
                {
                    return new AssistApplyResult(
                        AssistApplyStatus.ResolutionFailed,
                        0,
                        resolution.Status,
                        resolution.Message);
                }

                // Only boundaries planned as plugin-injected transient commas
                // are eligible for mutation. Source punctuation is never
                // enumerated as a correction target.
                var mutation = ForcedBoundaryMutator.Apply(
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

            if (!AtomicWaveFile.TryReplace(correctedPath, targetPath, () => lease.IsCurrent))
                return Superseded();
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

    public async Task<AssistApplyResult> RestoreBaselineAsync(VoiceItem voice, Func<bool>? isCurrentTarget = null)
    {
        ArgumentNullException.ThrowIfNull(voice);
        using var lease = new VoiceApplyLease(voice, isCurrentTarget);
        if (!lease.IsCurrent) return Superseded();

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
        if (!lease.IsCurrent) return Superseded();
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return new AssistApplyResult(
                AssistApplyStatus.SynthesisFailed,
                0,
                null,
                "VoiceItem.FilePath is unavailable.");
        }

        lease.PinOutput(targetPath);
        AssistRuntimeStatus.Current.NoteGeneration();
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

            if (!AtomicWaveFile.TryReplace(baselinePath, targetPath, () => lease.IsCurrent))
                return Superseded();
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

        if (!string.Equals(speaker.GetType().FullName,
            "YukkuriMovieMaker.Voice.VOICEVOXVoiceSpeaker", StringComparison.Ordinal))
        {
            error = new AssistApplyResult(AssistApplyStatus.UnsupportedProvider, 0, null,
                "この候補版の発音補助はYMM4標準のVOICEVOX話者が対象です。元の音声は変更しません。");
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

    static AssistApplyResult Superseded()
    {
        AssistRuntimeStatus.Current.NoteDiscarded();
        return new(AssistApplyStatus.Superseded, 0, null,
            "補正中に対象が変更されたため、古い生成結果を破棄しました。");
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
