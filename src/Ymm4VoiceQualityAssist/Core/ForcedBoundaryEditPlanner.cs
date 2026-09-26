using Ymm4VoiceQualityAssist.Effects;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4VoiceQualityAssist.Core;

public enum ForcedBoundaryEditPrepareStatus
{
    Ready,
    NoChanges,
    InvalidSelection,
    InvalidSettings,
    InvalidSource,
}

public sealed record ForcedBoundaryEditPrepareResult(
    ForcedBoundaryEditPrepareStatus Status,
    ForcedBoundarySerifJournal? Journal,
    string? Message)
{
    public bool IsReady =>
        Status == ForcedBoundaryEditPrepareStatus.Ready
        && Journal is not null;
}

public enum ForcedBoundaryEditCommitStatus
{
    Success,
    AlreadyCommitted,
    SourceChanged,
    ApplyFailed,
}

public sealed record ForcedBoundaryEditCommitResult(
    ForcedBoundaryEditCommitStatus Status,
    string? Message)
{
    public bool IsSuccess =>
        Status == ForcedBoundaryEditCommitStatus.Success;
}

public static class ForcedBoundaryEditPlanner
{
    public static ForcedBoundaryEditPrepareResult
        PrepareNormalizeTokens(
            IReadOnlyList<VoiceItem> voices)
    {
        ArgumentNullException.ThrowIfNull(
            voices);

        if (voices.Count == 0)
        {
            return new(
                ForcedBoundaryEditPrepareStatus.InvalidSelection,
                null,
                "強制区切りへ変換するVoiceItemを選択してください。");
        }

        var entries =
            new List<ForcedBoundarySerifEdit>();

        var changedBoundaries =
            0;

        foreach (var voice in voices)
        {
            ArgumentNullException.ThrowIfNull(
                voice);

            if (!TryResolveInputToken(
                    voice,
                    out var token,
                    out var tokenError))
            {
                return new(
                    ForcedBoundaryEditPrepareStatus.InvalidSettings,
                    null,
                    tokenError);
            }

            var before =
                voice.Serif
                ?? string.Empty;

            var edited =
                ForcedBoundaryTextEditor
                    .NormalizeToken(
                        before,
                        token);

            if (!edited.IsSuccess)
            {
                return new(
                    edited.Status
                        == ForcedBoundaryTextEditStatus.InvalidToken
                            ? ForcedBoundaryEditPrepareStatus.InvalidSettings
                            : ForcedBoundaryEditPrepareStatus.InvalidSource,
                    null,
                    edited.Message);
            }

            if (!edited.HasChanges)
                continue;

            entries.Add(
                new(
                    voice,
                    before,
                    edited.UpdatedSerif!));

            changedBoundaries +=
                edited.ChangedBoundaryCount;
        }

        if (entries.Count == 0)
        {
            return new(
                ForcedBoundaryEditPrepareStatus.NoChanges,
                null,
                "選択VoiceItemに変換対象の入力記号はありません。");
        }

        return new(
            ForcedBoundaryEditPrepareStatus.Ready,
            new ForcedBoundarySerifJournal(
                entries,
                changedBoundaries),
            null);
    }

    public static ForcedBoundaryEditPrepareResult
        PrepareInsert(
            VoiceItem voice,
            int cleanTextPosition)
    {
        ArgumentNullException.ThrowIfNull(
            voice);

        if (!PronunciationAssistSettingsStore
            .Enumerate(voice)
            .Any(x => x.IsEnabled))
        {
            return new(
                ForcedBoundaryEditPrepareStatus.InvalidSettings,
                null,
                "選択VoiceItemに有効な発音補助Audio Effectがありません。");
        }

        var before =
            voice.Serif
            ?? string.Empty;

        var edited =
            ForcedBoundaryTextEditor
                .InsertAtCleanTextPosition(
                    before,
                    cleanTextPosition);

        if (!edited.IsSuccess)
        {
            return new(
                edited.Status
                    == ForcedBoundaryTextEditStatus.InvalidPosition
                        ? ForcedBoundaryEditPrepareStatus.InvalidSelection
                        : ForcedBoundaryEditPrepareStatus.InvalidSource,
                null,
                edited.Message);
        }

        if (!edited.HasChanges)
        {
            return new(
                ForcedBoundaryEditPrepareStatus.NoChanges,
                null,
                edited.Message);
        }

        return new(
            ForcedBoundaryEditPrepareStatus.Ready,
            new ForcedBoundarySerifJournal(
                [
                    new(
                        voice,
                        before,
                        edited.UpdatedSerif!),
                ],
                edited.ChangedBoundaryCount),
            null);
    }

    static bool TryResolveInputToken(
        VoiceItem voice,
        out string token,
        out string? error)
    {
        var enabled =
            PronunciationAssistSettingsStore
                .Enumerate(voice)
                .Where(x => x.IsEnabled)
                .ToArray();

        if (enabled.Length == 0)
        {
            token =
                string.Empty;

            error =
                "選択VoiceItemに有効な発音補助Audio Effectがありません。";
            return false;
        }

        var tokens =
            enabled
                .Select(x =>
                    x.BoundaryInputToken)
                .Distinct(
                    StringComparer.Ordinal)
                .ToArray();

        if (tokens.Length != 1)
        {
            token =
                string.Empty;

            error =
                "同じVoiceItemに異なる強制区切り入力記号を持つ発音補助設定が複数あります。設定を1つに揃えてください。";
            return false;
        }

        if (!ForcedBoundaryInputToken.TryValidate(
                tokens[0],
                out error))
        {
            token =
                string.Empty;
            return false;
        }

        token =
            tokens[0];

        return true;
    }
}

public sealed class ForcedBoundarySerifJournal
{
    readonly IReadOnlyList<
        ForcedBoundarySerifEdit>
        entries;

    bool committed;

    internal ForcedBoundarySerifJournal(
        IReadOnlyList<ForcedBoundarySerifEdit>
            entries,
        int changedBoundaryCount)
    {
        this.entries =
            entries
            ?? throw new ArgumentNullException(
                nameof(entries));

        if (entries.Count == 0)
        {
            throw new ArgumentException(
                "At least one Serif edit is required.",
                nameof(entries));
        }

        ChangedBoundaryCount =
            changedBoundaryCount;
    }

    public int VoiceCount =>
        entries.Count;

    public int ChangedBoundaryCount
    {
        get;
    }

    public ForcedBoundaryEditCommitResult Commit(
        Func<VoiceItem, bool>? isCurrentTarget =
            null)
    {
        if (committed)
        {
            return new(
                ForcedBoundaryEditCommitStatus
                    .AlreadyCommitted,
                "強制区切り編集は既に適用されています。");
        }

        if (!Matches(
                x => x.Before,
                isCurrentTarget))
        {
            return new(
                ForcedBoundaryEditCommitStatus
                    .SourceChanged,
                "プレビュー後にSerifまたは対象Timelineが変わったため、強制区切り編集を中止しました。");
        }

        try
        {
            ApplyOrThrow(
                x => x.Before,
                x => x.After,
                isCurrentTarget);

            committed =
                true;

            return new(
                ForcedBoundaryEditCommitStatus.Success,
                null);
        }
        catch (Exception ex)
        {
            return new(
                ForcedBoundaryEditCommitStatus.ApplyFailed,
                ex.GetBaseException()
                    .Message);
        }
    }

    public void RollbackOrThrow()
    {
        if (!committed)
        {
            throw new InvalidOperationException(
                "強制区切り編集はまだ適用されていません。");
        }

        ApplyOrThrow(
            x => x.After,
            x => x.Before,
            null);
    }

    bool Matches(
        Func<ForcedBoundarySerifEdit, string>
            expected,
        Func<VoiceItem, bool>? isCurrentTarget)
    {
        foreach (var entry in entries)
        {
            if (isCurrentTarget is not null
                && !isCurrentTarget(
                    entry.Voice))
            {
                return false;
            }

            if (!string.Equals(
                    entry.Voice.Serif
                        ?? string.Empty,
                    expected(entry),
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    void ApplyOrThrow(
        Func<ForcedBoundarySerifEdit, string>
            expected,
        Func<ForcedBoundarySerifEdit, string>
            replacement,
        Func<VoiceItem, bool>? isCurrentTarget)
    {
        if (!Matches(
                expected,
                isCurrentTarget))
        {
            throw new InvalidOperationException(
                "Serifが強制区切り編集の想定状態から変化しています。");
        }

        var applied =
            new List<ForcedBoundarySerifEdit>();

        try
        {
            foreach (var entry in entries)
            {
                entry.Voice.Serif =
                    replacement(
                        entry);

                applied.Add(
                    entry);
            }
        }
        catch
        {
            for (var index =
                    applied.Count - 1;
                 index >= 0;
                 index--)
            {
                var entry =
                    applied[index];

                entry.Voice.Serif =
                    expected(
                        entry);
            }

            throw;
        }
    }
}

internal sealed record ForcedBoundarySerifEdit(
    VoiceItem Voice,
    string Before,
    string After);
