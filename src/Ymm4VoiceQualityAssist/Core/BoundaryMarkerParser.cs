using System.Collections.Immutable;
using YukkuriMovieMaker.Commons;
using YmmTextDecoration = YukkuriMovieMaker.Commons.TextDecoration;

namespace Ymm4VoiceQualityAssist.Core;

public sealed record BoundaryMarkerParseResult(
    string CleanText,
    IReadOnlyList<int> ZeroWaitPositions);

public static class BoundaryMarkerParser
{
    public static BoundaryMarkerParseResult Parse(string serif)
    {
        ArgumentNullException.ThrowIfNull(serif);

        var parsed = ControlTagParser.Parse(
            serif,
            ImmutableList<YmmTextDecoration>.Empty,
            32.0,
            "Yu Gothic UI",
            false,
            false);

        var positions = parsed.Item3
            .Where(tag =>
                tag.Type.ToString() == "Wait"
                && Math.Abs(Convert.ToDouble(
                    tag.Value,
                    System.Globalization.CultureInfo.InvariantCulture)) < 0.000001
                && tag.Operator.ToString() == "Set")
            .Select(tag => tag.Position)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        return new BoundaryMarkerParseResult(parsed.Item1, positions);
    }
}
