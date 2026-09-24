using System.Collections;
using System.Reflection;
using Ymm4VoiceQualityAssist.Core;
using Ymm4VoiceQualityAssist.Effects;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4VoiceQualityAssist.Tests;

public sealed class ReviewExportTests
{
    [Fact]
    public void Builder_OrdersByFrameLayerThenInputAndBuildsContext()
    {
        var later = Voice(
            frame: 300,
            layer: 2,
            serif: "後",
            hatsuon: "ゴ",
            character: "小夜");

        var sameFrameLaterLayer = Voice(
            frame: 100,
            layer: 4,
            serif: "中<w0>央",
            hatsuon: "チュウオウ",
            character: "ミコ");

        var sameFrameEarlierLayer = Voice(
            frame: 100,
            layer: 1,
            serif: "前",
            hatsuon: "マエ",
            character: "小夜");

        var result = ReviewExportBuilder.Build(
            [
                later,
                sameFrameLaterLayer,
                sameFrameEarlierLayer,
            ],
            "session-001",
            new DateTimeOffset(
                2026,
                9,
                24,
                18,
                0,
                0,
                TimeSpan.FromHours(9)));

        Assert.True(
            result.IsSuccess,
            result.Message);

        var package =
            Assert.IsType<ReviewExportPackage>(
                result.Package);

        Assert.Equal(
            ReviewExportBuilder.Schema,
            package.Schema);

        Assert.Equal(
            "session-001",
            package.ExportSessionId);

        Assert.Equal(
            new DateTimeOffset(
                2026,
                9,
                24,
                9,
                0,
                0,
                TimeSpan.Zero),
            package.ExportedAt);

        Assert.Collection(
            package.Voices,
            first =>
            {
                Assert.Equal(
                    "voice-000000",
                    first.Target.ExportRef);
                Assert.Equal(
                    0,
                    first.Target.ExportIndex);
                Assert.Equal(
                    100,
                    first.Target.Frame);
                Assert.Equal(
                    1,
                    first.Target.Layer);
                Assert.Equal(
                    "前",
                    first.Serif);
                Assert.Null(
                    first.Context.PreviousSerif);
                Assert.Equal(
                    "中<w0>央",
                    first.Context.NextSerif);
            },
            second =>
            {
                Assert.Equal(
                    "voice-000001",
                    second.Target.ExportRef);
                Assert.Equal(
                    "中<w0>央",
                    second.Serif);
                Assert.Equal(
                    "前",
                    second.Context.PreviousSerif);
                Assert.Equal(
                    "後",
                    second.Context.NextSerif);
                Assert.Equal(
                    "中央",
                    second.Controls.CleanText);

                var boundary =
                    Assert.Single(
                        second.Controls.Boundaries);

                Assert.Equal(
                    1,
                    boundary.Position);
                Assert.Equal(
                    "w0",
                    boundary.Source);
            },
            third =>
            {
                Assert.Equal(
                    "voice-000002",
                    third.Target.ExportRef);
                Assert.Equal(
                    300,
                    third.Target.Frame);
                Assert.Equal(
                    2,
                    third.Target.Layer);
                Assert.Equal(
                    "後",
                    third.Serif);
                Assert.Equal(
                    "中<w0>央",
                    third.Context.PreviousSerif);
                Assert.Null(
                    third.Context.NextSerif);
            });
    }

    [Fact]
    public void Builder_PreservesLiveSameSessionTargetMap()
    {
        var later = Voice(
            200,
            2,
            "B",
            "ビー",
            "ミコ");

        var earlier = Voice(
            100,
            1,
            "A",
            "エー",
            "小夜");

        var result =
            ReviewExportBuilder.Build(
                [later, earlier],
                "session-map",
                DateTimeOffset.UnixEpoch);

        Assert.True(
            result.IsSuccess,
            result.Message);

        var session =
            Assert.IsType<ReviewExportSession>(
                result.Session);

        Assert.Equal(
            "session-map",
            session.Package.ExportSessionId);

        Assert.Same(
            earlier,
            session.LiveTargets[
                "voice-000000"]);

        Assert.Same(
            later,
            session.LiveTargets[
                "voice-000001"]);
    }

    [Fact]
    public void Builder_TieUsesOriginalInputOrder()
    {
        var a = Voice(
            10,
            2,
            "A",
            "エー",
            "小夜");

        var b = Voice(
            10,
            2,
            "B",
            "ビー",
            "ミコ");

        var result =
            ReviewExportBuilder.Build(
                [b, a],
                "session",
                DateTimeOffset.UnixEpoch);

        Assert.True(
            result.IsSuccess,
            result.Message);

        Assert.Equal(
            ["B", "A"],
            result.Package!.Voices
                .Select(x => x.Serif)
                .ToArray());
    }

    [Fact]
    public void Builder_MissingGeneratedPronounceKeepsOptionalSummaryEmpty()
    {
        var voice = Voice(
            10,
            1,
            "本文",
            "ホンブン",
            "小夜");

        Assert.Null(
            voice.Pronounce);

        var result =
            ReviewExportBuilder.Build(
                [voice],
                "session",
                DateTimeOffset.UnixEpoch);

        Assert.True(
            result.IsSuccess,
            result.Message);

        var summary =
            Assert.Single(
                result.Package!.Voices)
                .Pronunciation;

        Assert.False(
            summary.HasGeneratedPronounce);

        Assert.Null(
            summary.GeneratedMoraReading);

        Assert.Null(
            summary.AccentPhraseCount);
    }

    [Fact]
    public void Builder_UsesExactlyTheB0Fingerprint()
    {
        var voice = Voice(
            50,
            3,
            "東京<w0>大学",
            "トウキョウダイガク",
            "小夜");

        Assert.True(
            SourceFingerprint.TryCreate(
                voice,
                out var expected,
                out var fingerprintError),
            fingerprintError);

        var result =
            ReviewExportBuilder.Build(
                [voice],
                "session",
                DateTimeOffset.UnixEpoch);

        Assert.True(
            result.IsSuccess,
            result.Message);

        Assert.Equal(
            expected!.Fingerprint,
            Assert.Single(
                result.Package!.Voices)
                .SourceFingerprint);
    }

    [Fact]
    public void MovingVoiceChangesLocatorButNotFingerprint()
    {
        var first = Voice(
            10,
            1,
            "同じ声",
            "オナジコエ",
            "小夜");

        var moved = Voice(
            900,
            8,
            "同じ声",
            "オナジコエ",
            "小夜");

        var a =
            ReviewExportBuilder.Build(
                [first],
                "session-a",
                DateTimeOffset.UnixEpoch);

        var b =
            ReviewExportBuilder.Build(
                [moved],
                "session-b",
                DateTimeOffset.UnixEpoch);

        Assert.True(a.IsSuccess, a.Message);
        Assert.True(b.IsSuccess, b.Message);

        var recordA =
            Assert.Single(a.Package!.Voices);
        var recordB =
            Assert.Single(b.Package!.Voices);

        Assert.NotEqual(
            recordA.Target.Frame,
            recordB.Target.Frame);

        Assert.NotEqual(
            recordA.Target.Layer,
            recordB.Target.Layer);

        Assert.Equal(
            recordA.SourceFingerprint,
            recordB.SourceFingerprint);
    }

    [Fact]
    public void EnabledAssistSettingsAreExportedCanonically()
    {
        var voice = Voice(
            10,
            1,
            "ABCDE",
            "エービーシーディーイー",
            "小夜");

        var rules =
            new HelperRuleSet(
                HelperRuleSet.CurrentVersion,
                [
                    HelperRuleFactory.Create(
                        "ABCDE",
                        2,
                        "ウ",
                        HelperMoraKind.ZeroVowel,
                        contextLength: 2),
                ]);

        AppendEffect(
            voice,
            new PronunciationAssistEffect
            {
                IsEnabled = true,
                Prosody =
                    ProsodyGesture.LightFall,
                HelperRulesJson =
                    HelperRuleCodec.Encode(
                        rules),
            });

        AppendEffect(
            voice,
            new PronunciationAssistEffect
            {
                IsEnabled = false,
                Prosody =
                    ProsodyGesture.Hold,
            });

        var result =
            ReviewExportBuilder.Build(
                [voice],
                "session",
                DateTimeOffset.UnixEpoch);

        Assert.True(
            result.IsSuccess,
            result.Message);

        var assist =
            Assert.Single(
                result.Package!.Voices)
                .Assist;

        Assert.True(
            assist.Enabled);

        var profile =
            Assert.Single(
                assist.Profiles);

        Assert.Equal(
            ProsodyGesture.LightFall,
            profile.Prosody);

        var helper =
            Assert.Single(
                profile.HelperRules);

        Assert.Equal(
            HelperMoraKind.ZeroVowel,
            helper.Kind);

        Assert.Equal(
            "ウ",
            helper.Helper);

        Assert.Equal(
            2,
            helper.Position);
    }

    [Fact]
    public void MalformedEnabledAssistFailsWholeExport()
    {
        var voice = Voice(
            10,
            1,
            "ABC",
            "エービーシー",
            "小夜");

        AppendEffect(
            voice,
            new PronunciationAssistEffect
            {
                IsEnabled = true,
                HelperRulesJson =
                    "{broken",
            });

        var result =
            ReviewExportBuilder.Build(
                [voice],
                "session",
                DateTimeOffset.UnixEpoch);

        Assert.False(
            result.IsSuccess);

        Assert.Equal(
            ReviewExportBuildStatus
                .InvalidVoiceSource,
            result.Status);

        Assert.Equal(
            0,
            result.FailedExportIndex);

        Assert.Null(
            result.Package);
    }

    [Fact]
    public void EmptySessionIdFailsClosed()
    {
        var result =
            ReviewExportBuilder.Build(
                [],
                " ",
                DateTimeOffset.UnixEpoch);

        Assert.Equal(
            ReviewExportBuildStatus
                .InvalidSessionId,
            result.Status);

        Assert.False(
            result.IsSuccess);
    }

    [Fact]
    public void JsonCodec_RoundTripsPackageAndUsesWireEnumNames()
    {
        var voice = Voice(
            10,
            1,
            "ABC",
            "エービーシー",
            "小夜");

        AppendEffect(
            voice,
            new PronunciationAssistEffect
            {
                IsEnabled = true,
                Prosody =
                    ProsodyGesture.LightRise,
            });

        var built =
            ReviewExportBuilder.Build(
                [voice],
                "session-json",
                new DateTimeOffset(
                    2026,
                    9,
                    24,
                    0,
                    0,
                    0,
                    TimeSpan.Zero));

        Assert.True(
            built.IsSuccess,
            built.Message);

        var json =
            ReviewExportJson.Serialize(
                built.Package!);

        Assert.Contains(
            "\"schema\": \"ymm4.voice-review.v0\"",
            json,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"prosody\": \"lightRise\"",
            json,
            StringComparison.Ordinal);

        Assert.True(
            ReviewExportJson.TryDeserialize(
                json,
                out var decoded,
                out var error),
            error);

        Assert.NotNull(decoded);
        Assert.Equal(
            "session-json",
            decoded!.ExportSessionId);

        var record =
            Assert.Single(
                decoded.Voices);

        Assert.Equal(
            "ABC",
            record.Serif);

        Assert.Equal(
            ProsodyGesture.LightRise,
            Assert.Single(
                record.Assist.Profiles)
                .Prosody);
    }

    static VoiceItem Voice(
        int frame,
        int layer,
        string serif,
        string hatsuon,
        string character)
    {
        var voice =
            new VoiceItem
            {
                Frame = frame,
                Layer = layer,
                Serif = serif,
                Hatsuon = hatsuon,
                CharacterName = character,
            };

        voice.Frame = frame;
        voice.Layer = layer;
        voice.Serif = serif;
        voice.Hatsuon = hatsuon;
        voice.CharacterName = character;

        return voice;
    }

    static void AppendEffect(
        VoiceItem voice,
        IVideoEffect effect)
    {
        var property =
            typeof(VoiceItem).GetProperty(
                nameof(
                    VoiceItem.JimakuVideoEffects),
                BindingFlags.Instance
                | BindingFlags.Public)
            ?? throw new MissingMemberException(
                typeof(VoiceItem).FullName,
                nameof(
                    VoiceItem.JimakuVideoEffects));

        var current =
            property.GetValue(voice)
            ?? throw new InvalidOperationException(
                "JimakuVideoEffects returned null.");

        if (current is IList list
            && !list.IsReadOnly
            && !list.IsFixedSize)
        {
            list.Add(effect);
            return;
        }

        var add = current.GetType()
            .GetMethods(
                BindingFlags.Instance
                | BindingFlags.Public)
            .FirstOrDefault(x =>
                x.Name == "Add"
                && x.GetParameters().Length == 1
                && (
                    x.GetParameters()[0]
                        .ParameterType
                        .IsAssignableFrom(
                            effect.GetType())
                    || x.GetParameters()[0]
                        .ParameterType
                        .IsAssignableFrom(
                            typeof(IVideoEffect))));

        if (add is null)
        {
            throw new InvalidOperationException(
                "JimakuVideoEffects has no supported Add route.");
        }

        var updated =
            add.Invoke(
                current,
                [effect]);

        if (updated is null
            || ReferenceEquals(
                updated,
                current))
        {
            return;
        }

        if (property.SetMethod?.IsPublic
            != true)
        {
            throw new InvalidOperationException(
                "JimakuVideoEffects is immutable without a public setter.");
        }

        property.SetValue(
            voice,
            updated);
    }
}
