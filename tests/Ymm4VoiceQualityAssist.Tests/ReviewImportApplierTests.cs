using Ymm4VoiceQualityAssist.Core;
using Ymm4VoiceQualityAssist.Effects;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4VoiceQualityAssist.Tests;

public sealed class ReviewImportApplierTests
{
    [Fact]
    public void CommitUndoRedo_ReadingAndBoundaryRoundTrip()
    {
        var voice =
            Voice(
                100,
                1,
                "ABC",
                "エービーシー");

        var context =
            Context(
                [voice],
                records =>
                [
                    new ReviewCorrectionWireRecord(
                        records[0].Target.ExportRef,
                        records[0].SourceFingerprint,
                        [
                            new ReviewCorrectionWireOperation(
                                "setReading",
                                Reading: "エービーシーディー"),
                            new ReviewCorrectionWireOperation(
                                "addBoundary",
                                Position: 1),
                        ]),
                ]);

        var exportRef =
            context.Package.Voices[0]
                .Target.ExportRef;

        var prepared =
            ReviewImportApplier.Prepare(
                context.Plan,
                [exportRef]);

        Assert.True(
            prepared.IsSuccess,
            prepared.Message);

        var journal =
            prepared.Journal!;

        var committed =
            journal.Commit();

        Assert.True(
            committed.IsSuccess,
            committed.Message);

        Assert.Equal(
            "A<w0>BC",
            voice.Serif);

        Assert.Equal(
            "エービーシーディー",
            voice.Hatsuon);

        journal.UndoOrThrow();

        Assert.Equal(
            "ABC",
            voice.Serif);

        Assert.Equal(
            "エービーシー",
            voice.Hatsuon);

        journal.RedoOrThrow();

        Assert.Equal(
            "A<w0>BC",
            voice.Serif);

        Assert.Equal(
            "エービーシーディー",
            voice.Hatsuon);
    }

    [Fact]
    public void HelperAndProsody_CreateAssistEffectAndUndoMembership()
    {
        var voice =
            Voice(
                100,
                1,
                "ABC",
                "エービーシー");

        var context =
            Context(
                [voice],
                records =>
                [
                    new ReviewCorrectionWireRecord(
                        records[0].Target.ExportRef,
                        records[0].SourceFingerprint,
                        [
                            new ReviewCorrectionWireOperation(
                                "helperVowelZero",
                                Position: 1,
                                Helper: "ウ"),
                            new ReviewCorrectionWireOperation(
                                "setProsodyGesture",
                                Gesture: "hold"),
                        ]),
                ]);

        var exportRef =
            context.Package.Voices[0]
                .Target.ExportRef;

        var prepared =
            ReviewImportApplier.Prepare(
                context.Plan,
                [exportRef]);

        Assert.True(
            prepared.IsSuccess,
            prepared.Message);

        var journal =
            prepared.Journal!;

        Assert.True(
            journal.Commit().IsSuccess);

        var effect =
            Assert.Single(
                ReviewAssistEffectCollection
                    .Enumerate(voice));

        Assert.IsType<PronunciationAssistAudioEffect>(
            effect);

        Assert.Empty(
            PronunciationAssistSettingsStore
                .EnumerateLegacy(voice));

        Assert.True(
            effect.IsEnabled);

        Assert.Equal(
            ProsodyGesture.Hold,
            effect.Prosody);

        Assert.True(
            HelperRuleCodec.TryDecode(
                effect.HelperRulesJson,
                out var rules,
                out var decodeError),
            decodeError);

        var rule =
            Assert.Single(
                rules.Rules);

        Assert.Equal(
            HelperMoraKind.ZeroVowel,
            rule.Kind);

        Assert.Equal(
            "ウ",
            rule.Helper);

        Assert.Equal(
            1,
            rule.Anchor.Position);

        journal.UndoOrThrow();

        Assert.Empty(
            ReviewAssistEffectCollection
                .Enumerate(voice));

        journal.RedoOrThrow();

        var redone =
            Assert.Single(
                ReviewAssistEffectCollection
                    .Enumerate(voice));

        Assert.Same(
            effect,
            redone);

        Assert.Equal(
            ProsodyGesture.Hold,
            redone.Prosody);
    }

    [Fact]
    public void DisabledExistingAssistEffect_IsNotReusedOrEnabled()
    {
        var voice =
            Voice(
                100,
                1,
                "ABC",
                "エービーシー");

        var hidden =
            new PronunciationAssistEffect
            {
                IsEnabled = false,
                Prosody =
                    ProsodyGesture.LightFall,
                HelperRulesJson =
                    HelperRuleCodec.Encode(
                        new HelperRuleSet(
                            HelperRuleSet.CurrentVersion,
                            [
                                HelperRuleFactory.Create(
                                    "ABC",
                                    2,
                                    "セ",
                                    HelperMoraKind.ZeroConsonant,
                                    contextLength: 1),
                            ])),
            };

        Assert.True(
            ReviewAssistEffectCollection.TryAdd(
                voice,
                hidden,
                out var addError),
            addError);

        var context =
            Context(
                [voice],
                records =>
                [
                    new ReviewCorrectionWireRecord(
                        records[0].Target.ExportRef,
                        records[0].SourceFingerprint,
                        [
                            new ReviewCorrectionWireOperation(
                                "helperVowelZero",
                                Position: 1,
                                Helper: "ウ"),
                        ]),
                ]);

        var prepared =
            ReviewImportApplier.Prepare(
                context.Plan,
                [
                    context.Package.Voices[0]
                        .Target.ExportRef,
                ]);

        Assert.True(
            prepared.IsSuccess,
            prepared.Message);

        Assert.True(
            prepared.Journal!.Commit().IsSuccess);

        var effects =
            ReviewAssistEffectCollection
                .Enumerate(voice);

        Assert.Equal(
            2,
            effects.Count);

        Assert.False(
            hidden.IsEnabled);

        Assert.Equal(
            ProsodyGesture.LightFall,
            hidden.Prosody);

        var active =
            Assert.Single(
                effects.Where(x =>
                    x.IsEnabled));

        Assert.NotSame(
            hidden,
            active);
    }

    [Fact]
    public void ProsodyProposal_MakesOneEffectiveGesture()
    {
        var voice =
            Voice(
                100,
                1,
                "ABC",
                "エービーシー");

        var first =
            new PronunciationAssistEffect
            {
                IsEnabled = true,
                Prosody =
                    ProsodyGesture.LightRise,
            };

        var second =
            new PronunciationAssistEffect
            {
                IsEnabled = true,
                Prosody =
                    ProsodyGesture.LightFall,
            };

        Assert.True(
            ReviewAssistEffectCollection.TryAdd(
                voice,
                first,
                out var firstError),
            firstError);

        Assert.True(
            ReviewAssistEffectCollection.TryAdd(
                voice,
                second,
                out var secondError),
            secondError);

        var context =
            Context(
                [voice],
                records =>
                [
                    new ReviewCorrectionWireRecord(
                        records[0].Target.ExportRef,
                        records[0].SourceFingerprint,
                        [
                            new ReviewCorrectionWireOperation(
                                "setProsodyGesture",
                                Gesture: "hold"),
                        ]),
                ]);

        var prepared =
            ReviewImportApplier.Prepare(
                context.Plan,
                [
                    context.Package.Voices[0]
                        .Target.ExportRef,
                ]);

        Assert.True(
            prepared.IsSuccess,
            prepared.Message);

        Assert.True(
            prepared.Journal!.Commit().IsSuccess);

        Assert.Equal(
            ProsodyGesture.Hold,
            first.Prosody);

        Assert.Equal(
            ProsodyGesture.None,
            second.Prosody);

        prepared.Journal.UndoOrThrow();

        Assert.Equal(
            ProsodyGesture.LightRise,
            first.Prosody);

        Assert.Equal(
            ProsodyGesture.LightFall,
            second.Prosody);
    }

    [Fact]
    public void SourceChangeAfterPrepare_BlocksCommitWithoutMutation()
    {
        var voice =
            Voice(
                100,
                1,
                "ABC",
                "エービーシー");

        var context =
            Context(
                [voice],
                records =>
                [
                    new ReviewCorrectionWireRecord(
                        records[0].Target.ExportRef,
                        records[0].SourceFingerprint,
                        [
                            new ReviewCorrectionWireOperation(
                                "setReading",
                                Reading: "NEW"),
                        ]),
                ]);

        var prepared =
            ReviewImportApplier.Prepare(
                context.Plan,
                [
                    context.Package.Voices[0]
                        .Target.ExportRef,
                ]);

        Assert.True(
            prepared.IsSuccess,
            prepared.Message);

        voice.Serif =
            "変更後";

        var commit =
            prepared.Journal!.Commit();

        Assert.Equal(
            ReviewImportCommitStatus.SourceChanged,
            commit.Status);

        Assert.Equal(
            "変更後",
            voice.Serif);

        Assert.Equal(
            "エービーシー",
            voice.Hatsuon);
    }

    [Fact]
    public void UnsafeBoundaryInBatch_FailsPreflightAndMutatesNothing()
    {
        var safe =
            Voice(
                100,
                1,
                "ABC",
                "エービーシー");

        var unsafeVoice =
            Voice(
                200,
                1,
                "A<w100>B",
                "エービー");

        var context =
            Context(
                [safe, unsafeVoice],
                records =>
                [
                    new ReviewCorrectionWireRecord(
                        records[0].Target.ExportRef,
                        records[0].SourceFingerprint,
                        [
                            new ReviewCorrectionWireOperation(
                                "setReading",
                                Reading: "SAFE-NEW"),
                        ]),
                    new ReviewCorrectionWireRecord(
                        records[1].Target.ExportRef,
                        records[1].SourceFingerprint,
                        [
                            new ReviewCorrectionWireOperation(
                                "addBoundary",
                                Position: 1),
                        ]),
                ]);

        var prepared =
            ReviewImportApplier.Prepare(
                context.Plan,
                context.Package.Voices
                    .Select(x =>
                        x.Target.ExportRef)
                    .ToArray());

        Assert.Equal(
            ReviewImportPrepareStatus.PreflightFailed,
            prepared.Status);

        Assert.Null(
            prepared.Journal);

        Assert.Equal(
            "エービーシー",
            safe.Hatsuon);

        Assert.Equal(
            "A<w100>B",
            unsafeVoice.Serif);
    }

    [Fact]
    public void UnselectedProposal_IsNeverApplied()
    {
        var first =
            Voice(
                100,
                1,
                "A",
                "A");

        var second =
            Voice(
                200,
                1,
                "B",
                "B");

        var context =
            Context(
                [first, second],
                records =>
                [
                    new ReviewCorrectionWireRecord(
                        records[0].Target.ExportRef,
                        records[0].SourceFingerprint,
                        [
                            new ReviewCorrectionWireOperation(
                                "setReading",
                                Reading: "A2"),
                        ]),
                    new ReviewCorrectionWireRecord(
                        records[1].Target.ExportRef,
                        records[1].SourceFingerprint,
                        [
                            new ReviewCorrectionWireOperation(
                                "setReading",
                                Reading: "B2"),
                        ]),
                ]);

        var prepared =
            ReviewImportApplier.Prepare(
                context.Plan,
                [
                    context.Package.Voices[1]
                        .Target.ExportRef,
                ]);

        Assert.True(
            prepared.IsSuccess,
            prepared.Message);

        Assert.True(
            prepared.Journal!.Commit().IsSuccess);

        Assert.Equal(
            "A",
            first.Hatsuon);

        Assert.Equal(
            "B2",
            second.Hatsuon);
    }

    [Fact]
    public void HelperCannotCollideWithAfterBoundary()
    {
        var voice =
            Voice(
                100,
                1,
                "ABC",
                "ABC");

        var context =
            Context(
                [voice],
                records =>
                [
                    new ReviewCorrectionWireRecord(
                        records[0].Target.ExportRef,
                        records[0].SourceFingerprint,
                        [
                            new ReviewCorrectionWireOperation(
                                "addBoundary",
                                Position: 1),
                            new ReviewCorrectionWireOperation(
                                "helperVowelZero",
                                Position: 1,
                                Helper: "ウ"),
                        ]),
                ]);

        var prepared =
            ReviewImportApplier.Prepare(
                context.Plan,
                [
                    context.Package.Voices[0]
                        .Target.ExportRef,
                ]);

        Assert.Equal(
            ReviewImportPrepareStatus.PreflightFailed,
            prepared.Status);

        Assert.Equal(
            "ABC",
            voice.Serif);

        Assert.Empty(
            ReviewAssistEffectCollection
                .Enumerate(voice));
    }

    static ImportContext Context(
        IReadOnlyList<VoiceItem> voices,
        Func<
            IReadOnlyList<ReviewVoiceExportRecord>,
            IReadOnlyList<ReviewCorrectionWireRecord>>
            createRecords)
    {
        var exported =
            ReviewExportBuilder.Build(
                voices,
                "session-apply",
                DateTimeOffset.UnixEpoch);

        Assert.True(
            exported.IsSuccess,
            exported.Message);

        var package =
            exported.Session!.Package;

        var wire =
            new ReviewCorrectionWirePackage(
                ReviewCorrectionValidator.Schema,
                package.ExportSessionId,
                createRecords(
                    package.Voices));

        var decoded =
            ReviewCorrectionJson
                .DecodeAndValidateAgainstExport(
                    ReviewCorrectionJson.Serialize(
                        wire),
                    package);

        Assert.True(
            decoded.IsSuccess,
            string.Join(
                Environment.NewLine,
                decoded.Errors.Select(
                    x => x.Code
                        + ": "
                        + x.Message)));

        var plan =
            ReviewImportPlanner.Build(
                package,
                decoded,
                voices,
                exported.Session);

        Assert.True(
            plan.IsSuccess,
            plan.Message);

        return new ImportContext(
            package,
            plan);
    }

    static VoiceItem Voice(
        int frame,
        int layer,
        string serif,
        string hatsuon)
    {
        var voice =
            new VoiceItem
            {
                Frame = frame,
                Layer = layer,
                Serif = serif,
                Hatsuon = hatsuon,
                CharacterName = "小夜",
            };

        voice.Frame = frame;
        voice.Layer = layer;
        voice.Serif = serif;
        voice.Hatsuon = hatsuon;
        voice.CharacterName = "小夜";

        return voice;
    }

    sealed record ImportContext(
        ReviewExportPackage Package,
        ReviewImportPlan Plan);
}
