using Ymm4VoiceQualityAssist.Core;
using Ymm4VoiceQualityAssist.Effects;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4VoiceQualityAssist.Tests;

public sealed class CorrectionModelV0Tests
{
    [Fact]
    public void Build_FlattensEnabledAssistStorageIntoSemanticState()
    {
        var voice =
            Voice(
                "A<w0>BC",
                "エービーシー");

        var primary =
            new PronunciationAssistEffect
            {
                IsEnabled = true,
                Prosody =
                    ProsodyGesture.LightRise,
                HelperRulesJson =
                    HelperRuleCodec.Encode(
                        new HelperRuleSet(
                            HelperRuleSet.CurrentVersion,
                            [
                                HelperRuleFactory.Create(
                                    "ABC",
                                    2,
                                    "ウ",
                                    HelperMoraKind.ZeroVowel,
                                    contextLength: 1),
                            ])),
            };

        var secondary =
            new PronunciationAssistEffect
            {
                IsEnabled = true,
                Prosody =
                    ProsodyGesture.None,
                HelperRulesJson =
                    HelperRuleCodec.Encode(
                        new HelperRuleSet(
                            HelperRuleSet.CurrentVersion,
                            [
                                HelperRuleFactory.Create(
                                    "ABC",
                                    3,
                                    "セ",
                                    HelperMoraKind.ZeroConsonant,
                                    contextLength: 1),
                            ])),
            };

        var disabled =
            new PronunciationAssistEffect
            {
                IsEnabled = false,
                Prosody =
                    ProsodyGesture.Hold,
                HelperRulesJson =
                    HelperRuleCodec.Encode(
                        new HelperRuleSet(
                            HelperRuleSet.CurrentVersion,
                            [
                                HelperRuleFactory.Create(
                                    "ABC",
                                    0,
                                    "オ",
                                    HelperMoraKind.ZeroVowel,
                                    contextLength: 1),
                            ])),
            };

        Add(
            voice,
            secondary);

        Add(
            voice,
            disabled);

        Add(
            voice,
            primary);

        var result =
            CorrectionModelV0.Build(
                voice);

        Assert.True(
            result.IsSuccess,
            result.Message);

        var state =
            Assert.IsType<CorrectionStateV0>(
                result.State);

        Assert.Equal(
            "ABC",
            state.CleanText);

        Assert.Equal(
            "エービーシー",
            state.Hatsuon);

        Assert.Equal(
            [1],
            state.ZeroWaitBoundaries);

        Assert.Equal(
            ProsodyGesture.LightRise,
            state.Prosody);

        Assert.Collection(
            state.Helpers,
            first =>
            {
                Assert.Equal(
                    2,
                    first.CleanTextPosition);
                Assert.Equal(
                    HelperMoraKind.ZeroVowel,
                    first.Kind);
                Assert.Equal(
                    "ウ",
                    first.Helper);
            },
            second =>
            {
                Assert.Equal(
                    3,
                    second.CleanTextPosition);
                Assert.Equal(
                    HelperMoraKind.ZeroConsonant,
                    second.Kind);
                Assert.Equal(
                    "セ",
                    second.Helper);
            });
    }

    [Fact]
    public void Build_ConflictingEnabledProsodyFailsClosed()
    {
        var voice =
            Voice(
                "ABC",
                "ABC");

        Add(
            voice,
            new PronunciationAssistEffect
            {
                IsEnabled = true,
                Prosody =
                    ProsodyGesture.LightRise,
            });

        Add(
            voice,
            new PronunciationAssistEffect
            {
                IsEnabled = true,
                Prosody =
                    ProsodyGesture.Hold,
            });

        var result =
            CorrectionModelV0.Build(
                voice);

        Assert.Equal(
            CorrectionModelBuildStatus
                .ConflictingProsody,
            result.Status);

        Assert.False(
            result.IsSuccess);
    }

    [Fact]
    public void Build_DuplicateResolvedHelperBoundaryFailsClosed()
    {
        var voice =
            Voice(
                "ABC",
                "ABC");

        foreach (var helper
            in new[] { "ウ", "オ" })
        {
            Add(
                voice,
                new PronunciationAssistEffect
                {
                    IsEnabled = true,
                    HelperRulesJson =
                        HelperRuleCodec.Encode(
                            new HelperRuleSet(
                                HelperRuleSet.CurrentVersion,
                                [
                                    HelperRuleFactory.Create(
                                        "ABC",
                                        1,
                                        helper,
                                        HelperMoraKind.ZeroVowel,
                                        contextLength: 1),
                                ])),
                });
        }

        var result =
            CorrectionModelV0.Build(
                voice);

        Assert.Equal(
            CorrectionModelBuildStatus
                .DuplicateHelperBoundary,
            result.Status);
    }

    [Fact]
    public void Build_HelperBoundaryCollisionFailsClosed()
    {
        var voice =
            Voice(
                "A<w0>BC",
                "ABC");

        Add(
            voice,
            new PronunciationAssistEffect
            {
                IsEnabled = true,
                HelperRulesJson =
                    HelperRuleCodec.Encode(
                        new HelperRuleSet(
                            HelperRuleSet.CurrentVersion,
                            [
                                HelperRuleFactory.Create(
                                    "ABC",
                                    1,
                                    "ウ",
                                    HelperMoraKind.ZeroVowel,
                                    contextLength: 1),
                            ])),
            });

        var result =
            CorrectionModelV0.Build(
                voice);

        Assert.Equal(
            CorrectionModelBuildStatus
                .HelperBoundaryCollision,
            result.Status);
    }

    [Fact]
    public void Apply_ComposesReviewOperationsIntoDesiredSemanticState()
    {
        var current =
            new CorrectionStateV0(
                "ABCD",
                "OLD",
                [1],
                [
                    new CorrectionHelperV0(
                        HelperMoraKind.ZeroVowel,
                        "ウ",
                        4),
                ],
                ProsodyGesture.None);

        IReadOnlyList<CorrectionOperation>
            operations =
            [
                new SetReadingCorrection(
                    "NEW"),
                new RemoveBoundaryCorrection(1),
                new AddBoundaryCorrection(2),
                new HelperConsonantZeroCorrection(
                    0,
                    "セ"),
                new SetProsodyGestureCorrection(
                    ProsodyGesture.Hold),
            ];

        var result =
            CorrectionModelV0.Apply(
                current,
                operations);

        Assert.True(
            result.IsSuccess,
            result.Message);

        var state =
            result.State!;

        Assert.Equal(
            "NEW",
            state.Hatsuon);

        Assert.Equal(
            [2],
            state.ZeroWaitBoundaries);

        Assert.Equal(
            ProsodyGesture.Hold,
            state.Prosody);

        Assert.Collection(
            state.Helpers,
            first =>
            {
                Assert.Equal(
                    0,
                    first.CleanTextPosition);
                Assert.Equal(
                    HelperMoraKind.ZeroConsonant,
                    first.Kind);
                Assert.Equal(
                    "セ",
                    first.Helper);
            },
            second =>
            {
                Assert.Equal(
                    4,
                    second.CleanTextPosition);
                Assert.Equal(
                    HelperMoraKind.ZeroVowel,
                    second.Kind);
            });
    }

    [Fact]
    public void Apply_NoChangeReturnsNormalizedEquivalentState()
    {
        var current =
            new CorrectionStateV0(
                "ABC",
                "ABC",
                [2, 1, 2],
                [
                    new CorrectionHelperV0(
                        HelperMoraKind.ZeroVowel,
                        "ウ",
                        3),
                ],
                ProsodyGesture.LightFall);

        var result =
            CorrectionModelV0.Apply(
                current,
                [
                    new NoChangeCorrection(),
                ]);

        Assert.True(
            result.IsSuccess,
            result.Message);

        Assert.Equal(
            [1, 2],
            result.State!
                .ZeroWaitBoundaries);

        Assert.Equal(
            ProsodyGesture.LightFall,
            result.State.Prosody);
    }

    [Fact]
    public void Apply_HelperCannotCollideWithResultBoundary()
    {
        var current =
            new CorrectionStateV0(
                "ABC",
                "ABC",
                [],
                [],
                ProsodyGesture.None);

        var result =
            CorrectionModelV0.Apply(
                current,
                [
                    new AddBoundaryCorrection(1),
                    new HelperVowelZeroCorrection(
                        1,
                        "ウ"),
                ]);

        Assert.Equal(
            CorrectionPatchStatus
                .HelperBoundaryCollision,
            result.Status);
    }

    [Fact]
    public void Apply_BoundaryCannotSplitSurrogatePair()
    {
        var current =
            new CorrectionStateV0(
                "A😀B",
                "A",
                [],
                [],
                ProsodyGesture.None);

        var result =
            CorrectionModelV0.Apply(
                current,
                [
                    new AddBoundaryCorrection(2),
                ]);

        Assert.Equal(
            CorrectionPatchStatus
                .BoundarySplitsSurrogatePair,
            result.Status);
    }

    [Fact]
    public void CreateHelperRuleSet_ReanchorsSemanticHelpers()
    {
        var state =
            new CorrectionStateV0(
                "東京大学",
                "トウキョウダイガク",
                [],
                [
                    new CorrectionHelperV0(
                        HelperMoraKind.ZeroVowel,
                        "ウ",
                        2),
                ],
                ProsodyGesture.None);

        var rules =
            CorrectionModelV0
                .CreateHelperRuleSet(
                    state,
                    contextLength: 2);

        var rule =
            Assert.Single(
                rules.Rules);

        Assert.Equal(
            2,
            rule.Anchor.Position);

        Assert.Equal(
            "東京",
            rule.Anchor.Left);

        Assert.Equal(
            "大学",
            rule.Anchor.Right);
    }

    [Fact]
    public void ReviewOperations_AreSharedCorrectionOperations()
    {
        CorrectionOperation operation =
            new SetReadingCorrection(
                "READING");

        Assert.IsAssignableFrom<
            ReviewCorrectionOperation>(
                operation);
    }

    static VoiceItem Voice(
        string serif,
        string hatsuon)
    {
        var voice =
            new VoiceItem
            {
                Serif = serif,
                Hatsuon = hatsuon,
                CharacterName = "小夜",
            };

        voice.Serif = serif;
        voice.Hatsuon = hatsuon;
        voice.CharacterName = "小夜";

        return voice;
    }

    static void Add(
        VoiceItem voice,
        PronunciationAssistEffect effect)
    {
        Assert.True(
            ReviewAssistEffectCollection.TryAdd(
                voice,
                effect,
                out var error),
            error);
    }
}
