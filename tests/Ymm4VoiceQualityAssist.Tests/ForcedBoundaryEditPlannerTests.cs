using Ymm4VoiceQualityAssist.Core;
using Ymm4VoiceQualityAssist.Effects;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4VoiceQualityAssist.Tests;

public sealed class ForcedBoundaryEditPlannerTests
{
    [Fact]
    public void NormalizeTokens_CommitAndRollback_AreExact()
    {
        var voice =
            CreateVoice(
                "A|B|C",
                "|");

        var prepared =
            ForcedBoundaryEditPlanner
                .PrepareNormalizeTokens(
                    [voice]);

        Assert.True(
            prepared.IsReady,
            prepared.Message);

        var journal =
            prepared.Journal!;

        Assert.Equal(
            1,
            journal.VoiceCount);

        Assert.Equal(
            2,
            journal.ChangedBoundaryCount);

        Assert.True(
            journal.Commit()
                .IsSuccess);

        Assert.Equal(
            "A<w0>B<w0>C",
            voice.Serif);

        journal.RollbackOrThrow();

        Assert.Equal(
            "A|B|C",
            voice.Serif);
    }

    [Fact]
    public void NormalizeTokens_SourceChangesAfterPrepare_FailsClosed()
    {
        var voice =
            CreateVoice(
                "A|B",
                "|");

        var prepared =
            ForcedBoundaryEditPlanner
                .PrepareNormalizeTokens(
                    [voice]);

        Assert.True(
            prepared.IsReady);

        voice.Serif =
            "changed";

        var commit =
            prepared.Journal!
                .Commit();

        Assert.Equal(
            ForcedBoundaryEditCommitStatus
                .SourceChanged,
            commit.Status);

        Assert.Equal(
            "changed",
            voice.Serif);
    }

    [Fact]
    public void NormalizeTokens_DifferentEnabledTokens_FailsClosed()
    {
        var voice =
            new VoiceItem
            {
                Serif = "A|B",
            };

        AddEffect(
            voice,
            "|");

        AddEffect(
            voice,
            "｜");

        var prepared =
            ForcedBoundaryEditPlanner
                .PrepareNormalizeTokens(
                    [voice]);

        Assert.Equal(
            ForcedBoundaryEditPrepareStatus
                .InvalidSettings,
            prepared.Status);

        Assert.Equal(
            "A|B",
            voice.Serif);
    }

    [Fact]
    public void NormalizeTokens_DisabledAssist_IsNotReinterpreted()
    {
        var voice =
            new VoiceItem
            {
                Serif = "ordinary|text",
            };

        var effect =
            new PronunciationAssistAudioEffect
            {
                IsEnabled = false,
                BoundaryInputToken = "|",
            };

        Assert.True(
            PronunciationAssistSettingsStore
                .TryAdd(
                    voice,
                    effect,
                    out var error),
            error);

        var prepared =
            ForcedBoundaryEditPlanner
                .PrepareNormalizeTokens(
                    [voice]);

        Assert.Equal(
            ForcedBoundaryEditPrepareStatus
                .InvalidSettings,
            prepared.Status);

        Assert.Equal(
            "ordinary|text",
            voice.Serif);
    }

    [Fact]
    public void Insert_CommitAndRollback_AreExact()
    {
        var voice =
            CreateVoice(
                "ABC",
                "|");

        var prepared =
            ForcedBoundaryEditPlanner
                .PrepareInsert(
                    voice,
                    2);

        Assert.True(
            prepared.IsReady,
            prepared.Message);

        var journal =
            prepared.Journal!;

        Assert.True(
            journal.Commit()
                .IsSuccess);

        Assert.Equal(
            "AB<w0>C",
            voice.Serif);

        journal.RollbackOrThrow();

        Assert.Equal(
            "ABC",
            voice.Serif);
    }

    static VoiceItem CreateVoice(
        string serif,
        string token)
    {
        var voice =
            new VoiceItem
            {
                Serif = serif,
            };

        AddEffect(
            voice,
            token);

        return voice;
    }

    static void AddEffect(
        VoiceItem voice,
        string token)
    {
        var effect =
            new PronunciationAssistAudioEffect
            {
                IsEnabled = true,
                BoundaryInputToken =
                    token,
            };

        Assert.True(
            PronunciationAssistSettingsStore
                .TryAdd(
                    voice,
                    effect,
                    out var error),
            error);
    }
}
