using System.Reflection;

namespace Ymm4VoiceQualityAssist.Core;

public enum HelperMoraMutationStatus
{
    Success,
    MoraStreamMismatch,
    BoundaryNotUnique,
    HelperNotSingleMora,
    MissingConsonant,
    PropertyUnavailable,
    DuplicateTarget,
}

public sealed record HelperMoraMutationResult(
    HelperMoraMutationStatus Status,
    int MutatedMoraCount,
    string? Message)
{
    public bool IsSuccess =>
        Status == HelperMoraMutationStatus.Success;

    public static HelperMoraMutationResult Success(
        int count) =>
        new(HelperMoraMutationStatus.Success, count, null);

    public static HelperMoraMutationResult Failure(
        HelperMoraMutationStatus status,
        string message) =>
        new(status, 0, message);
}

public static class HelperMoraMutator
{
    sealed record Target(
        object Mora,
        HelperMoraKind Kind,
        int RuleIndex);

    public static HelperMoraMutationResult Apply(
        VoiceVoxPronounceProjection projection,
        HelperReadingPlan plan)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(plan);

        var flat = projection.AccentPhrases
            .SelectMany(x => x.Moras)
            .Cast<object>()
            .ToArray();

        var moraTexts = flat
            .Select(GetMoraText)
            .ToArray();

        var normalizedStream = ReadingNormalizer.Normalize(
            string.Concat(moraTexts));

        if (!string.Equals(
            normalizedStream,
            ReadingNormalizer.Normalize(plan.AugmentedReading),
            StringComparison.Ordinal))
        {
            return HelperMoraMutationResult.Failure(
                HelperMoraMutationStatus.MoraStreamMismatch,
                "Augmented VOICEVOX mora stream does not exactly match the transient reading.");
        }

        var targets = new List<Target>();
        var usedMoras = new HashSet<object>(
            ReferenceEqualityComparer.Instance);

        foreach (var insertion in plan.Insertions)
        {
            var starts = FindMoraBoundaries(
                moraTexts,
                insertion.NormalizedPrefixBefore);
            var ends = FindMoraBoundaries(
                moraTexts,
                insertion.NormalizedPrefixThroughHelper);

            if (starts.Count != 1 || ends.Count != 1)
            {
                return HelperMoraMutationResult.Failure(
                    HelperMoraMutationStatus.BoundaryNotUnique,
                    $"Helper rule {insertion.RuleIndex} resolved to {starts.Count} start and {ends.Count} end mora boundaries.");
            }

            var start = starts[0];
            var end = ends[0];

            if (end - start != 1)
            {
                return HelperMoraMutationResult.Failure(
                    HelperMoraMutationStatus.HelperNotSingleMora,
                    $"Helper rule {insertion.RuleIndex} spans {end - start} moras; A2 MVP requires exactly one helper mora.");
            }

            var mora = flat[start];

            if (!usedMoras.Add(mora))
            {
                return HelperMoraMutationResult.Failure(
                    HelperMoraMutationStatus.DuplicateTarget,
                    $"More than one helper rule resolved to mora index {start}.");
            }

            var validation = ValidateTarget(
                mora,
                insertion.Rule.Kind,
                insertion.RuleIndex);

            if (validation is not null)
                return validation;

            targets.Add(new Target(
                mora,
                insertion.Rule.Kind,
                insertion.RuleIndex));
        }

        foreach (var target in targets)
        {
            switch (target.Kind)
            {
                case HelperMoraKind.ZeroVowel:
                    SetDouble(
                        target.Mora,
                        "VowelLength",
                        0.0);
                    break;

                case HelperMoraKind.ZeroConsonant:
                    SetDouble(
                        target.Mora,
                        "ConsonantLength",
                        0.0);
                    break;

                default:
                    return HelperMoraMutationResult.Failure(
                        HelperMoraMutationStatus.PropertyUnavailable,
                        $"Unsupported helper kind: {target.Kind}.");
            }
        }

        return HelperMoraMutationResult.Success(
            targets.Count);
    }

    static HelperMoraMutationResult? ValidateTarget(
        object mora,
        HelperMoraKind kind,
        int ruleIndex)
    {
        var propertyName = kind switch
        {
            HelperMoraKind.ZeroVowel => "VowelLength",
            HelperMoraKind.ZeroConsonant => "ConsonantLength",
            _ => string.Empty,
        };

        var property = mora.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public);

        if (property?.SetMethod?.IsPublic != true)
        {
            return HelperMoraMutationResult.Failure(
                HelperMoraMutationStatus.PropertyUnavailable,
                $"Helper rule {ruleIndex}: public writable {propertyName} was not found.");
        }

        if (kind == HelperMoraKind.ZeroConsonant)
        {
            var consonant = mora.GetType().GetProperty(
                "Consonant",
                BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(mora)?.ToString();

            var length = property.GetValue(mora);

            if (string.IsNullOrWhiteSpace(consonant)
                || length is null
                || Convert.ToDouble(
                    length,
                    System.Globalization.CultureInfo.InvariantCulture) <= 0.0)
            {
                return HelperMoraMutationResult.Failure(
                    HelperMoraMutationStatus.MissingConsonant,
                    $"Helper rule {ruleIndex}: target mora has no positive consonant duration.");
            }
        }

        return null;
    }

    static IReadOnlyList<int> FindMoraBoundaries(
        IReadOnlyList<string> moraTexts,
        string normalizedPrefix)
    {
        var result = new List<int>();

        for (var count = 0; count <= moraTexts.Count; count++)
        {
            var prefix = string.Concat(
                moraTexts.Take(count));

            if (string.Equals(
                ReadingNormalizer.Normalize(prefix),
                normalizedPrefix,
                StringComparison.Ordinal))
            {
                result.Add(count);
            }
        }

        return result;
    }

    static string GetMoraText(object mora) =>
        mora.GetType().GetProperty(
            "Text",
            BindingFlags.Instance | BindingFlags.Public)
        ?.GetValue(mora)?.ToString()
        ?? string.Empty;

    static void SetDouble(
        object target,
        string propertyName,
        double value)
    {
        var property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(
                target.GetType().FullName,
                propertyName);

        property.SetValue(target, value);
    }
}
