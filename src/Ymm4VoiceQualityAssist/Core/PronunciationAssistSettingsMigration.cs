using Ymm4VoiceQualityAssist.Effects;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4VoiceQualityAssist.Core;

public enum PronunciationAssistMigrationPrepareStatus
{
    Ready,
    NoLegacySettings,
    CanonicalSettingsAlreadyPresent,
}

public sealed record PronunciationAssistMigrationPrepareResult(
    PronunciationAssistMigrationPrepareStatus Status,
    PronunciationAssistMigrationJournal? Journal,
    string? Message)
{
    public bool IsReady =>
        Status == PronunciationAssistMigrationPrepareStatus.Ready
        && Journal is not null;
}

public enum PronunciationAssistMigrationCommitStatus
{
    Success,
    AlreadyCommitted,
    SourceChanged,
    ApplyFailed,
}

public sealed record PronunciationAssistMigrationCommitResult(
    PronunciationAssistMigrationCommitStatus Status,
    string? Message)
{
    public bool IsSuccess =>
        Status == PronunciationAssistMigrationCommitStatus.Success;
}

public static class PronunciationAssistSettingsMigration
{
    public static PronunciationAssistMigrationPrepareResult Prepare(
        VoiceItem voice)
    {
        ArgumentNullException.ThrowIfNull(voice);

        var legacy =
            PronunciationAssistSettingsStore
                .EnumerateLegacy(voice)
                .ToArray();

        if (legacy.Length == 0)
        {
            return new(
                PronunciationAssistMigrationPrepareStatus.NoLegacySettings,
                null,
                "No legacy Pronunciation Assist settings were found.");
        }

        if (PronunciationAssistSettingsStore
            .EnumerateAudio(voice)
            .Count > 0)
        {
            return new(
                PronunciationAssistMigrationPrepareStatus
                    .CanonicalSettingsAlreadyPresent,
                null,
                "Audio Effect settings already exist. Automatic legacy migration is intentionally blocked to avoid duplicate or conflicting settings.");
        }

        var pairs =
            legacy
                .Select(x =>
                {
                    var snapshot =
                        PronunciationAssistSettingsSnapshot
                            .Capture(x);

                    var audio =
                        new PronunciationAssistAudioEffect();

                    snapshot.ApplyTo(audio);

                    return new PronunciationAssistMigrationPair(
                        x,
                        audio,
                        snapshot);
                })
                .ToArray();

        return new(
            PronunciationAssistMigrationPrepareStatus.Ready,
            new PronunciationAssistMigrationJournal(
                voice,
                pairs),
            null);
    }
}

public sealed class PronunciationAssistMigrationJournal
{
    readonly VoiceItem voice;
    readonly IReadOnlyList<PronunciationAssistMigrationPair> pairs;
    bool committed;

    internal PronunciationAssistMigrationJournal(
        VoiceItem voice,
        IReadOnlyList<PronunciationAssistMigrationPair> pairs)
    {
        this.voice =
            voice
            ?? throw new ArgumentNullException(
                nameof(voice));

        this.pairs =
            pairs
            ?? throw new ArgumentNullException(
                nameof(pairs));
    }

    public IReadOnlyList<IPronunciationAssistSettings>
        LegacySettings =>
        pairs
            .Select(x =>
                (IPronunciationAssistSettings)x.Legacy)
            .ToArray();

    public IReadOnlyList<IPronunciationAssistSettings>
        AudioSettings =>
        pairs
            .Select(x =>
                (IPronunciationAssistSettings)x.Audio)
            .ToArray();

    public PronunciationAssistMigrationCommitResult Commit()
    {
        if (committed)
        {
            return new(
                PronunciationAssistMigrationCommitStatus.AlreadyCommitted,
                "Migration journal has already been committed.");
        }

        if (!MatchesPreparedSource())
        {
            return new(
                PronunciationAssistMigrationCommitStatus.SourceChanged,
                "Pronunciation Assist settings changed after migration preview.");
        }

        try
        {
            ApplyMigratedStateOrThrow();
            committed = true;

            return new(
                PronunciationAssistMigrationCommitStatus.Success,
                null);
        }
        catch (Exception ex)
        {
            TryRestoreLegacyState();

            return new(
                PronunciationAssistMigrationCommitStatus.ApplyFailed,
                ex.GetBaseException().Message);
        }
    }

    public void UndoOrThrow()
    {
        if (!committed)
        {
            throw new InvalidOperationException(
                "Migration journal has not been committed.");
        }

        ApplyLegacyStateOrThrow();
    }

    public void RedoOrThrow()
    {
        if (!committed)
        {
            throw new InvalidOperationException(
                "Migration journal has not been committed.");
        }

        ApplyMigratedStateOrThrow();
    }

    bool MatchesPreparedSource()
    {
        if (PronunciationAssistSettingsStore
            .EnumerateAudio(voice)
            .Count != 0)
        {
            return false;
        }

        foreach (var pair in pairs)
        {
            if (!PronunciationAssistSettingsStore.Contains(
                    voice,
                    pair.Legacy))
            {
                return false;
            }

            if (PronunciationAssistSettingsSnapshot
                    .Capture(pair.Legacy)
                != pair.Snapshot)
            {
                return false;
            }
        }

        return true;
    }

    void ApplyMigratedStateOrThrow()
    {
        var addedAudio =
            new List<PronunciationAssistAudioEffect>();

        var removedLegacy =
            new List<PronunciationAssistEffect>();

        try
        {
            foreach (var pair in pairs)
            {
                pair.Snapshot.ApplyTo(
                    pair.Audio);

                if (!PronunciationAssistSettingsStore.TryAdd(
                        voice,
                        pair.Audio,
                        out var error))
                {
                    throw new InvalidOperationException(
                        error
                        ?? "Failed to attach migrated Audio Effect.");
                }

                addedAudio.Add(
                    pair.Audio);
            }

            foreach (var pair in pairs)
            {
                if (!PronunciationAssistSettingsStore.TryRemove(
                        voice,
                        pair.Legacy,
                        out var error))
                {
                    throw new InvalidOperationException(
                        error
                        ?? "Failed to remove legacy Pronunciation Assist Effect.");
                }

                removedLegacy.Add(
                    pair.Legacy);
            }
        }
        catch
        {
            foreach (var legacy in removedLegacy)
            {
                PronunciationAssistSettingsStore.TryAdd(
                    voice,
                    legacy,
                    out _);
            }

            foreach (var audio in addedAudio)
            {
                PronunciationAssistSettingsStore.TryRemove(
                    voice,
                    audio,
                    out _);
            }

            throw;
        }
    }

    void ApplyLegacyStateOrThrow()
    {
        var addedLegacy =
            new List<PronunciationAssistEffect>();

        var removedAudio =
            new List<PronunciationAssistAudioEffect>();

        try
        {
            foreach (var pair in pairs)
            {
                pair.Snapshot.ApplyTo(
                    pair.Legacy);

                if (!PronunciationAssistSettingsStore.TryAdd(
                        voice,
                        pair.Legacy,
                        out var error))
                {
                    throw new InvalidOperationException(
                        error
                        ?? "Failed to restore legacy Pronunciation Assist Effect.");
                }

                addedLegacy.Add(
                    pair.Legacy);
            }

            foreach (var pair in pairs)
            {
                if (!PronunciationAssistSettingsStore.TryRemove(
                        voice,
                        pair.Audio,
                        out var error))
                {
                    throw new InvalidOperationException(
                        error
                        ?? "Failed to remove migrated Audio Effect.");
                }

                removedAudio.Add(
                    pair.Audio);
            }
        }
        catch
        {
            foreach (var audio in removedAudio)
            {
                PronunciationAssistSettingsStore.TryAdd(
                    voice,
                    audio,
                    out _);
            }

            foreach (var legacy in addedLegacy)
            {
                PronunciationAssistSettingsStore.TryRemove(
                    voice,
                    legacy,
                    out _);
            }

            throw;
        }
    }

    void TryRestoreLegacyState()
    {
        foreach (var pair in pairs)
        {
            pair.Snapshot.ApplyTo(
                pair.Legacy);

            PronunciationAssistSettingsStore.TryAdd(
                voice,
                pair.Legacy,
                out _);

            PronunciationAssistSettingsStore.TryRemove(
                voice,
                pair.Audio,
                out _);
        }
    }
}

internal sealed record PronunciationAssistMigrationPair(
    PronunciationAssistEffect Legacy,
    PronunciationAssistAudioEffect Audio,
    PronunciationAssistSettingsSnapshot Snapshot);
