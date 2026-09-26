using Ymm4VoiceQualityAssist.Core;

namespace Ymm4VoiceQualityAssist.Tests;

public sealed class ReviewImportPreviewTextTests
{
    [Fact]
    public void BoundaryChange_UsesForcedAutomaticAccentLabel()
    {
        var preview =
            new ReviewImportPreview(
                "エービーシー",
                "エービーシー",
                [1],
                [2],
                [],
                [],
                null,
                false);

        var text =
            ReviewImportPreviewText.Format(
                preview);

        Assert.Contains(
            "VOICEVOX自動アクセント用の強制区切り",
            text,
            StringComparison.Ordinal);

        Assert.Contains(
            "[1] → [2]",
            text,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "pause=0",
            text,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoChange_LabelRemainsStable()
    {
        var preview =
            new ReviewImportPreview(
                "ホンブン",
                "ホンブン",
                [],
                [],
                [],
                [],
                null,
                true);

        Assert.Equal(
            "変更なし（noChange）",
            ReviewImportPreviewText.Format(
                preview));
    }
}
