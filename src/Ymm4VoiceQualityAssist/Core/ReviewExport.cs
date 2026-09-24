using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4VoiceQualityAssist.Core;

public sealed record ReviewExportPackage(
    string Schema,
    string ExportSessionId,
    DateTimeOffset ExportedAt,
    IReadOnlyList<ReviewVoiceExportRecord> Voices);

public sealed record ReviewVoiceExportRecord(
    ReviewVoiceTarget Target,
    string SourceFingerprint,
    string? CharacterName,
    ReviewVoiceSpeaker Speaker,
    string? Serif,
    string? Hatsuon,
    ReviewVoiceContext Context,
    ReviewVoiceControls Controls,
    ReviewVoiceAssistSettings Assist);

public sealed record ReviewVoiceTarget(
    string ExportRef,
    int ExportIndex,
    int Frame,
    int Layer);

public sealed record ReviewVoiceSpeaker(
    string? Api,
    string? Id);

public sealed record ReviewVoiceContext(
    string? PreviousSerif,
    string? NextSerif);

public sealed record ReviewVoiceControls(
    string CleanText,
    IReadOnlyList<ReviewVoiceBoundary> Boundaries);

public sealed record ReviewVoiceBoundary(
    int Position,
    string Source);

public sealed record ReviewVoiceAssistSettings(
    bool Enabled,
    IReadOnlyList<SourceFingerprintAssistProfile> Profiles);

public enum ReviewExportBuildStatus
{
    Success,
    InvalidSessionId,
    InvalidVoiceSource,
}

public sealed record ReviewExportBuildResult(
    ReviewExportBuildStatus Status,
    ReviewExportPackage? Package,
    int? FailedExportIndex,
    string? Message)
{
    public bool IsSuccess =>
        Status == ReviewExportBuildStatus.Success
        && Package is not null;

    public static ReviewExportBuildResult Success(
        ReviewExportPackage package) =>
        new(
            ReviewExportBuildStatus.Success,
            package,
            null,
            null);

    public static ReviewExportBuildResult Failure(
        ReviewExportBuildStatus status,
        int? failedExportIndex,
        string message) =>
        new(
            status,
            null,
            failedExportIndex,
            message);
}

public static class ReviewExportBuilder
{
    public const string Schema =
        "ymm4.voice-review.v0";

    public static ReviewExportBuildResult Build(
        IReadOnlyList<VoiceItem> voices,
        string exportSessionId,
        DateTimeOffset exportedAt)
    {
        ArgumentNullException.ThrowIfNull(voices);

        if (string.IsNullOrWhiteSpace(
            exportSessionId))
        {
            return ReviewExportBuildResult.Failure(
                ReviewExportBuildStatus.InvalidSessionId,
                null,
                "exportSessionId is required.");
        }

        var ordered = voices
            .Select(
                (voice, sourceIndex) =>
                    new
                    {
                        Voice = voice
                            ?? throw new ArgumentException(
                                "Voice collection contains null.",
                                nameof(voices)),
                        SourceIndex = sourceIndex,
                    })
            .OrderBy(x => x.Voice.Frame)
            .ThenBy(x => x.Voice.Layer)
            .ThenBy(x => x.SourceIndex)
            .Select((x, exportIndex) =>
                new
                {
                    x.Voice,
                    ExportIndex = exportIndex,
                })
            .ToArray();

        var materialized =
            new List<(
                VoiceItem Voice,
                int ExportIndex,
                SourceFingerprintInput Input,
                SourceFingerprintResult Fingerprint,
                BoundaryMarkerParseResult Controls)>(
                    ordered.Length);

        foreach (var entry in ordered)
        {
            if (!SourceFingerprint.TryCreateInput(
                entry.Voice,
                out var input,
                out var inputError)
                || input is null)
            {
                return ReviewExportBuildResult.Failure(
                    ReviewExportBuildStatus.InvalidVoiceSource,
                    entry.ExportIndex,
                    inputError
                        ?? "Voice source could not be materialized.");
            }

            var fingerprint =
                SourceFingerprint.Create(input);

            BoundaryMarkerParseResult controls;
            try
            {
                controls =
                    BoundaryMarkerParser.Parse(
                        entry.Voice.Serif
                        ?? string.Empty);
            }
            catch (Exception ex)
            {
                return ReviewExportBuildResult.Failure(
                    ReviewExportBuildStatus.InvalidVoiceSource,
                    entry.ExportIndex,
                    "Voice controls could not be parsed: "
                    + ex.GetBaseException().Message);
            }

            materialized.Add(
                (
                    entry.Voice,
                    entry.ExportIndex,
                    input,
                    fingerprint,
                    controls));
        }

        var records =
            new ReviewVoiceExportRecord[
                materialized.Count];

        for (var index = 0;
             index < materialized.Count;
             index++)
        {
            var current =
                materialized[index];

            var previousSerif =
                index > 0
                    ? materialized[index - 1]
                        .Voice.Serif
                    : null;

            var nextSerif =
                index + 1 < materialized.Count
                    ? materialized[index + 1]
                        .Voice.Serif
                    : null;

            var speaker =
                current.Voice.Character
                    ?.Voice
                    ?.Speaker;

            records[index] =
                new ReviewVoiceExportRecord(
                    new ReviewVoiceTarget(
                        $"voice-{current.ExportIndex:D6}",
                        current.ExportIndex,
                        current.Voice.Frame,
                        current.Voice.Layer),
                    current.Fingerprint.Fingerprint,
                    current.Voice.CharacterName,
                    new ReviewVoiceSpeaker(
                        speaker?.API,
                        speaker?.ID),
                    current.Voice.Serif,
                    current.Voice.Hatsuon,
                    new ReviewVoiceContext(
                        previousSerif,
                        nextSerif),
                    new ReviewVoiceControls(
                        current.Controls.CleanText,
                        current.Controls
                            .ZeroWaitPositions
                            .Select(
                                position =>
                                    new ReviewVoiceBoundary(
                                        position,
                                        "w0"))
                            .ToArray()),
                    new ReviewVoiceAssistSettings(
                        current.Input
                            .AssistProfiles.Count > 0,
                        current.Input
                            .AssistProfiles
                            .ToArray()));
        }

        return ReviewExportBuildResult.Success(
            new ReviewExportPackage(
                Schema,
                exportSessionId,
                exportedAt.ToUniversalTime(),
                records));
    }
}

public static class ReviewExportJson
{
    static readonly JsonSerializerOptions Options =
        new()
        {
            PropertyNamingPolicy =
                JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Encoder =
                JavaScriptEncoder
                    .UnsafeRelaxedJsonEscaping,
            Converters =
            {
                new JsonStringEnumConverter(
                    JsonNamingPolicy.CamelCase),
            },
        };

    public static string Serialize(
        ReviewExportPackage package)
    {
        ArgumentNullException.ThrowIfNull(
            package);

        return JsonSerializer.Serialize(
            package,
            Options);
    }

    public static bool TryDeserialize(
        string json,
        out ReviewExportPackage? package,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            package =
                JsonSerializer.Deserialize<
                    ReviewExportPackage>(
                        json,
                        Options);

            if (package is null)
            {
                error =
                    "Review export JSON resolved to null.";
                return false;
            }

            error = null;
            return true;
        }
        catch (Exception ex)
        {
            package = null;
            error =
                ex.GetBaseException().Message;
            return false;
        }
    }
}
