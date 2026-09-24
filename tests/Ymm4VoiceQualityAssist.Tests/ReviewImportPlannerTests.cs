using Ymm4VoiceQualityAssist.Core;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4VoiceQualityAssist.Tests;

public sealed class ReviewImportPlannerTests
{
    [Fact]
    public void SameSession_LiveObjectWinsAfterTimelineMove()
    {
        var voice = Voice(
            100,
            1,
            "本文",
            "ホンブン",
            "小夜");

        var exported =
            Export(
                [voice]);

        var corrections =
            Corrections(
                exported.Session!.Package,
                [
                    NoChange(
                        exported.Session.Package
                            .Voices[0]),
                ]);

        voice.Frame = 900;
        voice.Layer = 7;

        var plan =
            ReviewImportPlanner.Build(
                exported.Session.Package,
                corrections,
                [voice],
                exported.Session);

        var item =
            Assert.Single(
                plan.Items);

        Assert.Equal(
            ReviewImportResolutionStatus
                .ExactSessionMatch,
            item.ResolutionStatus);

        Assert.Same(
            voice,
            item.Target);

        Assert.True(
            item.CanApply);

        Assert.False(
            item.SelectedByDefault);
    }

    [Fact]
    public void SameSession_SourceChange_IsStale()
    {
        var voice = Voice(
            100,
            1,
            "本文",
            "ホンブン",
            "小夜");

        var exported =
            Export(
                [voice]);

        var corrections =
            Corrections(
                exported.Session!.Package,
                [
                    NoChange(
                        exported.Session.Package
                            .Voices[0]),
                ]);

        voice.Hatsuon =
            "カワッタ";

        var plan =
            ReviewImportPlanner.Build(
                exported.Session.Package,
                corrections,
                [voice],
                exported.Session);

        var item =
            Assert.Single(
                plan.Items);

        Assert.Equal(
            ReviewImportResolutionStatus.Stale,
            item.ResolutionStatus);

        Assert.Same(
            voice,
            item.Target);

        Assert.False(
            item.CanApply);
    }

    [Fact]
    public void CrossSession_UniqueFingerprintMatch_IsExact()
    {
        var oldVoice = Voice(
            100,
            1,
            "本文",
            "ホンブン",
            "小夜");

        var exported =
            Export(
                [oldVoice]);

        var corrections =
            Corrections(
                exported.Session!.Package,
                [
                    NoChange(
                        exported.Session.Package
                            .Voices[0]),
                ]);

        var current = Voice(
            900,
            8,
            "本文",
            "ホンブン",
            "小夜");

        var plan =
            ReviewImportPlanner.Build(
                exported.Session.Package,
                corrections,
                [current]);

        var item =
            Assert.Single(
                plan.Items);

        Assert.Equal(
            ReviewImportResolutionStatus
                .ExactFingerprintMatch,
            item.ResolutionStatus);

        Assert.Same(
            current,
            item.Target);

        Assert.True(
            item.CanApply);
    }

    [Fact]
    public void CrossSession_DuplicateFingerprint_IsAmbiguous()
    {
        var oldVoice = Voice(
            100,
            1,
            "本文",
            "ホンブン",
            "小夜");

        var exported =
            Export(
                [oldVoice]);

        var corrections =
            Corrections(
                exported.Session!.Package,
                [
                    NoChange(
                        exported.Session.Package
                            .Voices[0]),
                ]);

        var a = Voice(
            100,
            1,
            "本文",
            "ホンブン",
            "小夜");

        var b = Voice(
            200,
            2,
            "本文",
            "ホンブン",
            "小夜");

        var plan =
            ReviewImportPlanner.Build(
                exported.Session.Package,
                corrections,
                [a, b]);

        var item =
            Assert.Single(
                plan.Items);

        Assert.Equal(
            ReviewImportResolutionStatus.Ambiguous,
            item.ResolutionStatus);

        Assert.Null(
            item.Target);

        Assert.False(
            item.CanApply);
    }

    [Fact]
    public void CrossSession_UniqueLocatorWithChangedSource_IsStale()
    {
        var oldVoice = Voice(
            100,
            1,
            "本文",
            "ホンブン",
            "小夜");

        var exported =
            Export(
                [oldVoice]);

        var corrections =
            Corrections(
                exported.Session!.Package,
                [
                    NoChange(
                        exported.Session.Package
                            .Voices[0]),
                ]);

        var current = Voice(
            100,
            1,
            "本文",
            "カワッタ",
            "小夜");

        var plan =
            ReviewImportPlanner.Build(
                exported.Session.Package,
                corrections,
                [current]);

        var item =
            Assert.Single(
                plan.Items);

        Assert.Equal(
            ReviewImportResolutionStatus.Stale,
            item.ResolutionStatus);

        Assert.Same(
            current,
            item.Target);

        Assert.False(
            item.CanApply);
    }

    [Fact]
    public void CrossSession_NoFingerprintOrLocatorMatch_IsMissing()
    {
        var oldVoice = Voice(
            100,
            1,
            "本文",
            "ホンブン",
            "小夜");

        var exported =
            Export(
                [oldVoice]);

        var corrections =
            Corrections(
                exported.Session!.Package,
                [
                    NoChange(
                        exported.Session.Package
                            .Voices[0]),
                ]);

        var current = Voice(
            900,
            8,
            "別",
            "ベツ",
            "ミコ");

        var plan =
            ReviewImportPlanner.Build(
                exported.Session.Package,
                corrections,
                [current]);

        var item =
            Assert.Single(
                plan.Items);

        Assert.Equal(
            ReviewImportResolutionStatus.Missing,
            item.ResolutionStatus);

        Assert.Null(
            item.Target);
    }

    [Fact]
    public void Preview_ShowsReadingBoundaryHelperAndProsodyChanges()
    {
        var voice = Voice(
            100,
            1,
            "A<w0>BC",
            "エービーシー",
            "小夜");

        var exported =
            Export(
                [voice]);

        var source =
            exported.Session!.Package
                .Voices[0];

        var corrections =
            Corrections(
                exported.Session.Package,
                [
                    new ReviewCorrectionWireRecord(
                        source.Target.ExportRef,
                        source.SourceFingerprint,
                        [
                            new ReviewCorrectionWireOperation(
                                "setReading",
                                Reading:
                                    "エービーシーディー"),
                            new ReviewCorrectionWireOperation(
                                "removeBoundary",
                                Position: 1),
                            new ReviewCorrectionWireOperation(
                                "addBoundary",
                                Position: 2),
                            new ReviewCorrectionWireOperation(
                                "helperVowelZero",
                                Position: 0,
                                Helper: "ウ"),
                            new ReviewCorrectionWireOperation(
                                "setProsodyGesture",
                                Gesture: "hold"),
                        ]),
                ]);

        var plan =
            ReviewImportPlanner.Build(
                exported.Session.Package,
                corrections,
                [voice],
                exported.Session);

        var item =
            Assert.Single(
                plan.Items);

        var preview =
            Assert.IsType<
                ReviewImportPreview>(
                    item.Preview);

        Assert.Equal(
            "エービーシー",
            preview.BeforeHatsuon);

        Assert.Equal(
            "エービーシーディー",
            preview.AfterHatsuon);

        Assert.Equal(
            [1],
            preview.BeforeBoundaries);

        Assert.Equal(
            [2],
            preview.AfterBoundaries);

        var helper =
            Assert.Single(
                preview.HelperAdditions);

        Assert.Equal(
            HelperMoraKind.ZeroVowel,
            helper.Kind);

        Assert.Equal(
            0,
            helper.CleanTextPosition);

        Assert.Equal(
            "ウ",
            helper.Helper);

        Assert.Equal(
            ProsodyGesture.Hold,
            preview.ProposedProsody);

        Assert.False(
            preview.IsNoChange);

        Assert.True(
            item.SelectedByDefault);
    }

    [Fact]
    public void InvalidB2Package_IsRejectedBeforePlanning()
    {
        var voice = Voice(
            100,
            1,
            "本文",
            "ホンブン",
            "小夜");

        var exported =
            Export(
                [voice]);

        var invalid =
            ReviewCorrectionJson.Decode(
                "{broken");

        var plan =
            ReviewImportPlanner.Build(
                exported.Session!.Package,
                invalid,
                [voice],
                exported.Session);

        Assert.False(
            plan.IsSuccess);

        Assert.Equal(
            ReviewImportBuildStatus
                .InvalidCorrectionPackage,
            plan.Status);

        Assert.Empty(
            plan.Items);
    }

    static ReviewExportBuildResult Export(
        IReadOnlyList<VoiceItem> voices)
    {
        var result =
            ReviewExportBuilder.Build(
                voices,
                "session-import",
                DateTimeOffset.UnixEpoch);

        Assert.True(
            result.IsSuccess,
            result.Message);

        return result;
    }

    static ReviewCorrectionDecodeResult Corrections(
        ReviewExportPackage package,
        IReadOnlyList<
            ReviewCorrectionWireRecord>
            records)
    {
        var json =
            ReviewCorrectionJson.Serialize(
                new ReviewCorrectionWirePackage(
                    ReviewCorrectionValidator.Schema,
                    package.ExportSessionId,
                    records));

        var decoded =
            ReviewCorrectionJson
                .DecodeAndValidateAgainstExport(
                    json,
                    package);

        Assert.True(
            decoded.IsSuccess,
            string.Join(
                Environment.NewLine,
                decoded.Errors.Select(
                    x => x.Code + ": " + x.Message)));

        return decoded;
    }

    static ReviewCorrectionWireRecord NoChange(
        ReviewVoiceExportRecord record) =>
        new(
            record.Target.ExportRef,
            record.SourceFingerprint,
            [
                new ReviewCorrectionWireOperation(
                    "noChange"),
            ]);

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
}
