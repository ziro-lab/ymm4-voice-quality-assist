using Ymm4VoiceQualityAssist.Core;

namespace Ymm4VoiceQualityAssist.Tests;

public sealed class HelperRuleTests
{
    [Fact]
    public void Codec_RoundTripsVersionedRuleSet()
    {
        var rule = HelperRuleFactory.Create(
            "東京大学",
            2,
            "ウ",
            HelperMoraKind.ZeroVowel,
            contextLength: 2);

        var source = new HelperRuleSet(
            HelperRuleSet.CurrentVersion,
            [rule]);

        var json = HelperRuleCodec.Encode(source);

        Assert.True(
            HelperRuleCodec.TryDecode(
                json,
                out var decoded,
                out var error),
            error);

        Assert.Equal(1, decoded.Version);
        var restored = Assert.Single(decoded.Rules);
        Assert.Equal(HelperMoraKind.ZeroVowel, restored.Kind);
        Assert.Equal("ウ", restored.Helper);
        Assert.Equal(2, restored.Anchor.Position);
        Assert.Equal("東京", restored.Anchor.Left);
        Assert.Equal("大学", restored.Anchor.Right);
    }

    [Fact]
    public void Codec_RejectsUnknownVersion()
    {
        const string json =
            """
            {"version":2,"rules":[]}
            """;

        Assert.False(
            HelperRuleCodec.TryDecode(
                json,
                out _,
                out var error));

        Assert.Contains(
            "Unsupported helper rule version",
            error);
    }

    [Fact]
    public void Codec_EmptyStringMeansNoRules()
    {
        Assert.True(
            HelperRuleCodec.TryDecode(
                "",
                out var decoded,
                out var error),
            error);

        Assert.Empty(decoded.Rules);
    }

    [Fact]
    public void AnchorResolver_UsesStoredPositionWhenContextStillMatches()
    {
        var rule = HelperRuleFactory.Create(
            "ABCDE",
            2,
            "ウ",
            HelperMoraKind.ZeroVowel,
            contextLength: 2);

        var result = HelperAnchorResolver.Resolve(
            "ABCDE",
            [rule]);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            2,
            Assert.Single(result.Anchors).Position);
    }

    [Fact]
    public void AnchorResolver_ReResolvesAfterTextInsertedBeforeAnchor()
    {
        var rule = HelperRuleFactory.Create(
            "ABCDE",
            2,
            "ウ",
            HelperMoraKind.ZeroVowel,
            contextLength: 2);

        var result = HelperAnchorResolver.Resolve(
            "XXABCDE",
            [rule]);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            4,
            Assert.Single(result.Anchors).Position);
    }

    [Fact]
    public void AnchorResolver_AmbiguousContextFailsClosed()
    {
        var rule = HelperRuleFactory.Create(
            "ABX",
            2,
            "ウ",
            HelperMoraKind.ZeroVowel,
            contextLength: 2);

        var result = HelperAnchorResolver.Resolve(
            "ABXABX",
            [rule]);

        Assert.Equal(
            HelperAnchorResolutionStatus.Ambiguous,
            result.Status);
        Assert.Empty(result.Anchors);
    }

    [Fact]
    public void AnchorResolver_MissingContextFailsClosed()
    {
        var rule = HelperRuleFactory.Create(
            "ABCDE",
            2,
            "ウ",
            HelperMoraKind.ZeroVowel,
            contextLength: 2);

        var result = HelperAnchorResolver.Resolve(
            "ABZZE",
            [rule]);

        Assert.Equal(
            HelperAnchorResolutionStatus.Missing,
            result.Status);
    }

    [Fact]
    public void ReadingBoundaryLocator_NormalizesHiraganaPrefix()
    {
        var candidates = ReadingBoundaryLocator.FindCandidates(
            "トウキョウダイガク",
            "とうきょう");

        Assert.Equal([5], candidates);
    }

    [Fact]
    public void ReadingBoundaryLocator_IgnoredPunctuationCanMakeBoundaryAmbiguous()
    {
        var candidates = ReadingBoundaryLocator.FindCandidates(
            "ア、イ",
            "ア");

        Assert.Equal([1, 2], candidates);
    }
}
