using Ymm4VoiceQualityAssist.Core;

namespace Ymm4VoiceQualityAssist.Tests;

public sealed class ForcedBoundaryTextEditorTests
{
    [Theory]
    [InlineData("|")]
    [InlineData("｜")]
    [InlineData("||")]
    public void TokenValidation_AcceptsSafeTokens(
        string token)
    {
        Assert.True(
            ForcedBoundaryInputToken.TryValidate(
                token,
                out var error),
            error);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("<")]
    [InlineData(">")]
    [InlineData("<w0>")]
    [InlineData("\n")]
    [InlineData("123456789")]
    public void TokenValidation_RejectsUnsafeTokens(
        string token)
    {
        Assert.False(
            ForcedBoundaryInputToken.TryValidate(
                token,
                out _));
    }

    [Fact]
    public void NormalizeToken_ReplacesOnlyOutsideControlTags()
    {
        var result =
            ForcedBoundaryTextEditor.NormalizeToken(
                "A<w0>B0C",
                "0");

        Assert.True(
            result.IsSuccess,
            result.Message);

        Assert.Equal(
            ForcedBoundaryTextEditStatus.Success,
            result.Status);

        Assert.Equal(
            "A<w0>B<w0>C",
            result.UpdatedSerif);

        Assert.Equal(
            1,
            result.ChangedBoundaryCount);
    }

    [Fact]
    public void NormalizeToken_MultipleTokens_BecomeCanonicalMarkers()
    {
        var result =
            ForcedBoundaryTextEditor.NormalizeToken(
                "A|B|C",
                "|");

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "A<w0>B<w0>C",
            result.UpdatedSerif);
        Assert.Equal(
            2,
            result.ChangedBoundaryCount);
    }

    [Fact]
    public void NormalizeToken_MultiCharacterToken_UsesNonOverlappingMatches()
    {
        var result =
            ForcedBoundaryTextEditor.NormalizeToken(
                "A||B||C",
                "||");

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "A<w0>B<w0>C",
            result.UpdatedSerif);
        Assert.Equal(
            2,
            result.ChangedBoundaryCount);
    }

    [Theory]
    [InlineData("|ABC")]
    [InlineData("ABC|")]
    public void NormalizeToken_EndpointBoundary_FailsClosed(
        string serif)
    {
        var result =
            ForcedBoundaryTextEditor.NormalizeToken(
                serif,
                "|");

        Assert.Equal(
            ForcedBoundaryTextEditStatus.InvalidPosition,
            result.Status);
        Assert.Null(
            result.UpdatedSerif);
    }

    [Fact]
    public void NormalizeToken_AdjacentTokensSameBoundary_FailsClosed()
    {
        var result =
            ForcedBoundaryTextEditor.NormalizeToken(
                "A||B",
                "|");

        Assert.Equal(
            ForcedBoundaryTextEditStatus.InvalidPosition,
            result.Status);
        Assert.Null(
            result.UpdatedSerif);
    }

    [Fact]
    public void NormalizeToken_BesideExistingCanonicalMarker_FailsClosed()
    {
        var result =
            ForcedBoundaryTextEditor.NormalizeToken(
                "A<w0>|B",
                "|");

        Assert.Equal(
            ForcedBoundaryTextEditStatus.InvalidPosition,
            result.Status);
        Assert.Null(
            result.UpdatedSerif);
    }

    [Fact]
    public void NormalizeToken_NoToken_IsNoChange()
    {
        var result =
            ForcedBoundaryTextEditor.NormalizeToken(
                "ABC",
                "|");

        Assert.Equal(
            ForcedBoundaryTextEditStatus.NoChanges,
            result.Status);
        Assert.Equal(
            "ABC",
            result.UpdatedSerif);
    }

    [Fact]
    public void NormalizeToken_UnclosedTag_FailsClosed()
    {
        var result =
            ForcedBoundaryTextEditor.NormalizeToken(
                "A<w0B|C",
                "|");

        Assert.Equal(
            ForcedBoundaryTextEditStatus.InvalidSource,
            result.Status);
        Assert.Null(
            result.UpdatedSerif);
    }

    [Fact]
    public void InsertAtCleanTextPosition_PlainText_InsertsCanonicalMarker()
    {
        var result =
            ForcedBoundaryTextEditor
                .InsertAtCleanTextPosition(
                    "ABC",
                    1);

        Assert.True(
            result.IsSuccess,
            result.Message);

        Assert.Equal(
            "A<w0>BC",
            result.UpdatedSerif);
    }

    [Fact]
    public void InsertAtCleanTextPosition_PreservesExistingControlTag()
    {
        var result =
            ForcedBoundaryTextEditor
                .InsertAtCleanTextPosition(
                    "A<w100>B",
                    1);

        Assert.True(
            result.IsSuccess,
            result.Message);

        Assert.Equal(
            "A<w100><w0>B",
            result.UpdatedSerif);
    }

    [Fact]
    public void InsertAtCleanTextPosition_ExistingBoundary_IsNoChange()
    {
        var result =
            ForcedBoundaryTextEditor
                .InsertAtCleanTextPosition(
                    "A<w0>B",
                    1);

        Assert.Equal(
            ForcedBoundaryTextEditStatus.NoChanges,
            result.Status);
    }

    [Fact]
    public void InsertAtCleanTextPosition_RejectsSurrogateSplit()
    {
        var result =
            ForcedBoundaryTextEditor
                .InsertAtCleanTextPosition(
                    "A😀B",
                    2);

        Assert.Equal(
            ForcedBoundaryTextEditStatus.InvalidPosition,
            result.Status);

        Assert.Null(
            result.UpdatedSerif);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void InsertAtCleanTextPosition_RejectsEndpoints(
        int position)
    {
        var result =
            ForcedBoundaryTextEditor
                .InsertAtCleanTextPosition(
                    "ABC",
                    position);

        Assert.Equal(
            ForcedBoundaryTextEditStatus.InvalidPosition,
            result.Status);
    }
}
