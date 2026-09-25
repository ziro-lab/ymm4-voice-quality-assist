using System.ComponentModel.DataAnnotations;

namespace Ymm4VoiceQualityAssist.Core;

public enum ProsodyGesture
{
    [Display(
        Name = "なし",
        Description = "VOICEVOXの抑揚をそのまま使います。")]
    None,

    [Display(
        Name = "軽く上げる",
        Description = "語尾へ向かって軽く上げます。")]
    LightRise,

    [Display(
        Name = "軽く下げる",
        Description = "語尾へ向かって軽く下げます。")]
    LightFall,

    [Display(
        Name = "平らに寄せる",
        Description = "元の抑揚を残しながら高低差を軽く抑えます。")]
    Hold,
}

public enum ProsodyPitchPlanStatus
{
    Success,
    InsufficientVoicedMoras,
    InvalidPitch,
}

public sealed record ProsodyPitchPlanResult(
    ProsodyPitchPlanStatus Status,
    IReadOnlyList<double> AdjustedPitches,
    string? Message)
{
    public bool IsSuccess =>
        Status == ProsodyPitchPlanStatus.Success;

    public static ProsodyPitchPlanResult Success(
        IReadOnlyList<double> adjusted) =>
        new(ProsodyPitchPlanStatus.Success, adjusted, null);

    public static ProsodyPitchPlanResult Failure(
        ProsodyPitchPlanStatus status,
        string message) =>
        new(status, [], message);
}

public static class ProsodyPitchPlanner
{
    public const double LightAmplitude = 0.08;
    public const double HoldRetention = 0.50;

    public static ProsodyPitchPlanResult Plan(
        ProsodyGesture gesture,
        IReadOnlyList<double> baselinePitches)
    {
        ArgumentNullException.ThrowIfNull(baselinePitches);

        if (baselinePitches.Any(
            x => !double.IsFinite(x) || x <= 0.0))
        {
            return ProsodyPitchPlanResult.Failure(
                ProsodyPitchPlanStatus.InvalidPitch,
                "Prosody baseline contains a non-positive or non-finite voiced pitch.");
        }

        if (gesture == ProsodyGesture.None)
        {
            return ProsodyPitchPlanResult.Success(
                baselinePitches.ToArray());
        }

        if (baselinePitches.Count < 2)
        {
            return ProsodyPitchPlanResult.Failure(
                ProsodyPitchPlanStatus.InsufficientVoicedMoras,
                "Prosody gesture requires at least two voiced moras.");
        }

        var adjusted = gesture switch
        {
            ProsodyGesture.LightRise =>
                ApplyLinearOffset(
                    baselinePitches,
                    -LightAmplitude,
                    LightAmplitude),

            ProsodyGesture.LightFall =>
                ApplyLinearOffset(
                    baselinePitches,
                    LightAmplitude,
                    -LightAmplitude),

            ProsodyGesture.Hold =>
                ApplyHold(baselinePitches),

            _ => throw new ArgumentOutOfRangeException(
                nameof(gesture),
                gesture,
                "Unsupported prosody gesture."),
        };

        if (adjusted.Any(
            x => !double.IsFinite(x) || x <= 0.0))
        {
            return ProsodyPitchPlanResult.Failure(
                ProsodyPitchPlanStatus.InvalidPitch,
                "Prosody gesture produced an invalid pitch.");
        }

        return ProsodyPitchPlanResult.Success(adjusted);
    }

    static double[] ApplyLinearOffset(
        IReadOnlyList<double> baseline,
        double startOffset,
        double endOffset)
    {
        var result = new double[baseline.Count];
        var denominator = baseline.Count - 1.0;

        for (var i = 0; i < baseline.Count; i++)
        {
            var t = i / denominator;
            var offset =
                startOffset
                + ((endOffset - startOffset) * t);

            result[i] = baseline[i] + offset;
        }

        return result;
    }

    static double[] ApplyHold(
        IReadOnlyList<double> baseline)
    {
        var mean = baseline.Average();
        return baseline
            .Select(x =>
                mean
                + ((x - mean) * HoldRetention))
            .ToArray();
    }
}

public enum ProsodyMutationStatus
{
    Success,
    InvalidMoraData,
    InsufficientVoicedMoras,
}

public sealed record ProsodyMutationResult(
    ProsodyMutationStatus Status,
    int MutatedMoraCount,
    string? Message)
{
    public bool IsSuccess =>
        Status == ProsodyMutationStatus.Success;

    public static ProsodyMutationResult Success(
        int count) =>
        new(ProsodyMutationStatus.Success, count, null);

    public static ProsodyMutationResult Failure(
        ProsodyMutationStatus status,
        string message) =>
        new(status, 0, message);
}

public static class ProsodyMoraMutator
{
    public static ProsodyMutationResult Apply(
        VoiceVoxPronounceProjection projection,
        ProsodyGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(projection);

        if (gesture == ProsodyGesture.None)
            return ProsodyMutationResult.Success(0);

        var all = projection.AccentPhrases
            .SelectMany(x => x.Moras)
            .ToArray();

        foreach (var mora in all)
        {
            if (!double.IsFinite(mora.Pitch)
                || mora.Pitch < 0.0
                || !double.IsFinite(mora.VowelLength)
                || mora.VowelLength < 0.0)
            {
                return ProsodyMutationResult.Failure(
                    ProsodyMutationStatus.InvalidMoraData,
                    "VOICEVOX mora contains invalid pitch or vowel duration.");
            }
        }

        // pitch == 0 is treated as unvoiced.
        // vowel_length == 0 is an A2 zero-vowel helper and should not affect
        // the pitch-gesture curve.
        var targets = all
            .Where(x =>
                x.Pitch > 0.0
                && x.VowelLength > 0.0)
            .ToArray();

        var plan = ProsodyPitchPlanner.Plan(
            gesture,
            targets.Select(x => x.Pitch).ToArray());

        if (!plan.IsSuccess)
        {
            return ProsodyMutationResult.Failure(
                plan.Status
                    == ProsodyPitchPlanStatus.InsufficientVoicedMoras
                        ? ProsodyMutationStatus.InsufficientVoicedMoras
                        : ProsodyMutationStatus.InvalidMoraData,
                plan.Message
                    ?? "Prosody pitch plan failed.");
        }

        // Atomic: the complete plan is validated before any VOICEVOX object
        // is mutated.
        for (var i = 0; i < targets.Length; i++)
            targets[i].Pitch = plan.AdjustedPitches[i];

        return ProsodyMutationResult.Success(
            targets.Length);
    }
}
