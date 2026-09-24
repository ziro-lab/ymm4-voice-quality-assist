using Ymm4VoiceQualityAssist.Core;

namespace Ymm4VoiceQualityAssist.Tests;

public sealed class BoundaryMarkerParserTests
{
    [Fact]
    public void Parse_OfficialZeroWaitMarkers_ReturnsCleanTextPositions()
    {
        var result = BoundaryMarkerParser.Parse("A<w0>B<w0>C");

        Assert.Equal("ABC", result.CleanText);
        Assert.Equal([1, 2], result.ZeroWaitPositions);
    }

    [Fact]
    public void Parse_NonzeroWait_IsNotAssistBoundary()
    {
        var result = BoundaryMarkerParser.Parse("A<w100>B");

        Assert.Equal("AB", result.CleanText);
        Assert.Empty(result.ZeroWaitPositions);
    }
}
