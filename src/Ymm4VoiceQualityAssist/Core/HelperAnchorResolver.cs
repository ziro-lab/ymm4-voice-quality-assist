namespace Ymm4VoiceQualityAssist.Core;

public enum HelperAnchorResolutionStatus
{
    Success,
    InvalidAnchor,
    Missing,
    Ambiguous,
}

public sealed record HelperAnchorResolution(
    HelperAnchorResolutionStatus Status,
    int Position,
    bool Relocated,
    string? Message)
{
    public bool IsSuccess =>
        Status == HelperAnchorResolutionStatus.Success;

    public static HelperAnchorResolution Success(
        int position,
        bool relocated) =>
        new(
            HelperAnchorResolutionStatus.Success,
            position,
            relocated,
            null);

    public static HelperAnchorResolution Failure(
        HelperAnchorResolutionStatus status,
        string message) =>
        new(status, -1, false, message);
}

public static class HelperAnchorResolver
{
    public const int DefaultContextRadius = 6;

    public static HelperAnchor Capture(
        string cleanText,
        int position,
        int contextRadius = DefaultContextRadius)
    {
        ArgumentNullException.ThrowIfNull(cleanText);

        if (position < 0 || position > cleanText.Length)
            throw new ArgumentOutOfRangeException(nameof(position));

        if (contextRadius < 1)
            throw new ArgumentOutOfRangeException(nameof(contextRadius));

        var leftStart = Math.Max(0, position - contextRadius);
        var left = cleanText[leftStart..position];

        var rightEnd = Math.Min(
            cleanText.Length,
            position + contextRadius);
        var right = cleanText[position..rightEnd];

        return new HelperAnchor(
            position,
            left,
            right);
    }

    public static HelperAnchorResolution Resolve(
        string cleanText,
        HelperAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(cleanText);
        ArgumentNullException.ThrowIfNull(anchor);

        if (anchor.Position < 0
            || anchor.LeftContext is null
            || anchor.RightContext is null)
        {
            return HelperAnchorResolution.Failure(
                HelperAnchorResolutionStatus.InvalidAnchor,
                "Helper anchor is invalid.");
        }

        if (anchor.Position <= cleanText.Length
            && Matches(
                cleanText,
                anchor.Position,
                anchor))
        {
            return HelperAnchorResolution.Success(
                anchor.Position,
                relocated: false);
        }

        var candidates = new List<int>();

        for (var position = 0;
             position <= cleanText.Length;
             position++)
        {
            if (Matches(cleanText, position, anchor))
                candidates.Add(position);
        }

        return candidates.Count switch
        {
            0 => HelperAnchorResolution.Failure(
                HelperAnchorResolutionStatus.Missing,
                "Helper anchor context was not found in the current source."),

            1 => HelperAnchorResolution.Success(
                candidates[0],
                relocated: true),

            _ => HelperAnchorResolution.Failure(
                HelperAnchorResolutionStatus.Ambiguous,
                $"Helper anchor context matched {candidates.Count} positions."),
        };
    }

    static bool Matches(
        string cleanText,
        int position,
        HelperAnchor anchor)
    {
        if (position < 0 || position > cleanText.Length)
            return false;

        if (anchor.LeftContext.Length > position)
            return false;

        if (anchor.RightContext.Length
            > cleanText.Length - position)
        {
            return false;
        }

        var leftStart =
            position - anchor.LeftContext.Length;

        if (!cleanText.AsSpan(
                leftStart,
                anchor.LeftContext.Length)
            .SequenceEqual(anchor.LeftContext.AsSpan()))
        {
            return false;
        }

        return cleanText.AsSpan(
                position,
                anchor.RightContext.Length)
            .SequenceEqual(anchor.RightContext.AsSpan());
    }
}
