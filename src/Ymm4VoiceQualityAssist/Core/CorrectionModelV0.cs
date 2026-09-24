using YukkuriMovieMaker.Project.Items;

namespace Ymm4VoiceQualityAssist.Core;

public sealed record CorrectionHelperV0(
    HelperMoraKind Kind,
    string Helper,
    int CleanTextPosition);

public sealed record CorrectionStateV0(
    string CleanText,
    string Hatsuon,
    IReadOnlyList<int> ZeroWaitBoundaries,
    IReadOnlyList<CorrectionHelperV0> Helpers,
    ProsodyGesture Prosody);

public enum CorrectionModelBuildStatus
{
    Success,
    InvalidControls,
    InvalidHelperRules,
    HelperResolutionFailed,
    DuplicateHelperBoundary,
    HelperBoundaryCollision,
    ConflictingProsody,
}

public sealed record CorrectionModelBuildResult(
    CorrectionModelBuildStatus Status,
    CorrectionStateV0? State,
    string? Message)
{
    public bool IsSuccess =>
        Status == CorrectionModelBuildStatus.Success
        && State is not null;

    public static CorrectionModelBuildResult Success(
        CorrectionStateV0 state) =>
        new(
            CorrectionModelBuildStatus.Success,
            state,
            null);

    public static CorrectionModelBuildResult Failure(
        CorrectionModelBuildStatus status,
        string message) =>
        new(
            status,
            null,
            message);
}

public enum CorrectionPatchStatus
{
    Success,
    MissingOperations,
    NoChangeMixedWithOperations,
    MultipleReadingOverrides,
    EmptyReading,
    MultipleProsodyGestures,
    UnsupportedProsodyGesture,
    BoundaryPositionOutOfRange,
    BoundarySplitsSurrogatePair,
    AddBoundaryAlreadyExists,
    RemoveBoundaryMissing,
    HelperPositionOutOfRange,
    HelperSplitsSurrogatePair,
    EmptyHelper,
    DuplicateHelperBoundary,
    HelperBoundaryCollision,
    UnknownOperation,
}

public sealed record CorrectionPatchResult(
    CorrectionPatchStatus Status,
    CorrectionStateV0? State,
    string? Message)
{
    public bool IsSuccess =>
        Status == CorrectionPatchStatus.Success
        && State is not null;

    public static CorrectionPatchResult Success(
        CorrectionStateV0 state) =>
        new(
            CorrectionPatchStatus.Success,
            state,
            null);

    public static CorrectionPatchResult Failure(
        CorrectionPatchStatus status,
        string message) =>
        new(
            status,
            null,
            message);
}

public static class CorrectionModelV0
{
    public static CorrectionModelBuildResult Build(
        VoiceItem voice)
    {
        ArgumentNullException.ThrowIfNull(voice);

        BoundaryMarkerParseResult controls;

        try
        {
            controls =
                BoundaryMarkerParser.Parse(
                    voice.Serif
                    ?? string.Empty);
        }
        catch (Exception ex)
        {
            return CorrectionModelBuildResult.Failure(
                CorrectionModelBuildStatus.InvalidControls,
                "Serif control tags could not be parsed: "
                + ex.GetBaseException().Message);
        }

        var enabledEffects =
            ReviewAssistEffectCollection
                .Enumerate(voice)
                .Where(x => x.IsEnabled)
                .ToArray();

        var rules =
            new List<HelperMoraRule>();

        foreach (var effect
            in enabledEffects)
        {
            if (!HelperRuleCodec.TryDecode(
                effect.HelperRulesJson,
                out var decoded,
                out var error))
            {
                return CorrectionModelBuildResult.Failure(
                    CorrectionModelBuildStatus.InvalidHelperRules,
                    error
                        ?? "Enabled Assist Effect contains invalid helper rules.");
            }

            rules.AddRange(
                decoded.Rules);
        }

        var helpers =
            Array.Empty<CorrectionHelperV0>();

        if (rules.Count > 0)
        {
            var resolved =
                HelperAnchorResolver.Resolve(
                    controls.CleanText,
                    rules);

            if (!resolved.IsSuccess)
            {
                return CorrectionModelBuildResult.Failure(
                    CorrectionModelBuildStatus.HelperResolutionFailed,
                    resolved.Message
                        ?? "Helper anchors could not be resolved.");
            }

            var duplicate =
                resolved.Anchors
                    .GroupBy(x => x.Position)
                    .FirstOrDefault(x =>
                        x.Count() > 1);

            if (duplicate is not null)
            {
                return CorrectionModelBuildResult.Failure(
                    CorrectionModelBuildStatus.DuplicateHelperBoundary,
                    $"More than one helper resolves to clean-text boundary {duplicate.Key}.");
            }

            helpers =
                resolved.Anchors
                    .Select(x =>
                        new CorrectionHelperV0(
                            x.Rule.Kind,
                            x.Rule.Helper,
                            x.Position))
                    .OrderBy(x =>
                        x.CleanTextPosition)
                    .ThenBy(x =>
                        x.Kind)
                    .ThenBy(x =>
                        x.Helper,
                        StringComparer.Ordinal)
                    .ToArray();
        }

        var boundaries =
            controls.ZeroWaitPositions
                .Distinct()
                .OrderBy(x => x)
                .ToArray();

        var boundarySet =
            boundaries.ToHashSet();

        var collision =
            helpers.FirstOrDefault(x =>
                boundarySet.Contains(
                    x.CleanTextPosition));

        if (collision is not null)
        {
            return CorrectionModelBuildResult.Failure(
                CorrectionModelBuildStatus.HelperBoundaryCollision,
                $"Helper and <w0> boundary collide at clean-text position {collision.CleanTextPosition}.");
        }

        var prosodies =
            enabledEffects
                .Select(x => x.Prosody)
                .Where(x =>
                    x != ProsodyGesture.None)
                .Distinct()
                .ToArray();

        if (prosodies.Length > 1)
        {
            return CorrectionModelBuildResult.Failure(
                CorrectionModelBuildStatus.ConflictingProsody,
                "Enabled Assist Effects contain conflicting non-None prosody gestures.");
        }

        return CorrectionModelBuildResult.Success(
            Normalize(
                new CorrectionStateV0(
                    controls.CleanText,
                    voice.Hatsuon
                        ?? string.Empty,
                    boundaries,
                    helpers,
                    prosodies.Length == 1
                        ? prosodies[0]
                        : ProsodyGesture.None)));
    }

    public static CorrectionPatchResult Apply(
        CorrectionStateV0 current,
        IReadOnlyList<CorrectionOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(operations);

        var state =
            Normalize(current);

        if (operations.Count == 0)
        {
            return CorrectionPatchResult.Failure(
                CorrectionPatchStatus.MissingOperations,
                "At least one correction operation is required.");
        }

        var noChangeCount =
            operations
                .OfType<NoChangeCorrection>()
                .Count();

        if (noChangeCount > 0)
        {
            if (operations.Count != 1)
            {
                return CorrectionPatchResult.Failure(
                    CorrectionPatchStatus.NoChangeMixedWithOperations,
                    "noChange must be the only operation.");
            }

            return CorrectionPatchResult.Success(
                state);
        }

        var readings =
            operations
                .OfType<SetReadingCorrection>()
                .ToArray();

        if (readings.Length > 1)
        {
            return CorrectionPatchResult.Failure(
                CorrectionPatchStatus.MultipleReadingOverrides,
                "Only one reading override is allowed.");
        }

        var hatsuon =
            state.Hatsuon;

        if (readings.Length == 1)
        {
            if (string.IsNullOrWhiteSpace(
                readings[0].Reading))
            {
                return CorrectionPatchResult.Failure(
                    CorrectionPatchStatus.EmptyReading,
                    "Reading override must not be empty.");
            }

            hatsuon =
                readings[0].Reading;
        }

        var prosodies =
            operations
                .OfType<SetProsodyGestureCorrection>()
                .ToArray();

        if (prosodies.Length > 1)
        {
            return CorrectionPatchResult.Failure(
                CorrectionPatchStatus.MultipleProsodyGestures,
                "Only one prosody operation is allowed.");
        }

        var prosody =
            state.Prosody;

        if (prosodies.Length == 1)
        {
            if (!Enum.IsDefined(
                prosodies[0].Gesture))
            {
                return CorrectionPatchResult.Failure(
                    CorrectionPatchStatus.UnsupportedProsodyGesture,
                    "Prosody gesture is not supported.");
            }

            prosody =
                prosodies[0].Gesture;
        }

        var boundaries =
            state.ZeroWaitBoundaries
                .ToHashSet();

        foreach (var remove
            in operations
                .OfType<RemoveBoundaryCorrection>())
        {
            var position =
                remove.CleanTextPosition;

            var boundaryError =
                ValidateInteriorBoundary(
                    state.CleanText,
                    position);

            if (boundaryError is not null)
                return boundaryError;

            if (!boundaries.Remove(
                position))
            {
                return CorrectionPatchResult.Failure(
                    CorrectionPatchStatus.RemoveBoundaryMissing,
                    $"No boundary exists at clean-text position {position}.");
            }
        }

        foreach (var add
            in operations
                .OfType<AddBoundaryCorrection>())
        {
            var position =
                add.CleanTextPosition;

            var boundaryError =
                ValidateInteriorBoundary(
                    state.CleanText,
                    position);

            if (boundaryError is not null)
                return boundaryError;

            if (!boundaries.Add(
                position))
            {
                return CorrectionPatchResult.Failure(
                    CorrectionPatchStatus.AddBoundaryAlreadyExists,
                    $"A boundary already exists at clean-text position {position}.");
            }
        }

        var helpers =
            state.Helpers.ToDictionary(
                x => x.CleanTextPosition);

        foreach (var operation
            in operations)
        {
            CorrectionHelperV0? helper =
                operation switch
                {
                    HelperVowelZeroCorrection x =>
                        new CorrectionHelperV0(
                            HelperMoraKind.ZeroVowel,
                            x.Helper,
                            x.CleanTextPosition),

                    HelperConsonantZeroCorrection x =>
                        new CorrectionHelperV0(
                            HelperMoraKind.ZeroConsonant,
                            x.Helper,
                            x.CleanTextPosition),

                    _ =>
                        null,
                };

            if (helper is null)
                continue;

            if (helper.CleanTextPosition < 0
                || helper.CleanTextPosition
                    > state.CleanText.Length)
            {
                return CorrectionPatchResult.Failure(
                    CorrectionPatchStatus.HelperPositionOutOfRange,
                    $"Helper position {helper.CleanTextPosition} is outside the clean text.");
            }

            if (SplitsSurrogatePair(
                state.CleanText,
                helper.CleanTextPosition))
            {
                return CorrectionPatchResult.Failure(
                    CorrectionPatchStatus.HelperSplitsSurrogatePair,
                    $"Helper position {helper.CleanTextPosition} splits a UTF-16 surrogate pair.");
            }

            if (string.IsNullOrWhiteSpace(
                helper.Helper)
                || ReadingNormalizer.Normalize(
                    helper.Helper).Length == 0)
            {
                return CorrectionPatchResult.Failure(
                    CorrectionPatchStatus.EmptyHelper,
                    $"Helper at {helper.CleanTextPosition} is empty after normalization.");
            }

            if (helpers.ContainsKey(
                helper.CleanTextPosition))
            {
                return CorrectionPatchResult.Failure(
                    CorrectionPatchStatus.DuplicateHelperBoundary,
                    $"A helper already exists at clean-text boundary {helper.CleanTextPosition}.");
            }

            helpers.Add(
                helper.CleanTextPosition,
                helper);
        }

        foreach (var helper
            in helpers.Values)
        {
            if (boundaries.Contains(
                helper.CleanTextPosition))
            {
                return CorrectionPatchResult.Failure(
                    CorrectionPatchStatus.HelperBoundaryCollision,
                    $"Helper and boundary collide at clean-text position {helper.CleanTextPosition}.");
            }
        }

        foreach (var operation
            in operations)
        {
            if (operation
                is not SetReadingCorrection
                and not AddBoundaryCorrection
                and not RemoveBoundaryCorrection
                and not HelperVowelZeroCorrection
                and not HelperConsonantZeroCorrection
                and not SetProsodyGestureCorrection
                and not NoChangeCorrection)
            {
                return CorrectionPatchResult.Failure(
                    CorrectionPatchStatus.UnknownOperation,
                    "Unsupported correction operation: "
                    + operation.GetType().FullName);
            }
        }

        return CorrectionPatchResult.Success(
            Normalize(
                new CorrectionStateV0(
                    state.CleanText,
                    hatsuon,
                    boundaries
                        .OrderBy(x => x)
                        .ToArray(),
                    helpers.Values
                        .OrderBy(x =>
                            x.CleanTextPosition)
                        .ThenBy(x =>
                            x.Kind)
                        .ThenBy(x =>
                            x.Helper,
                            StringComparer.Ordinal)
                        .ToArray(),
                    prosody)));
    }

    public static HelperRuleSet CreateHelperRuleSet(
        CorrectionStateV0 state,
        int contextLength =
            HelperRuleFactory.DefaultContextLength)
    {
        ArgumentNullException.ThrowIfNull(state);

        var normalized =
            Normalize(state);

        return new HelperRuleSet(
            HelperRuleSet.CurrentVersion,
            normalized.Helpers
                .Select(x =>
                    HelperRuleFactory.Create(
                        normalized.CleanText,
                        x.CleanTextPosition,
                        x.Helper,
                        x.Kind,
                        contextLength))
                .ToArray());
    }

    static CorrectionPatchResult? ValidateInteriorBoundary(
        string cleanText,
        int position)
    {
        if (position <= 0
            || position >= cleanText.Length)
        {
            return CorrectionPatchResult.Failure(
                CorrectionPatchStatus.BoundaryPositionOutOfRange,
                $"Boundary {position} must be an interior clean-text boundary.");
        }

        if (SplitsSurrogatePair(
            cleanText,
            position))
        {
            return CorrectionPatchResult.Failure(
                CorrectionPatchStatus.BoundarySplitsSurrogatePair,
                $"Boundary {position} splits a UTF-16 surrogate pair.");
        }

        return null;
    }

    static bool SplitsSurrogatePair(
        string value,
        int boundary) =>
        boundary > 0
        && boundary < value.Length
        && char.IsHighSurrogate(
            value[boundary - 1])
        && char.IsLowSurrogate(
            value[boundary]);

    static CorrectionStateV0 Normalize(
        CorrectionStateV0 state)
    {
        ArgumentNullException.ThrowIfNull(
            state.CleanText);
        ArgumentNullException.ThrowIfNull(
            state.Hatsuon);
        ArgumentNullException.ThrowIfNull(
            state.ZeroWaitBoundaries);
        ArgumentNullException.ThrowIfNull(
            state.Helpers);

        return state with
        {
            ZeroWaitBoundaries =
                state.ZeroWaitBoundaries
                    .Distinct()
                    .OrderBy(x => x)
                    .ToArray(),

            Helpers =
                state.Helpers
                    .OrderBy(x =>
                        x.CleanTextPosition)
                    .ThenBy(x =>
                        x.Kind)
                    .ThenBy(x =>
                        x.Helper,
                        StringComparer.Ordinal)
                    .ToArray(),
        };
    }
}
