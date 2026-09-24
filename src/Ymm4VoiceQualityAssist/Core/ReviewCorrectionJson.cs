using System.Text.Encodings.Web;
using System.Text.Json;

namespace Ymm4VoiceQualityAssist.Core;

public sealed record ReviewCorrectionWirePackage(
    string Schema,
    string ExportSessionId,
    IReadOnlyList<ReviewCorrectionWireRecord> Corrections);

public sealed record ReviewCorrectionWireRecord(
    string ExportRef,
    string SourceFingerprint,
    IReadOnlyList<ReviewCorrectionWireOperation> Operations);

public sealed record ReviewCorrectionWireOperation(
    string Type,
    string? Reading = null,
    int? Position = null,
    string? Helper = null,
    string? Gesture = null);

public enum ReviewCorrectionWireErrorCode
{
    InvalidJson,
    NullPackage,
    InvalidSchema,
    MissingSessionId,
    MissingCorrections,
    MissingExportRef,
    MissingFingerprint,
    MissingOperations,
    UnknownOperation,
    InvalidPayload,
    SessionMismatch,
    DuplicateExportRef,
    UnknownExportRef,
    FingerprintMismatch,
    IncompleteCoverage,
    DomainValidationFailed,
}

public sealed record ReviewCorrectionWireError(
    ReviewCorrectionWireErrorCode Code,
    string Message,
    string? ExportRef = null);

public sealed record ReviewCorrectionDecodeResult(
    ReviewCorrectionWirePackage? WirePackage,
    IReadOnlyList<ReviewCorrectionProposal> Proposals,
    IReadOnlyList<ReviewCorrectionWireError> Errors)
{
    public bool IsSuccess =>
        WirePackage is not null
        && Errors.Count == 0;
}

public static class ReviewCorrectionJson
{
    static readonly JsonSerializerOptions Options =
        new()
        {
            PropertyNamingPolicy =
                JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Encoder =
                JavaScriptEncoder
                    .UnsafeRelaxedJsonEscaping,
        };

    public static string Serialize(
        ReviewCorrectionWirePackage package)
    {
        ArgumentNullException.ThrowIfNull(
            package);

        return JsonSerializer.Serialize(
            package,
            Options);
    }

    public static ReviewCorrectionDecodeResult Decode(
        string json)
    {
        ArgumentNullException.ThrowIfNull(
            json);

        ReviewCorrectionWirePackage? package;

        try
        {
            package =
                JsonSerializer.Deserialize<
                    ReviewCorrectionWirePackage>(
                        json,
                        Options);
        }
        catch (Exception ex)
        {
            return Failure(
                ReviewCorrectionWireErrorCode.InvalidJson,
                ex.GetBaseException().Message);
        }

        if (package is null)
        {
            return Failure(
                ReviewCorrectionWireErrorCode.NullPackage,
                "Correction JSON resolved to null.");
        }

        var errors =
            new List<ReviewCorrectionWireError>();

        if (!string.Equals(
            package.Schema,
            ReviewCorrectionValidator.Schema,
            StringComparison.Ordinal))
        {
            errors.Add(
                new ReviewCorrectionWireError(
                    ReviewCorrectionWireErrorCode.InvalidSchema,
                    "Correction schema is not supported."));
        }

        if (string.IsNullOrWhiteSpace(
            package.ExportSessionId))
        {
            errors.Add(
                new ReviewCorrectionWireError(
                    ReviewCorrectionWireErrorCode.MissingSessionId,
                    "exportSessionId is required."));
        }

        if (package.Corrections is null)
        {
            errors.Add(
                new ReviewCorrectionWireError(
                    ReviewCorrectionWireErrorCode.MissingCorrections,
                    "corrections is required."));

            return new ReviewCorrectionDecodeResult(
                package,
                [],
                errors);
        }

        var proposals =
            new List<ReviewCorrectionProposal>(
                package.Corrections.Count);

        foreach (var correction
            in package.Corrections)
        {
            if (correction is null)
            {
                errors.Add(
                    new ReviewCorrectionWireError(
                        ReviewCorrectionWireErrorCode.InvalidPayload,
                        "Correction record is null."));
                continue;
            }

            var exportRef =
                correction.ExportRef;

            if (string.IsNullOrWhiteSpace(
                exportRef))
            {
                errors.Add(
                    new ReviewCorrectionWireError(
                        ReviewCorrectionWireErrorCode.MissingExportRef,
                        "exportRef is required."));
            }

            if (string.IsNullOrWhiteSpace(
                correction.SourceFingerprint))
            {
                errors.Add(
                    new ReviewCorrectionWireError(
                        ReviewCorrectionWireErrorCode.MissingFingerprint,
                        "sourceFingerprint is required.",
                        exportRef));
            }

            if (correction.Operations is null
                || correction.Operations.Count == 0)
            {
                errors.Add(
                    new ReviewCorrectionWireError(
                        ReviewCorrectionWireErrorCode.MissingOperations,
                        "At least one operation is required.",
                        exportRef));
                continue;
            }

            var operations =
                new List<ReviewCorrectionOperation>(
                    correction.Operations.Count);

            foreach (var operation
                in correction.Operations)
            {
                if (!TryConvertOperation(
                    operation,
                    out var converted,
                    out var operationError)
                    || converted is null)
                {
                    errors.Add(
                        new ReviewCorrectionWireError(
                            operationError?.Code
                                ?? ReviewCorrectionWireErrorCode.InvalidPayload,
                            operationError?.Message
                                ?? "Operation payload is invalid.",
                            exportRef));

                    continue;
                }

                operations.Add(converted);
            }

            proposals.Add(
                new ReviewCorrectionProposal(
                    package.Schema,
                    package.ExportSessionId,
                    exportRef ?? string.Empty,
                    correction.SourceFingerprint
                        ?? string.Empty,
                    operations));
        }

        return new ReviewCorrectionDecodeResult(
            package,
            proposals,
            errors);
    }

    public static ReviewCorrectionDecodeResult
        DecodeAndValidateAgainstExport(
            string json,
            ReviewExportPackage exportPackage)
    {
        ArgumentNullException.ThrowIfNull(
            exportPackage);

        var decoded =
            Decode(json);

        if (decoded.WirePackage is null)
            return decoded;

        var errors =
            decoded.Errors.ToList();

        var wire =
            decoded.WirePackage;

        if (!string.Equals(
            wire.ExportSessionId,
            exportPackage.ExportSessionId,
            StringComparison.Ordinal))
        {
            errors.Add(
                new ReviewCorrectionWireError(
                    ReviewCorrectionWireErrorCode.SessionMismatch,
                    "Correction exportSessionId does not match the review export."));
        }

        var byRef =
            exportPackage.Voices.ToDictionary(
                x => x.Target.ExportRef,
                StringComparer.Ordinal);

        var grouped =
            decoded.Proposals
                .GroupBy(
                    x => x.ExportRef,
                    StringComparer.Ordinal)
                .ToArray();

        foreach (var group
            in grouped.Where(x => x.Count() > 1))
        {
            errors.Add(
                new ReviewCorrectionWireError(
                    ReviewCorrectionWireErrorCode.DuplicateExportRef,
                    "More than one correction record targets the same exportRef.",
                    group.Key));
        }

        foreach (var proposal
            in decoded.Proposals)
        {
            if (!byRef.TryGetValue(
                proposal.ExportRef,
                out var source))
            {
                errors.Add(
                    new ReviewCorrectionWireError(
                        ReviewCorrectionWireErrorCode.UnknownExportRef,
                        "Correction targets an exportRef that is not present in the review package.",
                        proposal.ExportRef));
                continue;
            }

            if (!string.Equals(
                proposal.SourceFingerprint,
                source.SourceFingerprint,
                StringComparison.Ordinal))
            {
                errors.Add(
                    new ReviewCorrectionWireError(
                        ReviewCorrectionWireErrorCode.FingerprintMismatch,
                        "Correction sourceFingerprint does not match the exported source.",
                        proposal.ExportRef));
            }

            var validation =
                ReviewCorrectionValidator.Validate(
                    proposal,
                    new ReviewCorrectionValidationContext(
                        exportPackage.ExportSessionId,
                        source.SourceFingerprint,
                        source.Controls.CleanText.Length));

            foreach (var error
                in validation.Errors)
            {
                errors.Add(
                    new ReviewCorrectionWireError(
                        ReviewCorrectionWireErrorCode.DomainValidationFailed,
                        $"{error.Code}: {error.Message}",
                        proposal.ExportRef));
            }
        }

        var uniqueKnownRefs =
            decoded.Proposals
                .Select(x => x.ExportRef)
                .Where(byRef.ContainsKey)
                .Distinct(
                    StringComparer.Ordinal)
                .ToHashSet(
                    StringComparer.Ordinal);

        var missing =
            byRef.Keys
                .Where(x =>
                    !uniqueKnownRefs.Contains(x))
                .ToArray();

        if (missing.Length > 0)
        {
            errors.Add(
                new ReviewCorrectionWireError(
                    ReviewCorrectionWireErrorCode.IncompleteCoverage,
                    "LLM response must contain exactly one correction record for every exported VoiceItem. Missing: "
                    + string.Join(", ", missing)));
        }

        return new ReviewCorrectionDecodeResult(
            wire,
            decoded.Proposals,
            errors);
    }

    static bool TryConvertOperation(
        ReviewCorrectionWireOperation? operation,
        out ReviewCorrectionOperation? converted,
        out ReviewCorrectionWireError? error)
    {
        converted = null;
        error = null;

        if (operation is null
            || string.IsNullOrWhiteSpace(
                operation.Type))
        {
            error =
                new ReviewCorrectionWireError(
                    ReviewCorrectionWireErrorCode.UnknownOperation,
                    "Operation type is required.");
            return false;
        }

        switch (operation.Type)
        {
            case "setReading":
                if (operation.Reading is null)
                    return InvalidPayload(
                        out converted,
                        out error,
                        "setReading requires reading.");

                converted =
                    new SetReadingCorrection(
                        operation.Reading);
                return true;

            case "addBoundary":
                if (operation.Position is null)
                    return InvalidPayload(
                        out converted,
                        out error,
                        "addBoundary requires position.");

                converted =
                    new AddBoundaryCorrection(
                        operation.Position.Value);
                return true;

            case "removeBoundary":
                if (operation.Position is null)
                    return InvalidPayload(
                        out converted,
                        out error,
                        "removeBoundary requires position.");

                converted =
                    new RemoveBoundaryCorrection(
                        operation.Position.Value);
                return true;

            case "helperVowelZero":
                if (operation.Position is null
                    || operation.Helper is null)
                {
                    return InvalidPayload(
                        out converted,
                        out error,
                        "helperVowelZero requires position and helper.");
                }

                converted =
                    new HelperVowelZeroCorrection(
                        operation.Position.Value,
                        operation.Helper);
                return true;

            case "helperConsonantZero":
                if (operation.Position is null
                    || operation.Helper is null)
                {
                    return InvalidPayload(
                        out converted,
                        out error,
                        "helperConsonantZero requires position and helper.");
                }

                converted =
                    new HelperConsonantZeroCorrection(
                        operation.Position.Value,
                        operation.Helper);
                return true;

            case "setProsodyGesture":
                if (!TryParseGesture(
                    operation.Gesture,
                    out var gesture))
                {
                    return InvalidPayload(
                        out converted,
                        out error,
                        "setProsodyGesture requires one of: none, lightRise, lightFall, hold.");
                }

                converted =
                    new SetProsodyGestureCorrection(
                        gesture);
                return true;

            case "noChange":
                converted =
                    new NoChangeCorrection();
                return true;

            default:
                error =
                    new ReviewCorrectionWireError(
                        ReviewCorrectionWireErrorCode.UnknownOperation,
                        "Unknown operation type: "
                        + operation.Type);
                return false;
        }

    }

    static bool InvalidPayload(
        out ReviewCorrectionOperation? converted,
        out ReviewCorrectionWireError? error,
        string message)
    {
        converted = null;
        error =
            new ReviewCorrectionWireError(
                ReviewCorrectionWireErrorCode.InvalidPayload,
                message);
        return false;
    }

    static bool TryParseGesture(
        string? value,
        out ProsodyGesture gesture)
    {
        gesture =
            value switch
            {
                "none" =>
                    ProsodyGesture.None,
                "lightRise" =>
                    ProsodyGesture.LightRise,
                "lightFall" =>
                    ProsodyGesture.LightFall,
                "hold" =>
                    ProsodyGesture.Hold,
                _ =>
                    (ProsodyGesture)(-1),
            };

        return Enum.IsDefined(
            gesture);
    }

    static ReviewCorrectionDecodeResult Failure(
        ReviewCorrectionWireErrorCode code,
        string message) =>
        new(
            null,
            [],
            [
                new ReviewCorrectionWireError(
                    code,
                    message),
            ]);
}
