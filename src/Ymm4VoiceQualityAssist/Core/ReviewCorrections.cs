namespace Ymm4VoiceQualityAssist.Core;

public abstract record CorrectionOperation;

public abstract record ReviewCorrectionOperation
    : CorrectionOperation;

public sealed record SetReadingCorrection(
    string Reading)
    : ReviewCorrectionOperation;

public sealed record AddBoundaryCorrection(
    int CleanTextPosition)
    : ReviewCorrectionOperation;

public sealed record RemoveBoundaryCorrection(
    int CleanTextPosition)
    : ReviewCorrectionOperation;

public sealed record HelperVowelZeroCorrection(
    int CleanTextPosition,
    string Helper)
    : ReviewCorrectionOperation;

public sealed record HelperConsonantZeroCorrection(
    int CleanTextPosition,
    string Helper)
    : ReviewCorrectionOperation;

public sealed record SetProsodyGestureCorrection(
    ProsodyGesture Gesture)
    : ReviewCorrectionOperation;

public sealed record NoChangeCorrection
    : ReviewCorrectionOperation;

public sealed record ReviewCorrectionProposal(
    string Schema,
    string ExportSessionId,
    string ExportRef,
    string SourceFingerprint,
    IReadOnlyList<ReviewCorrectionOperation> Operations);

public sealed record ReviewCorrectionValidationContext(
    string ExpectedExportSessionId,
    string ExpectedSourceFingerprint,
    int CleanTextLength);

public enum ReviewCorrectionValidationCode
{
    InvalidSchema,
    MissingExportSessionId,
    SessionMismatch,
    MissingExportRef,
    InvalidFingerprintFormat,
    SourceFingerprintMismatch,
    MissingOperations,
    UnknownOperation,
    NoChangeMixedWithOperations,
    MultipleReadingOverrides,
    EmptyReading,
    MultipleProsodyGestures,
    UnsupportedProsodyGesture,
    BoundaryPositionOutOfRange,
    DuplicateBoundaryOperation,
    ConflictingBoundaryOperation,
    HelperPositionOutOfRange,
    EmptyHelper,
    DuplicateHelperPosition,
}

public sealed record ReviewCorrectionValidationError(
    ReviewCorrectionValidationCode Code,
    string Message);

public sealed record ReviewCorrectionValidationResult(
    IReadOnlyList<ReviewCorrectionValidationError> Errors)
{
    public bool IsValid =>
        Errors.Count == 0;
}

public static class ReviewCorrectionValidator
{
    public const string Schema =
        "ymm4.voice-corrections.v0";

    public static ReviewCorrectionValidationResult
        Validate(
            ReviewCorrectionProposal proposal,
            ReviewCorrectionValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(
            proposal);
        ArgumentNullException.ThrowIfNull(
            context);

        if (context.CleanTextLength < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(context),
                "Clean text length must not be negative.");
        }

        var errors =
            new List<
                ReviewCorrectionValidationError>();

        if (!string.Equals(
            proposal.Schema,
            Schema,
            StringComparison.Ordinal))
        {
            Add(
                errors,
                ReviewCorrectionValidationCode
                    .InvalidSchema,
                "Correction schema is not supported.");
        }

        if (string.IsNullOrWhiteSpace(
            proposal.ExportSessionId))
        {
            Add(
                errors,
                ReviewCorrectionValidationCode
                    .MissingExportSessionId,
                "exportSessionId is required.");
        }
        else if (!string.Equals(
            proposal.ExportSessionId,
            context.ExpectedExportSessionId,
            StringComparison.Ordinal))
        {
            Add(
                errors,
                ReviewCorrectionValidationCode
                    .SessionMismatch,
                "exportSessionId does not match the review package.");
        }

        if (string.IsNullOrWhiteSpace(
            proposal.ExportRef))
        {
            Add(
                errors,
                ReviewCorrectionValidationCode
                    .MissingExportRef,
                "exportRef is required.");
        }

        if (!IsFingerprintFormat(
            proposal.SourceFingerprint))
        {
            Add(
                errors,
                ReviewCorrectionValidationCode
                    .InvalidFingerprintFormat,
                "sourceFingerprint must be sha256:<64 lowercase hex>.");
        }
        else if (!string.Equals(
            proposal.SourceFingerprint,
            context.ExpectedSourceFingerprint,
            StringComparison.Ordinal))
        {
            Add(
                errors,
                ReviewCorrectionValidationCode
                    .SourceFingerprintMismatch,
                "sourceFingerprint does not match the resolved source.");
        }

        if (proposal.Operations is null
            || proposal.Operations.Count == 0)
        {
            Add(
                errors,
                ReviewCorrectionValidationCode
                    .MissingOperations,
                "At least one correction operation is required.");

            return new ReviewCorrectionValidationResult(
                errors);
        }

        var noChangeCount =
            proposal.Operations
                .OfType<NoChangeCorrection>()
                .Count();

        if (noChangeCount > 0
            && proposal.Operations.Count != 1)
        {
            Add(
                errors,
                ReviewCorrectionValidationCode
                    .NoChangeMixedWithOperations,
                "noChange must be the only operation.");
        }

        var readings =
            proposal.Operations
                .OfType<SetReadingCorrection>()
                .ToArray();

        if (readings.Length > 1)
        {
            Add(
                errors,
                ReviewCorrectionValidationCode
                    .MultipleReadingOverrides,
                "Only one setReading operation is allowed.");
        }

        foreach (var operation in readings)
        {
            if (string.IsNullOrWhiteSpace(
                operation.Reading))
            {
                Add(
                    errors,
                    ReviewCorrectionValidationCode
                        .EmptyReading,
                    "setReading requires a non-empty reading.");
            }
        }

        var prosody =
            proposal.Operations
                .OfType<
                    SetProsodyGestureCorrection>()
                .ToArray();

        if (prosody.Length > 1)
        {
            Add(
                errors,
                ReviewCorrectionValidationCode
                    .MultipleProsodyGestures,
                "Only one setProsodyGesture operation is allowed.");
        }

        foreach (var operation in prosody)
        {
            if (!Enum.IsDefined(
                operation.Gesture))
            {
                Add(
                    errors,
                    ReviewCorrectionValidationCode
                        .UnsupportedProsodyGesture,
                    "Prosody gesture is not supported by this build.");
            }
        }

        ValidateBoundaries(
            proposal.Operations,
            context.CleanTextLength,
            errors);

        ValidateHelpers(
            proposal.Operations,
            context.CleanTextLength,
            errors);

        foreach (var operation
            in proposal.Operations)
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
                Add(
                    errors,
                    ReviewCorrectionValidationCode
                        .UnknownOperation,
                    "Unknown correction operation: "
                    + operation.GetType().FullName);
            }
        }

        return new ReviewCorrectionValidationResult(
            errors);
    }

    static void ValidateBoundaries(
        IReadOnlyList<ReviewCorrectionOperation>
            operations,
        int cleanTextLength,
        List<ReviewCorrectionValidationError>
            errors)
    {
        var adds = operations
            .OfType<AddBoundaryCorrection>()
            .Select(x => x.CleanTextPosition)
            .ToArray();

        var removes = operations
            .OfType<RemoveBoundaryCorrection>()
            .Select(x => x.CleanTextPosition)
            .ToArray();

        foreach (var position
            in adds.Concat(removes))
        {
            if (position <= 0
                || position >= cleanTextLength)
            {
                Add(
                    errors,
                    ReviewCorrectionValidationCode
                        .BoundaryPositionOutOfRange,
                    $"Boundary position {position} must be an interior clean-text boundary.");
            }
        }

        foreach (var duplicate
            in adds.GroupBy(x => x)
                .Where(x => x.Count() > 1))
        {
            Add(
                errors,
                ReviewCorrectionValidationCode
                    .DuplicateBoundaryOperation,
                $"addBoundary is duplicated at {duplicate.Key}.");
        }

        foreach (var duplicate
            in removes.GroupBy(x => x)
                .Where(x => x.Count() > 1))
        {
            Add(
                errors,
                ReviewCorrectionValidationCode
                    .DuplicateBoundaryOperation,
                $"removeBoundary is duplicated at {duplicate.Key}.");
        }

        foreach (var conflict
            in adds.Intersect(removes))
        {
            Add(
                errors,
                ReviewCorrectionValidationCode
                    .ConflictingBoundaryOperation,
                $"Boundary {conflict} is both added and removed.");
        }
    }

    static void ValidateHelpers(
        IReadOnlyList<ReviewCorrectionOperation>
            operations,
        int cleanTextLength,
        List<ReviewCorrectionValidationError>
            errors)
    {
        var helpers = operations
            .Select(
                x => x switch
                {
                    HelperVowelZeroCorrection h =>
                        (
                            Position: h.CleanTextPosition,
                            Helper: h.Helper),

                    HelperConsonantZeroCorrection h =>
                        (
                            Position: h.CleanTextPosition,
                            Helper: h.Helper),

                    _ => ((int Position, string Helper)?)null,
                })
            .Where(x => x is not null)
            .Select(x => x!.Value)
            .ToArray();

        foreach (var helper
            in helpers)
        {
            if (helper.Position < 0
                || helper.Position
                    > cleanTextLength)
            {
                Add(
                    errors,
                    ReviewCorrectionValidationCode
                        .HelperPositionOutOfRange,
                    $"Helper position {helper.Position} is outside the clean text.");
            }

            if (string.IsNullOrWhiteSpace(
                helper.Helper)
                || ReadingNormalizer.Normalize(
                    helper.Helper).Length == 0)
            {
                Add(
                    errors,
                    ReviewCorrectionValidationCode
                        .EmptyHelper,
                    $"Helper at {helper.Position} is empty after normalization.");
            }
        }

        foreach (var duplicate
            in helpers.GroupBy(
                x => x.Position)
                .Where(x => x.Count() > 1))
        {
            Add(
                errors,
                ReviewCorrectionValidationCode
                    .DuplicateHelperPosition,
                $"More than one helper targets clean-text boundary {duplicate.Key}.");
        }
    }

    static bool IsFingerprintFormat(
        string? value)
    {
        const string prefix = "sha256:";

        if (value is null
            || !value.StartsWith(
                prefix,
                StringComparison.Ordinal)
            || value.Length
                != prefix.Length + 64)
        {
            return false;
        }

        foreach (var ch
            in value.AsSpan(
                prefix.Length))
        {
            if (ch is not (
                >= '0' and <= '9'
                or >= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }

    static void Add(
        List<ReviewCorrectionValidationError>
            errors,
        ReviewCorrectionValidationCode code,
        string message) =>
        errors.Add(
            new ReviewCorrectionValidationError(
                code,
                message));
}
