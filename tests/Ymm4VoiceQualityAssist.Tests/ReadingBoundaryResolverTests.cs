using Ymm4VoiceQualityAssist.Core;

namespace Ymm4VoiceQualityAssist.Tests;

public sealed class ReadingBoundaryResolverTests
{
    static PhraseReadingProjection Phrase(
        int index,
        bool pause,
        params string[] moras) =>
        new(index, moras, pause);

    [Fact]
    public void Resolve_ExactSameSpeakerPrefix_ResolvesUniquePausedPhrase()
    {
        var result = ReadingBoundaryResolver.Resolve(
            "トウキョウダイガク",
            "とうきょうだいがく",
            [new MarkerReading(2, "トウキョウ")],
            [
                Phrase(0, true, "ト", "ウ", "キョ", "ウ"),
                Phrase(1, false, "ダ", "イ", "ガ", "ク"),
            ]);

        Assert.True(result.IsSuccess);
        var boundary = Assert.Single(result.Boundaries);
        Assert.Equal(0, boundary.PhraseIndex);
        Assert.Equal("トウキョウ", boundary.NormalizedPrefixReading);
    }

    [Fact]
    public void Resolve_CurrentHatsuonDiffersFromSpeakerReading_FailsClosed()
    {
        var result = ReadingBoundaryResolver.Resolve(
            "トウキョウダイガク",
            "トウキョーダイガク",
            [new MarkerReading(2, "トウキョウ")],
            [
                Phrase(0, true, "ト", "ウ", "キョ", "ウ"),
                Phrase(1, false, "ダ", "イ", "ガ", "ク"),
            ]);

        Assert.Equal(BoundaryResolutionStatus.CurrentReadingMismatch, result.Status);
        Assert.Empty(result.Boundaries);
    }

    [Fact]
    public void Resolve_MoraStreamDiffersFromReading_FailsClosed()
    {
        var result = ReadingBoundaryResolver.Resolve(
            "トウキョウダイガク",
            "トウキョウダイガク",
            [new MarkerReading(2, "トウキョウ")],
            [
                Phrase(0, true, "ト", "ウ", "キョ"),
                Phrase(1, false, "ダ", "イ", "ガ", "ク"),
            ]);

        Assert.Equal(BoundaryResolutionStatus.MoraStreamMismatch, result.Status);
        Assert.Empty(result.Boundaries);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Resolve_NonInteriorMarker_FailsClosed(int position)
    {
        var result = ReadingBoundaryResolver.Resolve(
            "アイ",
            "アイ",
            [new MarkerReading(position, "ア")],
            [Phrase(0, true, "ア"), Phrase(1, false, "イ")]);

        Assert.Equal(BoundaryResolutionStatus.InvalidMarkerPosition, result.Status);
        Assert.Empty(result.Boundaries);
    }

    [Fact]
    public void Resolve_PrefixNotStrictPrefix_FailsClosed()
    {
        var result = ReadingBoundaryResolver.Resolve(
            "アイ",
            "アイ",
            [new MarkerReading(1, "アイ")],
            [Phrase(0, true, "ア"), Phrase(1, false, "イ")]);

        Assert.Equal(BoundaryResolutionStatus.PrefixNotInFullReading, result.Status);
        Assert.Empty(result.Boundaries);
    }

    [Fact]
    public void Resolve_PrefixDoesNotEndAtPhraseBoundary_FailsClosed()
    {
        var result = ReadingBoundaryResolver.Resolve(
            "アイウ",
            "アイウ",
            [new MarkerReading(1, "ア")],
            [Phrase(0, true, "ア", "イ"), Phrase(1, false, "ウ")]);

        Assert.Equal(BoundaryResolutionStatus.NoUniquePhraseBoundary, result.Status);
        Assert.Empty(result.Boundaries);
    }

    [Fact]
    public void Resolve_TargetPhraseHasNoPauseMora_FailsClosed()
    {
        var result = ReadingBoundaryResolver.Resolve(
            "アイ",
            "アイ",
            [new MarkerReading(1, "ア")],
            [Phrase(0, false, "ア"), Phrase(1, false, "イ")]);

        Assert.Equal(BoundaryResolutionStatus.MissingPauseMora, result.Status);
        Assert.Empty(result.Boundaries);
    }

    [Fact]
    public void Resolve_TwoMarkersCollapseToSamePhrase_FailsClosed()
    {
        var result = ReadingBoundaryResolver.Resolve(
            "アイ",
            "アイ",
            [
                new MarkerReading(1, "ア"),
                new MarkerReading(2, "ア"),
            ],
            [Phrase(0, true, "ア"), Phrase(1, false, "イ")]);

        Assert.Equal(BoundaryResolutionStatus.DuplicateTarget, result.Status);
        Assert.Empty(result.Boundaries);
    }

    [Fact]
    public void Normalize_FoldsHiraganaAndIgnoredPunctuation()
    {
        Assert.Equal(
            "トウキョウダイガク",
            ReadingNormalizer.Normalize(" とうきょう、だいがく！ "));
    }

    [Fact]
    public void Resolve_MultipleDistinctMarkers_ResolvesAtomically()
    {
        var result = ReadingBoundaryResolver.Resolve(
            "アイウエオ",
            "アイウエオ",
            [
                new MarkerReading(1, "アイ"),
                new MarkerReading(2, "アイウエ"),
            ],
            [
                Phrase(0, true, "ア", "イ"),
                Phrase(1, true, "ウ", "エ"),
                Phrase(2, false, "オ"),
            ]);

        Assert.True(result.IsSuccess);
        Assert.Equal([0, 1], result.Boundaries.Select(x => x.PhraseIndex).ToArray());
    }
}
