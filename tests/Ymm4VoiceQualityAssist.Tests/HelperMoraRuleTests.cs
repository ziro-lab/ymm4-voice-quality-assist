using Ymm4VoiceQualityAssist.Core;

namespace Ymm4VoiceQualityAssist.Tests;

public sealed class HelperMoraRuleTests
{
    [Fact]
    public void Codec_RoundTripsVersionedRules()
    {
        var source = new HelperMoraRuleSet(
            HelperMoraRuleSet.CurrentVersion,
            [
                new HelperMoraRule(
                    "rule-a",
                    HelperMoraKind.ZeroVowel,
                    "ウ",
                    new HelperAnchor(
                        2,
                        "前",
                        "後")),
                new HelperMoraRule(
                    "rule-b",
                    HelperMoraKind.ZeroConsonant,
                    "セ",
                    new HelperAnchor(
                        4,
                        "左",
                        "右")),
            ]);

        var json = HelperMoraRuleCodec.Encode(source);
        var decoded = HelperMoraRuleCodec.Decode(json);

        Assert.True(decoded.IsSuccess);
        Assert.Equal(
            HelperMoraRuleSet.CurrentVersion,
            decoded.RuleSet.Version);
        Assert.Equal(source.Rules, decoded.RuleSet.Rules);
        Assert.Contains(
            "\"kind\":\"zeroVowel\"",
            json,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Codec_EmptyString_ReturnsEmptyCurrentRuleSet()
    {
        var decoded = HelperMoraRuleCodec.Decode("");

        Assert.True(decoded.IsSuccess);
        Assert.Equal(
            HelperRuleDecodeStatus.Empty,
            decoded.Status);
        Assert.Empty(decoded.RuleSet.Rules);
        Assert.Equal(
            HelperMoraRuleSet.CurrentVersion,
            decoded.RuleSet.Version);
    }

    [Fact]
    public void Codec_UnsupportedVersion_FailsClosed()
    {
        const string json =
            """
            {"version":99,"rules":[]}
            """;

        var decoded = HelperMoraRuleCodec.Decode(json);

        Assert.False(decoded.IsSuccess);
        Assert.Equal(
            HelperRuleDecodeStatus.UnsupportedVersion,
            decoded.Status);
        Assert.Empty(decoded.RuleSet.Rules);
    }

    [Fact]
    public void Codec_DuplicateIds_FailsClosed()
    {
        const string json =
            """
            {
              "version":1,
              "rules":[
                {
                  "id":"same",
                  "kind":"zeroVowel",
                  "helper":"ウ",
                  "anchor":{
                    "position":1,
                    "leftContext":"ア",
                    "rightContext":"イ"
                  }
                },
                {
                  "id":"same",
                  "kind":"zeroConsonant",
                  "helper":"セ",
                  "anchor":{
                    "position":2,
                    "leftContext":"イ",
                    "rightContext":"ウ"
                  }
                }
              ]
            }
            """;

        var decoded = HelperMoraRuleCodec.Decode(json);

        Assert.False(decoded.IsSuccess);
        Assert.Equal(
            HelperRuleDecodeStatus.DuplicateId,
            decoded.Status);
    }

    [Fact]
    public void Anchor_ExactPositionAndContext_ResolvesWithoutRelocation()
    {
        var anchor = HelperAnchorResolver.Capture(
            "前半後半",
            2);

        var resolved = HelperAnchorResolver.Resolve(
            "前半後半",
            anchor);

        Assert.True(resolved.IsSuccess);
        Assert.Equal(2, resolved.Position);
        Assert.False(resolved.Relocated);
    }

    [Fact]
    public void Anchor_InsertionBeforeContext_RelocatesUniquely()
    {
        var anchor = HelperAnchorResolver.Capture(
            "甲乙境界丙丁",
            4,
            contextRadius: 2);

        var resolved = HelperAnchorResolver.Resolve(
            "追加甲乙境界丙丁",
            anchor);

        Assert.True(resolved.IsSuccess);
        Assert.Equal(6, resolved.Position);
        Assert.True(resolved.Relocated);
    }

    [Fact]
    public void Anchor_RepeatedContext_FailsAmbiguous()
    {
        var anchor = new HelperAnchor(
            99,
            "甲",
            "乙");

        var resolved = HelperAnchorResolver.Resolve(
            "甲乙xx甲乙",
            anchor);

        Assert.False(resolved.IsSuccess);
        Assert.Equal(
            HelperAnchorResolutionStatus.Ambiguous,
            resolved.Status);
    }

    [Fact]
    public void Anchor_ContextDestroyed_FailsMissing()
    {
        var anchor = HelperAnchorResolver.Capture(
            "甲乙丙丁",
            2,
            contextRadius: 2);

        var resolved = HelperAnchorResolver.Resolve(
            "甲乙変更丁",
            anchor);

        Assert.False(resolved.IsSuccess);
        Assert.Equal(
            HelperAnchorResolutionStatus.Missing,
            resolved.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void Anchor_CaptureSupportsSourceEdges(int position)
    {
        const string text = "甲乙丙丁";

        var anchor = HelperAnchorResolver.Capture(
            text,
            position,
            contextRadius: 3);

        var resolved = HelperAnchorResolver.Resolve(
            text,
            anchor);

        Assert.True(resolved.IsSuccess);
        Assert.Equal(position, resolved.Position);
    }
}
