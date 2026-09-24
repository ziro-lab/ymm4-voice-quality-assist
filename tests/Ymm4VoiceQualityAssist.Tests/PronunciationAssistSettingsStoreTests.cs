using Ymm4VoiceQualityAssist.Core;
using Ymm4VoiceQualityAssist.Effects;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4VoiceQualityAssist.Tests;

public sealed class PronunciationAssistSettingsStoreTests
{
    [Fact]
    public void CanonicalNewWrite_UsesAudioEffects()
    {
        var voice = new VoiceItem();
        var settings =
            PronunciationAssistSettingsStore.CreateCanonical();

        var audio =
            Assert.IsType<PronunciationAssistAudioEffect>(
                settings);

        audio.HelperRulesJson = "rules";
        audio.Prosody = ProsodyGesture.Hold;

        Assert.True(
            PronunciationAssistSettingsStore.TryAdd(
                voice,
                audio,
                out var error),
            error);

        Assert.Contains(
            audio,
            voice.AudioEffects);

        Assert.DoesNotContain(
            voice.JimakuVideoEffects,
            x => x is PronunciationAssistEffect);

        Assert.Same(
            audio,
            Assert.Single(
                PronunciationAssistSettingsStore
                    .Enumerate(voice)));
    }

    [Fact]
    public void DualRead_PrefersAudioThenLegacy()
    {
        var voice = new VoiceItem();

        var legacy =
            new PronunciationAssistEffect
            {
                IsEnabled = true,
                Prosody = ProsodyGesture.LightFall,
            };

        var audio =
            new PronunciationAssistAudioEffect
            {
                IsEnabled = true,
                Prosody = ProsodyGesture.LightRise,
            };

        Assert.True(
            PronunciationAssistSettingsStore.TryAdd(
                voice,
                legacy,
                out var legacyError),
            legacyError);

        Assert.True(
            PronunciationAssistSettingsStore.TryAdd(
                voice,
                audio,
                out var audioError),
            audioError);

        var entries =
            PronunciationAssistSettingsStore
                .EnumerateEntries(voice);

        Assert.Equal(2, entries.Count);

        Assert.Same(
            audio,
            entries[0].Settings);

        Assert.Equal(
            PronunciationAssistStorageKind.AudioEffect,
            entries[0].Storage);

        Assert.Same(
            legacy,
            entries[1].Settings);

        Assert.Equal(
            PronunciationAssistStorageKind.LegacySubtitleEffect,
            entries[1].Storage);
    }

    [Fact]
    public void Migration_CommitUndoRedo_PreservesExactSettings()
    {
        var voice = new VoiceItem();

        var legacy =
            new PronunciationAssistEffect
            {
                IsEnabled = false,
                HelperRulesJson = "legacy-rules",
                Prosody = ProsodyGesture.Hold,
            };

        Assert.True(
            PronunciationAssistSettingsStore.TryAdd(
                voice,
                legacy,
                out var addError),
            addError);

        var prepared =
            PronunciationAssistSettingsMigration
                .Prepare(voice);

        Assert.True(
            prepared.IsReady,
            prepared.Message);

        var journal =
            prepared.Journal!;

        var commit =
            journal.Commit();

        Assert.True(
            commit.IsSuccess,
            commit.Message);

        Assert.Empty(
            PronunciationAssistSettingsStore
                .EnumerateLegacy(voice));

        var audio =
            Assert.Single(
                PronunciationAssistSettingsStore
                    .EnumerateAudio(voice));

        Assert.False(audio.IsEnabled);
        Assert.Equal(
            "legacy-rules",
            audio.HelperRulesJson);
        Assert.Equal(
            ProsodyGesture.Hold,
            audio.Prosody);

        journal.UndoOrThrow();

        Assert.Empty(
            PronunciationAssistSettingsStore
                .EnumerateAudio(voice));

        var restored =
            Assert.Single(
                PronunciationAssistSettingsStore
                    .EnumerateLegacy(voice));

        Assert.Same(
            legacy,
            restored);

        Assert.False(restored.IsEnabled);
        Assert.Equal(
            "legacy-rules",
            restored.HelperRulesJson);
        Assert.Equal(
            ProsodyGesture.Hold,
            restored.Prosody);

        journal.RedoOrThrow();

        Assert.Empty(
            PronunciationAssistSettingsStore
                .EnumerateLegacy(voice));

        var redone =
            Assert.Single(
                PronunciationAssistSettingsStore
                    .EnumerateAudio(voice));

        Assert.Same(
            audio,
            redone);

        Assert.False(redone.IsEnabled);
        Assert.Equal(
            "legacy-rules",
            redone.HelperRulesJson);
        Assert.Equal(
            ProsodyGesture.Hold,
            redone.Prosody);
    }

    [Fact]
    public void Migration_SourceChangesAfterPreview_FailsClosed()
    {
        var voice = new VoiceItem();

        var legacy =
            new PronunciationAssistEffect
            {
                IsEnabled = true,
                HelperRulesJson = "before",
            };

        Assert.True(
            PronunciationAssistSettingsStore.TryAdd(
                voice,
                legacy,
                out var addError),
            addError);

        var prepared =
            PronunciationAssistSettingsMigration
                .Prepare(voice);

        Assert.True(prepared.IsReady);

        legacy.HelperRulesJson =
            "changed-after-preview";

        var commit =
            prepared.Journal!.Commit();

        Assert.Equal(
            PronunciationAssistMigrationCommitStatus.SourceChanged,
            commit.Status);

        Assert.Single(
            PronunciationAssistSettingsStore
                .EnumerateLegacy(voice));

        Assert.Empty(
            PronunciationAssistSettingsStore
                .EnumerateAudio(voice));
    }

    [Fact]
    public void Migration_ExistingAudioSettings_BlocksAutomaticMerge()
    {
        var voice = new VoiceItem();

        Assert.True(
            PronunciationAssistSettingsStore.TryAdd(
                voice,
                new PronunciationAssistEffect(),
                out var legacyError),
            legacyError);

        Assert.True(
            PronunciationAssistSettingsStore.TryAdd(
                voice,
                new PronunciationAssistAudioEffect(),
                out var audioError),
            audioError);

        var prepared =
            PronunciationAssistSettingsMigration
                .Prepare(voice);

        Assert.False(prepared.IsReady);

        Assert.Equal(
            PronunciationAssistMigrationPrepareStatus
                .CanonicalSettingsAlreadyPresent,
            prepared.Status);
    }
}
