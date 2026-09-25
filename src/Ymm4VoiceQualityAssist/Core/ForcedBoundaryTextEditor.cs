using System.Text;

namespace Ymm4VoiceQualityAssist.Core;

public static class ForcedBoundaryInputToken
{
    public const string Default = "|";
    public const int MaxLength = 8;

    public static bool TryValidate(
        string? token,
        out string? error)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            error =
                "強制区切り入力記号は空にできません。";
            return false;
        }

        if (token.Length > MaxLength)
        {
            error =
                $"強制区切り入力記号は{MaxLength}文字以内にしてください。";
            return false;
        }

        if (token.Contains('<')
            || token.Contains('>'))
        {
            error =
                "YMM4制御タグと衝突するため、< または > を入力記号に使えません。";
            return false;
        }

        if (token.Any(char.IsControl))
        {
            error =
                "改行・タブなどの制御文字を入力記号に使えません。";
            return false;
        }

        error = null;
        return true;
    }
}

public enum ForcedBoundaryTextEditStatus
{
    Success,
    NoChanges,
    InvalidToken,
    InvalidSource,
    InvalidPosition,
}

public sealed record ForcedBoundaryTextEditResult(
    ForcedBoundaryTextEditStatus Status,
    string? UpdatedSerif,
    int ChangedBoundaryCount,
    string? Message)
{
    public bool IsSuccess =>
        Status is ForcedBoundaryTextEditStatus.Success
        or ForcedBoundaryTextEditStatus.NoChanges;

    public bool HasChanges =>
        Status == ForcedBoundaryTextEditStatus.Success
        && ChangedBoundaryCount > 0;
}

public static class ForcedBoundaryTextEditor
{
    const string CanonicalMarker = "<w0>";

    public static ForcedBoundaryTextEditResult
        NormalizeToken(
            string serif,
            string? token)
    {
        ArgumentNullException.ThrowIfNull(
            serif);

        if (!ForcedBoundaryInputToken.TryValidate(
                token,
                out var tokenError))
        {
            return new(
                ForcedBoundaryTextEditStatus.InvalidToken,
                null,
                0,
                tokenError);
        }

        if (!TryProjectCleanText(
                serif,
                out _,
                out var sourceError))
        {
            return new(
                ForcedBoundaryTextEditStatus.InvalidSource,
                null,
                0,
                sourceError);
        }

        var builder =
            new StringBuilder(
                serif.Length);

        var changed =
            0;

        for (var index = 0;
             index < serif.Length;)
        {
            if (serif[index] == '<')
            {
                var close =
                    serif.IndexOf(
                        '>',
                        index + 1);

                if (close < 0)
                {
                    return new(
                        ForcedBoundaryTextEditStatus.InvalidSource,
                        null,
                        0,
                        "閉じていないYMM4制御タグがあるため、強制区切りを変換しませんでした。");
                }

                builder.Append(
                    serif,
                    index,
                    close - index + 1);

                index =
                    close + 1;

                continue;
            }

            if (serif.AsSpan(index)
                .StartsWith(
                    token,
                    StringComparison.Ordinal))
            {
                builder.Append(
                    CanonicalMarker);

                changed++;
                index +=
                    token!.Length;

                continue;
            }

            builder.Append(
                serif[index]);

            index++;
        }

        if (changed == 0)
        {
            return new(
                ForcedBoundaryTextEditStatus.NoChanges,
                serif,
                0,
                "変換対象の入力記号はありません。");
        }

        var updated =
            builder.ToString();

        try
        {
            _ =
                BoundaryMarkerParser.Parse(
                    updated);
        }
        catch (Exception ex)
        {
            return new(
                ForcedBoundaryTextEditStatus.InvalidSource,
                null,
                0,
                "変換後のYMM4制御タグを検証できませんでした: "
                + ex.GetBaseException()
                    .Message);
        }

        return new(
            ForcedBoundaryTextEditStatus.Success,
            updated,
            changed,
            null);
    }

    public static ForcedBoundaryTextEditResult
        InsertAtCleanTextPosition(
            string serif,
            int position)
    {
        ArgumentNullException.ThrowIfNull(
            serif);

        if (!TryProjectCleanText(
                serif,
                out var projection,
                out var sourceError))
        {
            return new(
                ForcedBoundaryTextEditStatus.InvalidSource,
                null,
                0,
                sourceError);
        }

        BoundaryMarkerParseResult parsed;

        try
        {
            parsed =
                BoundaryMarkerParser.Parse(
                    serif);
        }
        catch (Exception ex)
        {
            return new(
                ForcedBoundaryTextEditStatus.InvalidSource,
                null,
                0,
                "YMM4制御タグを解析できませんでした: "
                + ex.GetBaseException()
                    .Message);
        }

        if (!string.Equals(
                projection.CleanText,
                parsed.CleanText,
                StringComparison.Ordinal))
        {
            return new(
                ForcedBoundaryTextEditStatus.InvalidSource,
                null,
                0,
                "Serifの制御タグ構造を安全に位置変換できないため、強制区切りを挿入しませんでした。");
        }

        if (position <= 0
            || position >= parsed.CleanText.Length)
        {
            return new(
                ForcedBoundaryTextEditStatus.InvalidPosition,
                null,
                0,
                $"本文位置は1〜{Math.Max(1, parsed.CleanText.Length - 1)}の範囲で指定してください。");
        }

        if (char.IsHighSurrogate(
                parsed.CleanText[position - 1])
            && char.IsLowSurrogate(
                parsed.CleanText[position]))
        {
            return new(
                ForcedBoundaryTextEditStatus.InvalidPosition,
                null,
                0,
                "サロゲートペアの途中には強制区切りを挿入できません。");
        }

        if (parsed.ZeroWaitPositions.Contains(
                position))
        {
            return new(
                ForcedBoundaryTextEditStatus.NoChanges,
                serif,
                0,
                "指定位置には既に強制区切りがあります。");
        }

        if (!projection.SourceIndexByCleanPosition
                .TryGetValue(
                    position,
                    out var sourceIndex))
        {
            return new(
                ForcedBoundaryTextEditStatus.InvalidSource,
                null,
                0,
                "指定位置をSerif上の安全な挿入位置へ変換できませんでした。");
        }

        var updated =
            serif.Insert(
                sourceIndex,
                CanonicalMarker);

        BoundaryMarkerParseResult verified;

        try
        {
            verified =
                BoundaryMarkerParser.Parse(
                    updated);
        }
        catch (Exception ex)
        {
            return new(
                ForcedBoundaryTextEditStatus.InvalidSource,
                null,
                0,
                "挿入後のYMM4制御タグを検証できませんでした: "
                + ex.GetBaseException()
                    .Message);
        }

        if (!string.Equals(
                verified.CleanText,
                parsed.CleanText,
                StringComparison.Ordinal)
            || !verified.ZeroWaitPositions.Contains(
                position))
        {
            return new(
                ForcedBoundaryTextEditStatus.InvalidSource,
                null,
                0,
                "挿入した強制区切りを同じ本文位置で再確認できなかったため、変更しませんでした。");
        }

        return new(
            ForcedBoundaryTextEditStatus.Success,
            updated,
            1,
            null);
    }

    static bool TryProjectCleanText(
        string serif,
        out CleanProjection projection,
        out string? error)
    {
        var builder =
            new StringBuilder(
                serif.Length);

        var sourceIndexByCleanPosition =
            new Dictionary<int, int>();

        var cleanPosition =
            0;

        for (var index = 0;
             index < serif.Length;)
        {
            if (serif[index] == '<')
            {
                var close =
                    serif.IndexOf(
                        '>',
                        index + 1);

                if (close < 0)
                {
                    projection =
                        new(
                            string.Empty,
                            new Dictionary<int, int>());

                    error =
                        "閉じていないYMM4制御タグがあるため、Serifを安全に編集できません。";
                    return false;
                }

                index =
                    close + 1;

                continue;
            }

            sourceIndexByCleanPosition[
                cleanPosition] =
                index;

            builder.Append(
                serif[index]);

            cleanPosition++;
            index++;
        }

        sourceIndexByCleanPosition[
            cleanPosition] =
            serif.Length;

        projection =
            new(
                builder.ToString(),
                sourceIndexByCleanPosition);

        error = null;
        return true;
    }

    sealed record CleanProjection(
        string CleanText,
        IReadOnlyDictionary<int, int>
            SourceIndexByCleanPosition);
}
