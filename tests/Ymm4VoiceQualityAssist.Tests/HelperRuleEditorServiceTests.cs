using Ymm4VoiceQualityAssist.Core;

namespace Ymm4VoiceQualityAssist.Tests;

public sealed class HelperRuleEditorServiceTests
{
    [Fact]
    public void Add_CreatesSemanticAnchorFromCurrentText()
    {
        var result =
            HelperRuleEditorService.Add(
                "",
                "これはテスト",
                "ウ",
                HelperMoraKind.ZeroVowel,
                3);

        Assert.True(
            result.IsSuccess,
            result.Error);

        Assert.True(
            HelperRuleCodec.TryDecode(
                result.UpdatedJson,
                out var decoded,
                out var error),
            error);

        var rule =
            Assert.Single(
                decoded.Rules);

        Assert.Equal(
            "ウ",
            rule.Helper);

        Assert.Equal(
            HelperMoraKind.ZeroVowel,
            rule.Kind);

        Assert.Equal(
            3,
            rule.Anchor.Position);

        Assert.Equal(
            "これは",
            rule.Anchor.Left);

        Assert.Equal(
            "テスト",
            rule.Anchor.Right);
    }

    [Fact]
    public void Replace_RefreshesAnchorAndKeepsOtherRules()
    {
        var first =
            HelperRuleFactory.Create(
                "ABCDE",
                1,
                "ア",
                HelperMoraKind.ZeroVowel);

        var second =
            HelperRuleFactory.Create(
                "ABCDE",
                4,
                "イ",
                HelperMoraKind.ZeroConsonant);

        var json =
            HelperRuleCodec.Encode(
                new HelperRuleSet(
                    HelperRuleSet.CurrentVersion,
                    [first, second]));

        var result =
            HelperRuleEditorService.Replace(
                json,
                "ABXCDE",
                0,
                "ウ",
                HelperMoraKind.ZeroConsonant,
                3);

        Assert.True(
            result.IsSuccess,
            result.Error);

        HelperRuleCodec.TryDecode(
            result.UpdatedJson,
            out var decoded,
            out _);

        Assert.Equal(
            2,
            decoded.Rules.Count);

        Assert.Equal(
            "ウ",
            decoded.Rules[0].Helper);

        Assert.Equal(
            3,
            decoded.Rules[0].Anchor.Position);

        Assert.Equal(
            "ABX",
            decoded.Rules[0].Anchor.Left);

        Assert.Equal(
            "CDE",
            decoded.Rules[0].Anchor.Right);

        Assert.Equal(
            "イ",
            decoded.Rules[1].Helper);
    }

    [Fact]
    public void Remove_RemovesOnlySelectedRule()
    {
        var json =
            HelperRuleCodec.Encode(
                new HelperRuleSet(
                    HelperRuleSet.CurrentVersion,
                    [
                        HelperRuleFactory.Create(
                            "ABCDE",
                            1,
                            "ア",
                            HelperMoraKind.ZeroVowel),
                        HelperRuleFactory.Create(
                            "ABCDE",
                            4,
                            "イ",
                            HelperMoraKind.ZeroConsonant),
                    ]));

        var result =
            HelperRuleEditorService.Remove(
                json,
                0);

        Assert.True(
            result.IsSuccess,
            result.Error);

        HelperRuleCodec.TryDecode(
            result.UpdatedJson,
            out var decoded,
            out _);

        var rule =
            Assert.Single(
                decoded.Rules);

        Assert.Equal(
            "イ",
            rule.Helper);
    }

    [Fact]
    public void Add_DuplicateBoundary_FailsClosed()
    {
        var json =
            HelperRuleCodec.Encode(
                new HelperRuleSet(
                    HelperRuleSet.CurrentVersion,
                    [
                        HelperRuleFactory.Create(
                            "ABCDE",
                            2,
                            "ア",
                            HelperMoraKind.ZeroVowel),
                    ]));

        var result =
            HelperRuleEditorService.Add(
                json,
                "ABCDE",
                "ウ",
                HelperMoraKind.ZeroVowel,
                2);

        Assert.False(
            result.IsSuccess);

        Assert.Null(
            result.UpdatedJson);
    }

    [Fact]
    public void Replace_InvalidPosition_DoesNotProduceJson()
    {
        var json =
            HelperRuleCodec.Encode(
                new HelperRuleSet(
                    HelperRuleSet.CurrentVersion,
                    [
                        HelperRuleFactory.Create(
                            "ABC",
                            1,
                            "ア",
                            HelperMoraKind.ZeroVowel),
                    ]));

        var result =
            HelperRuleEditorService.Replace(
                json,
                "ABC",
                0,
                "イ",
                HelperMoraKind.ZeroVowel,
                99);

        Assert.False(
            result.IsSuccess);

        Assert.Null(
            result.UpdatedJson);
    }

    [Fact]
    public void ContextPreview_ShowsInsertionBoundary()
    {
        Assert.Equal(
            "ABC｜DEF",
            HelperRuleEditorService
                .CreateContextPreview(
                    "ABCDEF",
                    3));
    }
}
