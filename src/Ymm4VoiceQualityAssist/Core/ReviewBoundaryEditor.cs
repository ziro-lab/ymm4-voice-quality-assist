namespace Ymm4VoiceQualityAssist.Core;

public enum ReviewBoundaryEditStatus
{
    Success,
    UnsupportedSourceShape,
    InvalidBoundary,
    SplitSurrogatePair,
    AddAlreadyExists,
    RemoveMissing,
}

public sealed record ReviewBoundaryEditResult(
    ReviewBoundaryEditStatus Status,
    string? Serif,
    IReadOnlyList<int> Boundaries,
    string? Message)
{
    public bool IsSuccess =>
        Status == ReviewBoundaryEditStatus.Success
        && Serif is not null;

    public static ReviewBoundaryEditResult Success(
        string serif,
        IReadOnlyList<int> boundaries) =>
        new(
            ReviewBoundaryEditStatus.Success,
            serif,
            boundaries,
            null);

    public static ReviewBoundaryEditResult Failure(
        ReviewBoundaryEditStatus status,
        string message) =>
        new(
            status,
            null,
            [],
            message);
}

public static class ReviewBoundaryEditor
{
    const string Marker = "<w0>";

    public static ReviewBoundaryEditResult Apply(
        string serif,
        IReadOnlyList<ReviewCorrectionOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(serif);
        ArgumentNullException.ThrowIfNull(operations);

        BoundaryMarkerParseResult parsed;

        try
        {
            parsed =
                BoundaryMarkerParser.Parse(serif);
        }
        catch (Exception ex)
        {
            return ReviewBoundaryEditResult.Failure(
                ReviewBoundaryEditStatus.UnsupportedSourceShape,
                "Serif control tags could not be parsed: "
                + ex.GetBaseException().Message);
        }

        // B3 v0 intentionally supports automatic boundary rewriting only when
        // every stripped official control tag is exactly our literal <w0>.
        // If any other official tag is present, reconstructing from clean text
        // could destroy source information, so fail closed.
        var literalStripped =
            serif.Replace(
                Marker,
                string.Empty,
                StringComparison.Ordinal);

        if (!string.Equals(
            literalStripped,
            parsed.CleanText,
            StringComparison.Ordinal))
        {
            return ReviewBoundaryEditResult.Failure(
                ReviewBoundaryEditStatus.UnsupportedSourceShape,
                "Serif contains supported YMM4 control tags other than literal <w0>; B3 v0 will not rewrite that source automatically.");
        }

        var current =
            parsed.ZeroWaitPositions
                .ToHashSet();

        var adds =
            operations
                .OfType<AddBoundaryCorrection>()
                .Select(x => x.CleanTextPosition)
                .ToArray();

        var removes =
            operations
                .OfType<RemoveBoundaryCorrection>()
                .Select(x => x.CleanTextPosition)
                .ToArray();

        foreach (var position
            in adds.Concat(removes))
        {
            if (position <= 0
                || position >= parsed.CleanText.Length)
            {
                return ReviewBoundaryEditResult.Failure(
                    ReviewBoundaryEditStatus.InvalidBoundary,
                    $"Boundary {position} must be an interior clean-text boundary.");
            }

            if (SplitsSurrogatePair(
                parsed.CleanText,
                position))
            {
                return ReviewBoundaryEditResult.Failure(
                    ReviewBoundaryEditStatus.SplitSurrogatePair,
                    $"Boundary {position} splits a UTF-16 surrogate pair.");
            }
        }

        foreach (var position in removes)
        {
            if (!current.Remove(position))
            {
                return ReviewBoundaryEditResult.Failure(
                    ReviewBoundaryEditStatus.RemoveMissing,
                    $"No <w0> boundary exists at clean-text position {position}.");
            }
        }

        foreach (var position in adds)
        {
            if (!current.Add(position))
            {
                return ReviewBoundaryEditResult.Failure(
                    ReviewBoundaryEditStatus.AddAlreadyExists,
                    $"A <w0> boundary already exists at clean-text position {position}.");
            }
        }

        var ordered =
            current.OrderBy(x => x).ToArray();

        return ReviewBoundaryEditResult.Success(
            Rebuild(
                parsed.CleanText,
                ordered),
            ordered);
    }

    public static string Rebuild(
        string cleanText,
        IReadOnlyList<int> boundaries)
    {
        ArgumentNullException.ThrowIfNull(cleanText);
        ArgumentNullException.ThrowIfNull(boundaries);

        var ordered =
            boundaries
                .Distinct()
                .OrderBy(x => x)
                .ToArray();

        foreach (var position in ordered)
        {
            if (position <= 0
                || position >= cleanText.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(boundaries),
                    $"Boundary {position} must be interior.");
            }

            if (SplitsSurrogatePair(
                cleanText,
                position))
            {
                throw new ArgumentException(
                    $"Boundary {position} splits a surrogate pair.",
                    nameof(boundaries));
            }
        }

        var builder =
            new System.Text.StringBuilder(
                cleanText.Length
                + (ordered.Length * Marker.Length));

        var boundarySet =
            ordered.ToHashSet();

        for (var index = 0;
             index < cleanText.Length;
             index++)
        {
            if (boundarySet.Contains(index))
                builder.Append(Marker);

            builder.Append(cleanText[index]);
        }

        return builder.ToString();
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
}
