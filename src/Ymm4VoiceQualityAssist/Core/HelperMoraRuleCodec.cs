using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ymm4VoiceQualityAssist.Core;

public enum HelperRuleDecodeStatus
{
    Success,
    Empty,
    InvalidJson,
    UnsupportedVersion,
    InvalidRule,
    DuplicateId,
}

public sealed record HelperRuleDecodeResult(
    HelperRuleDecodeStatus Status,
    HelperMoraRuleSet RuleSet,
    string? Message)
{
    public bool IsSuccess =>
        Status is HelperRuleDecodeStatus.Success
            or HelperRuleDecodeStatus.Empty;
}

public static class HelperMoraRuleCodec
{
    static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Encode(HelperMoraRuleSet ruleSet)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);

        var validation = Validate(ruleSet);
        if (!validation.IsSuccess)
            throw new ArgumentException(
                validation.Message ?? "Invalid helper rule set.",
                nameof(ruleSet));

        return JsonSerializer.Serialize(ruleSet, Options);
    }

    public static HelperRuleDecodeResult Decode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new HelperRuleDecodeResult(
                HelperRuleDecodeStatus.Empty,
                HelperMoraRuleSet.Empty,
                null);
        }

        HelperMoraRuleSet? ruleSet;
        try
        {
            ruleSet = JsonSerializer.Deserialize<HelperMoraRuleSet>(
                json,
                Options);
        }
        catch (JsonException ex)
        {
            return Failure(
                HelperRuleDecodeStatus.InvalidJson,
                $"Helper rule JSON is invalid: {ex.Message}");
        }

        if (ruleSet is null)
        {
            return Failure(
                HelperRuleDecodeStatus.InvalidJson,
                "Helper rule JSON produced no rule set.");
        }

        return Validate(ruleSet);
    }

    static HelperRuleDecodeResult Validate(
        HelperMoraRuleSet ruleSet)
    {
        if (ruleSet.Version != HelperMoraRuleSet.CurrentVersion)
        {
            return Failure(
                HelperRuleDecodeStatus.UnsupportedVersion,
                $"Unsupported helper rule version: {ruleSet.Version}.");
        }

        if (ruleSet.Rules is null)
        {
            return Failure(
                HelperRuleDecodeStatus.InvalidRule,
                "Helper rule array is null.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rule in ruleSet.Rules)
        {
            if (rule is null
                || string.IsNullOrWhiteSpace(rule.Id)
                || string.IsNullOrWhiteSpace(rule.Helper)
                || rule.Anchor is null
                || rule.Anchor.Position < 0
                || rule.Anchor.LeftContext is null
                || rule.Anchor.RightContext is null)
            {
                return Failure(
                    HelperRuleDecodeStatus.InvalidRule,
                    "A helper rule contains an invalid id, helper, or anchor.");
            }

            if (!ids.Add(rule.Id))
            {
                return Failure(
                    HelperRuleDecodeStatus.DuplicateId,
                    $"Duplicate helper rule id: {rule.Id}.");
            }
        }

        return new HelperRuleDecodeResult(
            HelperRuleDecodeStatus.Success,
            ruleSet,
            null);
    }

    static HelperRuleDecodeResult Failure(
        HelperRuleDecodeStatus status,
        string message) =>
        new(status, HelperMoraRuleSet.Empty, message);

    static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
        };

        options.Converters.Add(
            new JsonStringEnumConverter(
                JsonNamingPolicy.CamelCase));

        return options;
    }
}
