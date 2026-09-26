namespace Ymm4VoiceQualityAssist.Core;

public static class ReviewImportPreviewText
{
    public const string ForcedBoundaryLabel =
        "VOICEVOX自動アクセント用の強制区切り";

    public static string Format(
        ReviewImportPreview preview)
    {
        ArgumentNullException.ThrowIfNull(
            preview);

        if (preview.IsNoChange)
            return "変更なし（noChange）";

        var parts =
            new List<string>();

        if (!string.Equals(
                preview.BeforeHatsuon,
                preview.AfterHatsuon,
                StringComparison.Ordinal))
        {
            parts.Add(
                $"Hatsuon: {preview.BeforeHatsuon ?? ""} → {preview.AfterHatsuon ?? ""}");
        }

        if (!preview.BeforeBoundaries
            .SequenceEqual(
                preview.AfterBoundaries))
        {
            parts.Add(
                ForcedBoundaryLabel
                + ": ["
                + string.Join(
                    ", ",
                    preview.BeforeBoundaries)
                + "] → ["
                + string.Join(
                    ", ",
                    preview.AfterBoundaries)
                + "]");
        }

        if (preview.HelperAdditions.Count > 0)
        {
            parts.Add(
                "Helper: "
                + string.Join(
                    ", ",
                    preview.HelperAdditions
                        .Select(x =>
                            $"{x.Kind}:{x.Helper}@{x.CleanTextPosition}")));
        }

        if (preview.ProposedProsody
            is { } prosody)
        {
            parts.Add(
                "Prosody: "
                + prosody);
        }

        return parts.Count == 0
            ? "durable source変更候補"
            : string.Join(
                Environment.NewLine,
                parts);
    }
}
