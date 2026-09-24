namespace Ymm4VoiceQualityAssist.Core;

public sealed record HelperRuleEditorResult(
    bool IsSuccess,
    string? UpdatedJson,
    string? Error)
{
    public static HelperRuleEditorResult Success(
        string json) =>
        new(true, json, null);

    public static HelperRuleEditorResult Failure(
        string error) =>
        new(false, null, error);
}

/// <summary>
/// Pure editing helper for the typed helper-mora property editor.
///
/// Durable storage stays HelperRulesJson. UI edits are converted back into
/// semantic HelperMoraRule values with fresh left/right context generated from
/// the current clean Serif.
/// </summary>
public static class HelperRuleEditorService
{
    public static HelperRuleEditorResult Add(
        string? currentJson,
        string cleanSerif,
        string helper,
        HelperMoraKind kind,
        int position)
    {
        if (!TryDecode(
            currentJson,
            out var current,
            out var error))
        {
            return HelperRuleEditorResult.Failure(
                error!);
        }

        if (current.Rules.Any(
            x => x.Anchor.Position == position))
        {
            return HelperRuleEditorResult.Failure(
                $"位置 {position} には既に補助モーラがあります。");
        }

        if (!TryCreateRule(
            cleanSerif,
            helper,
            kind,
            position,
            out var created,
            out error))
        {
            return HelperRuleEditorResult.Failure(
                error!);
        }

        var rules =
            current.Rules
                .Append(created!)
                .OrderBy(x => x.Anchor.Position)
                .ToArray();

        return Encode(
            rules);
    }

    public static HelperRuleEditorResult Replace(
        string? currentJson,
        string cleanSerif,
        int ruleIndex,
        string helper,
        HelperMoraKind kind,
        int position)
    {
        if (!TryDecode(
            currentJson,
            out var current,
            out var error))
        {
            return HelperRuleEditorResult.Failure(
                error!);
        }

        if (ruleIndex < 0
            || ruleIndex >= current.Rules.Count)
        {
            return HelperRuleEditorResult.Failure(
                $"ルール番号 {ruleIndex} は範囲外です。");
        }

        if (current.Rules
            .Where((_, index) =>
                index != ruleIndex)
            .Any(x =>
                x.Anchor.Position == position))
        {
            return HelperRuleEditorResult.Failure(
                $"位置 {position} には既に別の補助モーラがあります。");
        }

        if (!TryCreateRule(
            cleanSerif,
            helper,
            kind,
            position,
            out var replacement,
            out error))
        {
            return HelperRuleEditorResult.Failure(
                error!);
        }

        var rules =
            current.Rules
                .ToArray();

        rules[ruleIndex] =
            replacement!;

        return Encode(
            rules);
    }

    public static HelperRuleEditorResult Remove(
        string? currentJson,
        int ruleIndex)
    {
        if (!TryDecode(
            currentJson,
            out var current,
            out var error))
        {
            return HelperRuleEditorResult.Failure(
                error!);
        }

        if (ruleIndex < 0
            || ruleIndex >= current.Rules.Count)
        {
            return HelperRuleEditorResult.Failure(
                $"ルール番号 {ruleIndex} は範囲外です。");
        }

        var rules =
            current.Rules
                .Where((_, index) =>
                    index != ruleIndex)
                .ToArray();

        return Encode(
            rules);
    }

    public static string CreateContextPreview(
        string cleanSerif,
        int position,
        int contextLength = 8)
    {
        ArgumentNullException.ThrowIfNull(
            cleanSerif);

        if (position < 0
            || position > cleanSerif.Length)
        {
            return "位置が本文の範囲外です";
        }

        var leftStart =
            Math.Max(
                0,
                position - contextLength);

        var rightLength =
            Math.Min(
                contextLength,
                cleanSerif.Length - position);

        var left =
            cleanSerif[leftStart..position];

        var right =
            cleanSerif.Substring(
                position,
                rightLength);

        return $"{left}｜{right}";
    }

    static bool TryDecode(
        string? json,
        out HelperRuleSet ruleSet,
        out string? error)
    {
        if (HelperRuleCodec.TryDecode(
            json,
            out ruleSet,
            out error))
        {
            return true;
        }

        error =
            "補助モーラ設定を読み込めません: "
            + (error ?? "不明なエラー");

        return false;
    }

    static bool TryCreateRule(
        string cleanSerif,
        string helper,
        HelperMoraKind kind,
        int position,
        out HelperMoraRule? rule,
        out string? error)
    {
        rule = null;
        error = null;

        if (string.IsNullOrWhiteSpace(
            helper))
        {
            error =
                "補助文字を入力してください。";
            return false;
        }

        if (!Enum.IsDefined(kind))
        {
            error =
                "未対応の補助モーラ種別です。";
            return false;
        }

        try
        {
            rule =
                HelperRuleFactory.Create(
                    cleanSerif,
                    position,
                    helper,
                    kind);

            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            error =
                $"位置は0〜{cleanSerif.Length}の範囲で指定してください。";
            return false;
        }
        catch (Exception ex)
        {
            error =
                "補助モーラ設定を作成できません: "
                + ex.GetBaseException().Message;
            return false;
        }
    }

    static HelperRuleEditorResult Encode(
        IReadOnlyList<HelperMoraRule> rules)
    {
        try
        {
            return HelperRuleEditorResult.Success(
                HelperRuleCodec.Encode(
                    new HelperRuleSet(
                        HelperRuleSet.CurrentVersion,
                        rules)));
        }
        catch (Exception ex)
        {
            return HelperRuleEditorResult.Failure(
                "補助モーラ設定を保存できません: "
                + ex.GetBaseException().Message);
        }
    }
}
