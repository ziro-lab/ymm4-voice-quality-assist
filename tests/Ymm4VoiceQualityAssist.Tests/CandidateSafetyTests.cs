using System.IO;
using Ymm4VoiceQualityAssist.Core;
using Ymm4VoiceQualityAssist.Effects;
using Ymm4VoiceQualityAssist.Runtime;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4VoiceQualityAssist.Tests;

public sealed class CandidateSafetyTests
{
    static VoiceItem Voice(int frame = 0) => new()
    { Serif = "ABC", Hatsuon = "エービーシー", CharacterName = "小夜", Frame = frame };

    [Fact]
    public void Lease_RejectsEditsEvenWhenTextIsChangedBack()
    {
        var voice = Voice();
        using var lease = new VoiceApplyLease(voice);
        Assert.True(lease.IsCurrent);
        voice.Hatsuon = "変更";
        voice.Hatsuon = "エービーシー";
        Assert.False(lease.IsCurrent);
    }

    [Fact]
    public void Lease_RejectsEffectDisableDuringSynthesis()
    {
        var voice = Voice();
        var effect = new PronunciationAssistEffect { IsEnabled = true };
        Assert.True(ReviewAssistEffectCollection.TryAdd(voice, effect, out _));
        using var lease = new VoiceApplyLease(voice);
        Assert.True(lease.IsCurrent);
        effect.IsEnabled = false;
        Assert.False(lease.IsCurrent);
    }

    [Fact]
    public void Lease_RejectsRuleChangesDuringSynthesis()
    {
        var voice = Voice();
        var effect = new PronunciationAssistEffect { IsEnabled = true };
        Assert.True(ReviewAssistEffectCollection.TryAdd(voice, effect, out _));
        using var lease = new VoiceApplyLease(voice);
        effect.HelperRulesJson = "{changed}";
        Assert.False(lease.IsCurrent);
    }

    [Fact]
    public void Lease_AllowsTimelineMovesButRejectsRemovalAndDisposal()
    {
        var voice = Voice();
        bool present = true;
        using var lease = new VoiceApplyLease(voice, () => present);
        voice.Frame = 900;
        voice.Layer = 4;
        Assert.True(lease.IsCurrent);
        present = false;
        Assert.False(lease.IsCurrent);
        present = true;
        lease.Dispose();
        Assert.False(lease.IsCurrent);
    }

    [Fact]
    public void WaveCommit_StaleAfterStagingPreservesOriginalAndCleansTemporaryFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "vqa-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "new.wav");
            var target = Path.Combine(directory, "voice.wav");
            File.WriteAllBytes(source, [4, 5, 6]);
            File.WriteAllBytes(target, [1, 2, 3]);
            var checks = 0;
            Assert.False(AtomicWaveFile.TryReplace(source, target, () => ++checks == 1));
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(target));
            Assert.Equal(2, Directory.GetFiles(directory).Length);
            Assert.True(AtomicWaveFile.TryReplace(source, target, () => true));
            Assert.Equal(new byte[] { 4, 5, 6 }, File.ReadAllBytes(target));
            Assert.Equal(2, Directory.GetFiles(directory).Length);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void WaveCommit_CopyFailureDoesNotTruncateCurrentAudio()
    {
        var directory = Path.Combine(Path.GetTempPath(), "vqa-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var target = Path.Combine(directory, "voice.wav");
            File.WriteAllBytes(target, [1, 2, 3]);
            Assert.Throws<FileNotFoundException>(() => AtomicWaveFile.TryReplace(
                Path.Combine(directory, "missing.wav"), target, () => true));
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(target));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void BoundaryOnlyImport_CreatesEnabledEffectAndUndoesMembership()
    {
        var voice = Voice();
        var session = Export([voice]);
        var decoded = Decode(session.Package, new ReviewCorrectionWireOperation("addBoundary", Position: 1));
        var plan = ReviewImportPlanner.Build(session.Package, decoded, [voice], session);
        var prepared = ReviewImportApplier.Prepare(plan, [session.Package.Voices[0].Target.ExportRef]);
        Assert.True(prepared.IsSuccess, prepared.Message);
        Assert.True(prepared.Journal!.Commit().IsSuccess);
        Assert.True(Assert.Single(ReviewAssistEffectCollection.Enumerate(voice)).IsEnabled);
        Assert.Equal("A<w0>BC", voice.Serif);
        prepared.Journal.UndoOrThrow();
        Assert.Empty(ReviewAssistEffectCollection.Enumerate(voice));
        Assert.Equal("ABC", voice.Serif);
    }

    [Fact]
    public void SameSession_DeletedVoiceDoesNotRebindToIdenticalClone()
    {
        var original = Voice();
        var session = Export([original]);
        var decoded = Decode(session.Package, new ReviewCorrectionWireOperation("noChange"));
        var clone = Voice();
        var plan = ReviewImportPlanner.Build(session.Package, decoded, [clone], session);
        var item = Assert.Single(plan.Items);
        Assert.Equal(ReviewImportResolutionStatus.Missing, item.ResolutionStatus);
        Assert.False(item.CanApply);
        Assert.Null(item.Target);
    }

    [Fact]
    public void CrossSession_TwoRecordsCannotApplyToOneLiveTarget()
    {
        var session = Export([Voice(0), Voice(100)]);
        var decoded = Decode(session.Package, new ReviewCorrectionWireOperation("setReading", Reading: "変更"));
        var current = Voice(200);
        var plan = ReviewImportPlanner.Build(session.Package, decoded, [current]);
        var prepared = ReviewImportApplier.Prepare(plan,
            session.Package.Voices.Select(v => v.Target.ExportRef).ToArray());
        Assert.Equal(ReviewImportPrepareStatus.InvalidSelection, prepared.Status);
        Assert.Equal("エービーシー", current.Hatsuon);
    }

    [Fact]
    public void Commit_RechecksLiveMembershipAfterPreview()
    {
        var voice = Voice();
        var session = Export([voice]);
        var plan = ReviewImportPlanner.Build(session.Package,
            Decode(session.Package, new ReviewCorrectionWireOperation("setReading", Reading: "変更")),
            [voice], session);
        bool present = true;
        var prepared = ReviewImportApplier.Prepare(plan,
            [session.Package.Voices[0].Target.ExportRef], _ => present);
        Assert.True(prepared.IsSuccess, prepared.Message);
        present = false;
        Assert.Equal(ReviewImportCommitStatus.SourceChanged, prepared.Journal!.Commit().Status);
        Assert.Equal("エービーシー", voice.Hatsuon);
    }

    [Fact]
    public void Planner_DoesNotTreatDecodeSuccessAsExportValidation()
    {
        var voice = Voice();
        var session = Export([voice]);
        var record = session.Package.Voices[0];
        var raw = ReviewCorrectionJson.Serialize(new ReviewCorrectionWirePackage(
            ReviewCorrectionValidator.Schema, "wrong-session", [
                new ReviewCorrectionWireRecord(record.Target.ExportRef, record.SourceFingerprint,
                    [new ReviewCorrectionWireOperation("noChange")]) ]));
        var decoded = ReviewCorrectionJson.Decode(raw);
        Assert.True(decoded.IsSuccess);
        var plan = ReviewImportPlanner.Build(session.Package, decoded, [voice], session);
        Assert.Equal(ReviewImportBuildStatus.InvalidCorrectionPackage, plan.Status);
        Assert.Empty(plan.Items);
    }

    [Fact]
    public void CorrectionJson_RejectsUnknownFieldsAndOversizedInput()
    {
        const string unknown = "{\"schema\":\"ymm4.voice-corrections.v0\",\"exportSessionId\":\"s\",\"corrections\":[],\"execute\":\"x\"}";
        Assert.False(ReviewCorrectionJson.Decode(unknown).IsSuccess);
        var large = ReviewCorrectionJson.Decode(new string(' ', 8 * 1024 * 1024 + 1));
        Assert.False(large.IsSuccess);
        Assert.Contains("limit", Assert.Single(large.Errors).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("=1+1")]
    [InlineData(" +1+1")]
    [InlineData("\t@SUM(A1)")]
    [InlineData("-1+1")]
    public void HumanCsv_NeutralizesFormulaTextWithoutChangingCanonicalJson(string text)
    {
        var voice = Voice();
        voice.Serif = text;
        var session = Export([voice]);
        Assert.Contains("\"'" + text + "\"", ReviewExportCsv.Serialize(session.Package), StringComparison.Ordinal);
        Assert.Equal(text, session.Package.Voices[0].Serif);
        Assert.Equal(text, voice.Serif);
    }

    static ReviewExportSession Export(IReadOnlyList<VoiceItem> voices)
    {
        var result = ReviewExportBuilder.Build(voices, "safety-session", DateTimeOffset.UnixEpoch);
        Assert.True(result.IsSuccess, result.Message);
        return result.Session!;
    }

    static ReviewCorrectionDecodeResult Decode(ReviewExportPackage package, ReviewCorrectionWireOperation operation)
    {
        var wire = new ReviewCorrectionWirePackage(ReviewCorrectionValidator.Schema, package.ExportSessionId,
            package.Voices.Select(v => new ReviewCorrectionWireRecord(v.Target.ExportRef,
                v.SourceFingerprint, [operation])).ToArray());
        var decoded = ReviewCorrectionJson.DecodeAndValidateAgainstExport(
            ReviewCorrectionJson.Serialize(wire), package);
        Assert.True(decoded.IsSuccess, string.Join("; ", decoded.Errors.Select(e => e.Message)));
        return decoded;
    }
}
