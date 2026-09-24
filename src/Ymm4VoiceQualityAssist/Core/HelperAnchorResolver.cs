namespace Ymm4VoiceQualityAssist.Core;

public enum HelperAnchorResolutionStatus
{
    Success,
    InvalidStoredPosition,
    Missing,
    Ambiguous,
}

public sealed record ResolvedHelperAnchor(
    HelperMoraRule Rule,
    int Position);

public sealed record HelperAnchorResolutionResult(
    HelperAnchorResolutionStatus Status,
    IReadOnlyList<ResolvedHelperAnchor> Anchors,
    string? Message)
{
    public bool IsSuccess => Status == HelperAnchorResolutionStatus.Success;

    public static HelperAnchorResolutionResult Success(
        IReadOnlyList<ResolvedHelperAnchor> anchors) =>
        new(HelperAnchorResolutionStatus.Success, anchors, null);

    public static HelperAnchorResolutionResult Failure(
        HelperAnchorResolutionStatus status,
        string message) =>
        new(status, [], message);
}

public static class HelperAnchorResolver
{
    public static HelperAnchorResolutionResult Resolve(
        string cleanSerif,
        IReadOnlyList<HelperMoraRule> rules)
    {
        ArgumentNullException.ThrowIfNull(cleanSerif);
        ArgumentNullException.ThrowIfNull(rules);

        var resolved = new List<ResolvedHelperAnchor>();

        foreach (var rule in rules)
        {
            var anchor = rule.Anchor;

            if (anchor.Position < 0)
            {
                return HelperAnchorResolutionResult.Failure(
                    HelperAnchorResolutionStatus.InvalidStoredPosition,
                    "Stored helper position is negative.");
            }

            if (Matches(cleanSerif, anchor.Position, anchor))
            {
                resolved.Add(new ResolvedHelperAnchor(
                    rule,
                    anchor.Position));
                continue;
            }

            var candidates = Enumerable
                .Range(0, cleanSerif.Length + 1)
                .Where(position =>
                    Matches(cleanSerif, position, anchor))
                .ToArray();

            if (candidates.Length == 0)
            {
                return HelperAnchorResolutionResult.Failure(
                    HelperAnchorResolutionStatus.Missing,
                    $"Helper anchor at stored position {anchor.Position} could not be re-resolved.");
            }

            if (candidates.Length != 1)
            {
                return HelperAnchorResolutionResult.Failure(
                    HelperAnchorResolutionStatus.Ambiguous,
                    $"Helper anchor at stored position {anchor.Position} resolved to {candidates.Length} candidates.");
            }

            resolved.Add(new ResolvedHelperAnchor(
                rule,
                candidates[0]));
        }

        return HelperAnchorResolutionResult.Success(
            resolved
                .OrderBy(x => x.Position)
                .ToArray());
    }

    static bool Matches(
        string text,
        int position,
        HelperAnchor anchor)
    {
        if (position < 0 || position > text.Length)
            return false;

        if (anchor.Left.Length > position)
            return false;

        if (position + anchor.Right.Length > text.Length)
            return false;

        var leftStart = position - anchor.Left.Length;

        return text.AsSpan(
                leftStart,
                anchor.Left.Length)
            .SequenceEqual(anchor.Left.AsSpan())
            && text.AsSpan(
                position,
                anchor.Right.Length)
            .SequenceEqual(anchor.Right.AsSpan());
    }
}
