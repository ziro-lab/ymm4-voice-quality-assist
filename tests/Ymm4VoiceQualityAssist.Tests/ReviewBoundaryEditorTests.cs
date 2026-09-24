using Ymm4VoiceQualityAssist.Core;

namespace Ymm4VoiceQualityAssist.Tests;

public sealed class ReviewBoundaryEditorTests
{
    [Fact]
    public void AddRemove_RebuildsLiteralW0OnlySource()
    {
        var result =
            ReviewBoundaryEditor.Apply(
                "A<w0>BC",
                [
                    new RemoveBoundaryCorrection(1),
                    new AddBoundaryCorrection(2),
                ]);

        Assert.True(
            result.IsSuccess,
            result.Message);

        Assert.Equal(
            "AB<w0>C",
            result.Serif);

        Assert.Equal(
            [2],
            result.Boundaries);
    }

    [Fact]
    public void MultipleExistingMarkers_ArePreserved()
    {
        var result =
            ReviewBoundaryEditor.Apply(
                "A<w0>B<w0>CD",
                [
                    new AddBoundaryCorrection(3),
                ]);

        Assert.True(
            result.IsSuccess,
            result.Message);

        Assert.Equal(
            "A<w0>B<w0>C<w0>D",
            result.Serif);

        Assert.Equal(
            [1, 2, 3],
            result.Boundaries);
    }

    [Fact]
    public void OtherOfficialControlTag_FailsClosed()
    {
        var result =
            ReviewBoundaryEditor.Apply(
                "A<w100>B",
                [
                    new AddBoundaryCorrection(1),
                ]);

        Assert.Equal(
            ReviewBoundaryEditStatus.UnsupportedSourceShape,
            result.Status);

        Assert.False(
            result.IsSuccess);

        Assert.Null(
            result.Serif);
    }

    [Fact]
    public void ExistingBoundaryCannotBeAddedAgain()
    {
        var result =
            ReviewBoundaryEditor.Apply(
                "A<w0>B",
                [
                    new AddBoundaryCorrection(1),
                ]);

        Assert.Equal(
            ReviewBoundaryEditStatus.AddAlreadyExists,
            result.Status);
    }

    [Fact]
    public void MissingBoundaryCannotBeRemoved()
    {
        var result =
            ReviewBoundaryEditor.Apply(
                "AB",
                [
                    new RemoveBoundaryCorrection(1),
                ]);

        Assert.Equal(
            ReviewBoundaryEditStatus.RemoveMissing,
            result.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-1)]
    [InlineData(3)]
    public void BoundaryMustBeInterior(
        int position)
    {
        var result =
            ReviewBoundaryEditor.Apply(
                "AB",
                [
                    new AddBoundaryCorrection(
                        position),
                ]);

        Assert.Equal(
            ReviewBoundaryEditStatus.InvalidBoundary,
            result.Status);
    }

    [Fact]
    public void BoundaryCannotSplitSurrogatePair()
    {
        const string serif = "A😀B";

        var result =
            ReviewBoundaryEditor.Apply(
                serif,
                [
                    new AddBoundaryCorrection(2),
                ]);

        Assert.Equal(
            ReviewBoundaryEditStatus.SplitSurrogatePair,
            result.Status);
    }

    [Fact]
    public void BoundaryMaySitBeforeOrAfterEmoji()
    {
        const string serif = "A😀B";

        var before =
            ReviewBoundaryEditor.Apply(
                serif,
                [
                    new AddBoundaryCorrection(1),
                ]);

        Assert.True(
            before.IsSuccess,
            before.Message);

        Assert.Equal(
            "A<w0>😀B",
            before.Serif);

        var after =
            ReviewBoundaryEditor.Apply(
                serif,
                [
                    new AddBoundaryCorrection(3),
                ]);

        Assert.True(
            after.IsSuccess,
            after.Message);

        Assert.Equal(
            "A😀<w0>B",
            after.Serif);
    }

    [Fact]
    public void InvalidVisiblePseudoTag_IsPreservedAsText()
    {
        var result =
            ReviewBoundaryEditor.Apply(
                "A<not-a-tag>B",
                [
                    new AddBoundaryCorrection(1),
                ]);

        Assert.True(
            result.IsSuccess,
            result.Message);

        Assert.Equal(
            "A<w0><not-a-tag>B",
            result.Serif);
    }
}
