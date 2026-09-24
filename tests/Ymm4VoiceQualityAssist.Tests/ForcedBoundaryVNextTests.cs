using System.Reflection;
using Ymm4VoiceQualityAssist.Core;
using YukkuriMovieMaker.Plugin.Voice;
using YukkuriMovieMaker.Voice;

namespace Ymm4VoiceQualityAssist.Tests;

public sealed class ForcedBoundaryVNextTests
{
    [Fact]
    public async Task Planner_InjectsCommaAtSameSpeakerReadingBoundary()
    {
        var speaker = FakeSpeaker(new Dictionary<string, string>
        {
            ["東京大学"] = "トウキョウダイガク",
            ["東京"] = "トウキョウ",
        });
        var parameter = FakeParameter();

        var result = await ForcedBoundaryReadingPlanner.ResolveAsync(
            speaker,
            parameter,
            new BoundaryMarkerParseResult("東京大学", [2]),
            "トウキョウダイガク");

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(
            "トウキョウ、ダイガク",
            result.Plan!.TransientReading);
        var boundary = Assert.Single(result.Plan.Boundaries);
        Assert.Equal(2, boundary.MarkerPosition);
        Assert.Equal("トウキョウ", boundary.PrefixReading);
    }

    [Fact]
    public async Task Planner_PreservesSourceCommaAndAddsOnlyTransientComma()
    {
        var speaker = FakeSpeaker(new Dictionary<string, string>
        {
            ["これは、テスト"] = "コレハ、テスト",
            ["これは、テ"] = "コレハ、テ",
        });
        var parameter = FakeParameter();

        var result = await ForcedBoundaryReadingPlanner.ResolveAsync(
            speaker,
            parameter,
            new BoundaryMarkerParseResult("これは、テスト", [5]),
            "コレハ、テスト");

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal("コレハ、テ、スト", result.Plan!.TransientReading);
        Assert.Equal(2, result.Plan.TransientReading.Count(x => x == '、'));
        Assert.Equal(1, result.Plan.BaseReading.Count(x => x == '、'));
    }

    [Fact]
    public async Task Planner_AmbiguousIgnoredPunctuationBoundary_FailsClosed()
    {
        var speaker = FakeSpeaker(new Dictionary<string, string>
        {
            ["曖昧"] = "ア、イ",
            ["曖"] = "ア",
        });
        var parameter = FakeParameter();

        var result = await ForcedBoundaryReadingPlanner.ResolveAsync(
            speaker,
            parameter,
            new BoundaryMarkerParseResult("曖昧", [1]),
            "ア、イ");

        Assert.False(result.IsSuccess);
        Assert.Equal(
            BoundaryResolutionStatus.NoUniquePhraseBoundary,
            result.Status);
    }

    [Fact]
    public void ResolverAndMutator_ZeroInjectedPauseOnly()
    {
        var source = Phrase(0, 0.25, "コ", "レ", "ハ");
        var injected = Phrase(1, 0.30, "テ", "ス", "ト");
        var tail = Phrase(2, null, "オ", "ン", "セ", "イ");

        var projection = Projection(source, injected, tail);
        var plan = new ForcedBoundaryAnalysisPlan(
            "これは、テストオンセイ",
            "コレハ、テストオンセイ",
            "コレハ、テストオンセイ",
            "コレハ、テスト、オンセイ",
            [
                new ResolvedForcedBoundaryInsertion(
                    7,
                    7,
                    7,
                    "コレハ、テスト"),
            ]);

        var resolution = InjectedPauseResolver.Resolve(
            plan,
            projection);

        Assert.True(resolution.IsSuccess, resolution.Message);
        Assert.Equal(
            1,
            Assert.Single(resolution.Boundaries).PhraseIndex);

        var mutation = ForcedBoundaryMutator.Apply(
            projection,
            resolution);

        Assert.True(mutation.Applied, mutation.Error);
        Assert.Equal(0.25, source.PauseMora!.VowelLength);
        Assert.Equal(0.0, injected.PauseMora!.VowelLength);
    }

    [Fact]
    public void Resolver_AmbiguousInjectedPrefix_FailsWithoutMutation()
    {
        var first = Phrase(0, 0.25, "ア");
        var punctuationOnly = Phrase(1, 0.30, "、");
        var tail = Phrase(2, null, "イ");

        var projection = Projection(
            first,
            punctuationOnly,
            tail);

        var plan = new ForcedBoundaryAnalysisPlan(
            "曖昧",
            "アイ",
            "アイ",
            "ア、イ",
            [
                new ResolvedForcedBoundaryInsertion(
                    1,
                    1,
                    1,
                    "ア"),
            ]);

        var resolution = InjectedPauseResolver.Resolve(
            plan,
            projection);

        Assert.False(resolution.IsSuccess);
        Assert.Equal(
            BoundaryResolutionStatus.NoUniquePhraseBoundary,
            resolution.Status);
        Assert.Equal(0.25, first.PauseMora!.VowelLength);
        Assert.Equal(0.30, punctuationOnly.PauseMora!.VowelLength);
    }

    [Fact]
    public async Task Planner_MultipleMarkers_InjectDistinctCommas()
    {
        var speaker = FakeSpeaker(new Dictionary<string, string>
        {
            ["東京大学院"] = "トウキョウダイガクイン",
            ["東京"] = "トウキョウ",
            ["東京大学"] = "トウキョウダイガク",
        });

        var result = await ForcedBoundaryReadingPlanner.ResolveAsync(
            speaker,
            FakeParameter(),
            new BoundaryMarkerParseResult("東京大学院", [2, 4]),
            "トウキョウダイガクイン");

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(
            "トウキョウ、ダイガク、イン",
            result.Plan!.TransientReading);
        Assert.Equal(
            [2, 4],
            result.Plan.Boundaries
                .Select(x => x.MarkerPosition)
                .ToArray());
        Assert.Equal(
            2,
            result.Plan.TransientReading.Count(x => x == '、'));
    }

    [Fact]
    public async Task Planner_ExistingCommaAfterInjectedBoundary_IsPreserved()
    {
        var speaker = FakeSpeaker(new Dictionary<string, string>
        {
            ["テスト、終わり"] = "テスト、オワリ",
            ["テ"] = "テ",
        });

        var result = await ForcedBoundaryReadingPlanner.ResolveAsync(
            speaker,
            FakeParameter(),
            new BoundaryMarkerParseResult("テスト、終わり", [1]),
            "テスト、オワリ");

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(
            "テ、スト、オワリ",
            result.Plan!.TransientReading);
        Assert.Equal(
            "テスト、オワリ",
            result.Plan.BaseReading);
    }

    [Fact]
    public async Task Planner_DoesNotSplitSurrogatePairBoundary()
    {
        var speaker = FakeSpeaker(new Dictionary<string, string>
        {
            ["😀あ"] = "😀ア",
            ["😀"] = "😀",
        });

        var result = await ForcedBoundaryReadingPlanner.ResolveAsync(
            speaker,
            FakeParameter(),
            new BoundaryMarkerParseResult("😀あ", [2]),
            "😀ア");

        Assert.True(result.IsSuccess, result.Message);
        var boundary = Assert.Single(result.Plan!.Boundaries);
        Assert.Equal(2, boundary.HatsuonBoundary);
        Assert.Equal("😀、ア", result.Plan.TransientReading);
    }

    [Fact]
    public async Task Planner_HelperAtSameReadingBoundary_FailsClosed()
    {
        var speaker = FakeSpeaker(new Dictionary<string, string>
        {
            ["東京大学"] = "トウキョウダイガク",
            ["東京"] = "トウキョウ",
        });

        var rule = new HelperMoraRule(
            HelperMoraKind.ZeroVowel,
            "ウ",
            new HelperAnchor(2, "東京", "大学"));

        var helperPlan = new HelperReadingPlan(
            "東京大学",
            "トウキョウダイガク",
            "トウキョウウダイガク",
            [
                new ResolvedHelperInsertion(
                    0,
                    rule,
                    2,
                    5,
                    "トウキョウ",
                    "トウキョウウ"),
            ]);

        var result = await ForcedBoundaryReadingPlanner.ResolveAsync(
            speaker,
            FakeParameter(),
            new BoundaryMarkerParseResult("東京大学", [2]),
            "トウキョウダイガク",
            helperPlan);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            BoundaryResolutionStatus.HelperBoundaryConflict,
            result.Status);
    }

    [Fact]
    public void ForcedBoundaryAndProsody_CoexistOnSameProjection()
    {
        var first = Phrase(0, 0.25, "ア", "イ");
        var tail = Phrase(1, null, "ウ");
        first.Moras[0].Pitch = 5.0;
        first.Moras[1].Pitch = 5.2;
        tail.Moras[0].Pitch = 5.0;

        var projection = Projection(first, tail);
        var plan = new ForcedBoundaryAnalysisPlan(
            "あいう",
            "アイウ",
            "アイウ",
            "アイ、ウ",
            [
                new ResolvedForcedBoundaryInsertion(
                    2,
                    2,
                    2,
                    "アイ"),
            ]);

        var resolution = InjectedPauseResolver.Resolve(
            plan,
            projection);
        Assert.True(resolution.IsSuccess, resolution.Message);

        var boundaryMutation = ForcedBoundaryMutator.Apply(
            projection,
            resolution);
        Assert.True(boundaryMutation.Applied, boundaryMutation.Error);

        var prosody = ProsodyMoraMutator.Apply(
            projection,
            ProsodyGesture.Hold);

        Assert.True(prosody.IsSuccess, prosody.Message);
        Assert.Equal(0.0, first.PauseMora!.VowelLength);
        Assert.Equal(3, prosody.MutatedMoraCount);
        Assert.NotEqual(5.2, first.Moras[1].Pitch);
    }

    static VOICEVOXAccentPhrase Phrase(
        int index,
        double? pause,
        params string[] moraTexts)
    {
        var phrase = new VOICEVOXAccentPhrase
        {
            Accent = Math.Max(1, Math.Min(index + 1, moraTexts.Length)),
        };

        foreach (var text in moraTexts)
        {
            phrase.Moras.Add(new VOICEVOXMora
            {
                Text = text,
                Vowel = "a",
                VowelLength = 0.12,
                Pitch = 5.0,
            });
        }

        if (pause is not null)
        {
            phrase.PauseMora = new VOICEVOXMora
            {
                Text = "、",
                Vowel = "pau",
                VowelLength = pause.Value,
                Pitch = 0.0,
            };
        }

        return phrase;
    }

    static VoiceVoxPronounceProjection Projection(
        params VOICEVOXAccentPhrase[] phrases) =>
        new(
            phrases.Select((x, index) =>
                new PhraseReadingProjection(
                    index,
                    x.Moras.Select(m => m.Text ?? string.Empty).ToArray(),
                    x.PauseMora is not null))
                .ToArray(),
            phrases);

    static IVoiceSpeaker FakeSpeaker(
        IReadOnlyDictionary<string, string> readings)
    {
        var speaker =
            DispatchProxy.Create<IVoiceSpeaker, SpeakerProxy>();
        ((SpeakerProxy)(object)speaker).Readings = readings;
        return speaker;
    }

    static IVoiceParameter FakeParameter() =>
        DispatchProxy.Create<IVoiceParameter, ParameterProxy>();

    public class SpeakerProxy : DispatchProxy
    {
        public IReadOnlyDictionary<string, string> Readings { get; set; } =
            new Dictionary<string, string>();

        protected override object? Invoke(
            MethodInfo? targetMethod,
            object?[]? args)
        {
            if (targetMethod?.Name == "ConvertKanjiToYomiAsync"
                && args is { Length: >= 1 }
                && args[0] is string text)
            {
                if (!Readings.TryGetValue(text, out var reading))
                    throw new InvalidOperationException(
                        "No fake reading for: " + text);
                return Task.FromResult(reading);
            }

            throw new NotSupportedException(
                targetMethod?.Name ?? "<null>");
        }
    }

    public class ParameterProxy : DispatchProxy
    {
        protected override object? Invoke(
            MethodInfo? targetMethod,
            object?[]? args) =>
            targetMethod?.ReturnType.IsValueType == true
                ? Activator.CreateInstance(targetMethod.ReturnType)
                : null;
    }
}
