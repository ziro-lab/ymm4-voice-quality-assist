using System.Text;
using Ymm4VoiceQualityAssist.Core;

namespace Ymm4VoiceQualityAssist.Tests;

public sealed class ReviewIdentityTests
{
    [Fact]
    public void Fingerprint_CanonicalVector_IsStable()
    {
        var result = SourceFingerprint.Create(
            new SourceFingerprintInput(
                "小夜",
                "東京<w0>大学\n😀",
                "トウキョウダイガク",
                [
                    new SourceFingerprintAssistProfile(
                        ProsodyGesture.LightRise,
                        [
                            new SourceFingerprintHelperRule(
                                HelperMoraKind.ZeroVowel,
                                "ウ",
                                2,
                                "東京",
                                "大学"),
                        ]),
                ]));

        const string expectedJson =
            """{"characterName":"小夜","serif":"東京<w0>大学\n😀","hatsuon":"トウキョウダイガク","assistSource":{"enabled":true,"profiles":[{"prosody":"lightRise","helperRules":[{"kind":"zeroVowel","helper":"ウ","position":2,"left":"東京","right":"大学"}]}]}}""";

        Assert.Equal(
            expectedJson,
            result.CanonicalJson);

        Assert.Equal(
            "sha256:c60391f9267827b1d5297e5abf17df5d97d9ad7f5d5882d63f76122978138d0a",
            result.Fingerprint);

        Assert.Equal(
            64,
            result.Fingerprint["sha256:".Length..].Length);
    }

    [Fact]
    public void Fingerprint_AssistAndRuleOrdering_IsSemanticNotPositional()
    {
        var profileA = new SourceFingerprintAssistProfile(
            ProsodyGesture.None,
            [
                Rule(
                    HelperMoraKind.ZeroConsonant,
                    "セ",
                    4,
                    "CD",
                    "EF"),
                Rule(
                    HelperMoraKind.ZeroVowel,
                    "ウ",
                    2,
                    "AB",
                    "CD"),
            ]);

        var profileB = new SourceFingerprintAssistProfile(
            ProsodyGesture.Hold,
            []);

        var forward = SourceFingerprint.Create(
            Input([profileA, profileB]));

        var reversed = SourceFingerprint.Create(
            Input(
                [
                    profileB,
                    new SourceFingerprintAssistProfile(
                        ProsodyGesture.None,
                        profileA.HelperRules
                            .Reverse()
                            .ToArray()),
                ]));

        Assert.Equal(
            forward.CanonicalJson,
            reversed.CanonicalJson);

        Assert.Equal(
            forward.Fingerprint,
            reversed.Fingerprint);
    }

    [Fact]
    public void Fingerprint_NoAssist_UsesExplicitDisabledSource()
    {
        var result = SourceFingerprint.Create(
            Input([]));

        Assert.Contains(
            "\"assistSource\":{\"enabled\":false,\"profiles\":[]}",
            result.CanonicalJson,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Fingerprint_NullAndEmptyRemainDifferent()
    {
        var withNull = SourceFingerprint.Create(
            new SourceFingerprintInput(
                null,
                null,
                null,
                []));

        var withEmpty = SourceFingerprint.Create(
            new SourceFingerprintInput(
                string.Empty,
                string.Empty,
                string.Empty,
                []));

        Assert.NotEqual(
            withNull.Fingerprint,
            withEmpty.Fingerprint);
    }

    [Fact]
    public void Fingerprint_DoesNotTrimStrings()
    {
        var original = SourceFingerprint.Create(
            new SourceFingerprintInput(
                "小夜",
                "abc",
                "エービーシー",
                []));

        var spaced = SourceFingerprint.Create(
            new SourceFingerprintInput(
                "小夜",
                " abc ",
                "エービーシー",
                []));

        Assert.NotEqual(
            original.Fingerprint,
            spaced.Fingerprint);
    }

    [Fact]
    public void Fingerprint_DoesNotNormalizeUnicode()
    {
        var composed = SourceFingerprint.Create(
            new SourceFingerprintInput(
                "小夜",
                "é",
                "エ",
                []));

        var decomposed = SourceFingerprint.Create(
            new SourceFingerprintInput(
                "小夜",
                "e\u0301",
                "エ",
                []));

        Assert.NotEqual(
            composed.Fingerprint,
            decomposed.Fingerprint);
    }

    [Theory]
    [InlineData("character")]
    [InlineData("serif")]
    [InlineData("hatsuon")]
    [InlineData("prosody")]
    [InlineData("helper")]
    public void Fingerprint_SourceMeaningChange_ChangesHash(
        string field)
    {
        var baseline = SourceFingerprint.Create(
            Input(
                [
                    new SourceFingerprintAssistProfile(
                        ProsodyGesture.LightRise,
                        [
                            Rule(
                                HelperMoraKind.ZeroVowel,
                                "ウ",
                                2,
                                "AB",
                                "CD"),
                        ]),
                ]));

        var changed = field switch
        {
            "character" => SourceFingerprint.Create(
                new SourceFingerprintInput(
                    "ミコ",
                    "ABCD",
                    "エービーシーディー",
                    baselineProfiles())),

            "serif" => SourceFingerprint.Create(
                new SourceFingerprintInput(
                    "小夜",
                    "ABXCD",
                    "エービーシーディー",
                    baselineProfiles())),

            "hatsuon" => SourceFingerprint.Create(
                new SourceFingerprintInput(
                    "小夜",
                    "ABCD",
                    "エービーシー",
                    baselineProfiles())),

            "prosody" => SourceFingerprint.Create(
                Input(
                    [
                        new SourceFingerprintAssistProfile(
                            ProsodyGesture.LightFall,
                            [
                                Rule(
                                    HelperMoraKind.ZeroVowel,
                                    "ウ",
                                    2,
                                    "AB",
                                    "CD"),
                            ]),
                    ])),

            "helper" => SourceFingerprint.Create(
                Input(
                    [
                        new SourceFingerprintAssistProfile(
                            ProsodyGesture.LightRise,
                            [
                                Rule(
                                    HelperMoraKind.ZeroVowel,
                                    "オ",
                                    2,
                                    "AB",
                                    "CD"),
                            ]),
                    ])),

            _ => throw new ArgumentOutOfRangeException(
                nameof(field)),
        };

        Assert.NotEqual(
            baseline.Fingerprint,
            changed.Fingerprint);

        static IReadOnlyList<
            SourceFingerprintAssistProfile>
            baselineProfiles() =>
            [
                new SourceFingerprintAssistProfile(
                    ProsodyGesture.LightRise,
                    [
                        Rule(
                            HelperMoraKind.ZeroVowel,
                            "ウ",
                            2,
                            "AB",
                            "CD"),
                    ]),
            ];
    }

    [Fact]
    public void Resolver_OneExactFingerprint_CanAutoApply()
    {
        var result = ReviewTargetResolver.Resolve(
            "sha256:target",
            Locator(),
            [
                Candidate(
                    "A",
                    "sha256:other",
                    10,
                    1),
                Candidate(
                    "B",
                    "sha256:target",
                    20,
                    2),
            ]);

        Assert.Equal(
            ReviewTargetResolutionStatus.ExactFingerprintMatch,
            result.Status);

        Assert.Equal(
            "B",
            result.Item);

        Assert.True(
            result.CanAutoApply);
    }

    [Fact]
    public void Resolver_DuplicateExactFingerprint_IsAmbiguousEvenWithLocator()
    {
        var result = ReviewTargetResolver.Resolve(
            "sha256:same",
            new ReviewTargetLocator(
                20,
                2,
                "小夜",
                null,
                null),
            [
                Candidate(
                    "A",
                    "sha256:same",
                    10,
                    1),
                Candidate(
                    "B",
                    "sha256:same",
                    20,
                    2),
            ]);

        Assert.Equal(
            ReviewTargetResolutionStatus.Ambiguous,
            result.Status);

        Assert.False(
            result.CanAutoApply);

        Assert.Null(
            result.Item);
    }

    [Fact]
    public void Resolver_UniqueLocatorWithFingerprintMismatch_IsStale()
    {
        var result = ReviewTargetResolver.Resolve(
            "sha256:old",
            new ReviewTargetLocator(
                20,
                2,
                "小夜",
                "前",
                "後"),
            [
                new ReviewTargetCandidate<string>(
                    "B",
                    "sha256:new",
                    20,
                    2,
                    "小夜",
                    "前",
                    "後"),
            ]);

        Assert.Equal(
            ReviewTargetResolutionStatus.Stale,
            result.Status);

        Assert.Equal(
            "B",
            result.Item);

        Assert.False(
            result.CanAutoApply);
    }

    [Fact]
    public void Resolver_NoExactOrLocatorMatch_IsMissing()
    {
        var result = ReviewTargetResolver.Resolve(
            "sha256:old",
            new ReviewTargetLocator(
                100,
                8,
                "小夜",
                null,
                null),
            [
                Candidate(
                    "A",
                    "sha256:new",
                    10,
                    1),
            ]);

        Assert.Equal(
            ReviewTargetResolutionStatus.Missing,
            result.Status);

        Assert.Equal(
            0,
            result.CandidateCount);
    }

    [Fact]
    public void Resolver_MultipleLocatorMatches_IsAmbiguous()
    {
        var locator =
            new ReviewTargetLocator(
                null,
                null,
                "小夜",
                null,
                null);

        var result = ReviewTargetResolver.Resolve(
            "sha256:old",
            locator,
            [
                Candidate(
                    "A",
                    "sha256:new-a",
                    10,
                    1),
                Candidate(
                    "B",
                    "sha256:new-b",
                    20,
                    2),
            ]);

        Assert.Equal(
            ReviewTargetResolutionStatus.Ambiguous,
            result.Status);

        Assert.Equal(
            2,
            result.CandidateCount);
    }

    static SourceFingerprintInput Input(
        IReadOnlyList<
            SourceFingerprintAssistProfile>
            profiles) =>
        new(
            "小夜",
            "ABCD",
            "エービーシーディー",
            profiles);

    static SourceFingerprintHelperRule Rule(
        HelperMoraKind kind,
        string helper,
        int position,
        string left,
        string right) =>
        new(
            kind,
            helper,
            position,
            left,
            right);

    static ReviewTargetLocator Locator() =>
        new(
            null,
            null,
            null,
            null,
            null);

    static ReviewTargetCandidate<string> Candidate(
        string item,
        string fingerprint,
        int frame,
        int layer) =>
        new(
            item,
            fingerprint,
            frame,
            layer,
            "小夜",
            null,
            null);
}
