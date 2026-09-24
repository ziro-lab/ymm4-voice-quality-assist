using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Ymm4VoiceQualityAssist.Core;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4VoiceQualityAssist.Tool;

public sealed class PronunciationAssistToolPlugin : IToolPlugin
{
    public string Name => "Voice Quality Assist";
    public Type ViewModelType => typeof(PronunciationAssistToolViewModel);
    public Type ViewType => typeof(PronunciationAssistToolView);
    public bool AllowMultipleInstances => false;

    public string DefaultGroupName =>
        YukkuriMovieMaker.Resources.Localization.Texts.ToolGroupUtilityName;
}

public sealed class PronunciationAssistToolView : UserControl
{
    enum ReviewExportFileFormat
    {
        Json,
        Csv,
    }

    readonly TextBlock status;
    readonly Button jsonExportButton;
    readonly Button csvExportButton;

    public PronunciationAssistToolView()
    {
        var title = new TextBlock
        {
            Text = "Voice Quality Assist",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        };

        var description = new TextBlock
        {
            Text =
                "発音補助のバックグラウンド監視は自動で動作します。\n"
                + "Voice Reviewは、JSONを正本としてLLM/Import用に、CSVを人間向け閲覧用に書き出せます。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        };

        jsonExportButton = new Button
        {
            Content = "Voice Review JSONをエクスポート",
            Padding = new Thickness(10, 6, 10, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 230,
            Margin = new Thickness(0, 0, 0, 6),
        };
        jsonExportButton.Click +=
            async (_, _) =>
                await ExportAsync(
                    ReviewExportFileFormat.Json);

        csvExportButton = new Button
        {
            Content = "閲覧用CSVをエクスポート",
            Padding = new Thickness(10, 6, 10, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 230,
        };
        csvExportButton.Click +=
            async (_, _) =>
                await ExportAsync(
                    ReviewExportFileFormat.Csv);

        status = new TextBlock
        {
            Text = "Timelineを開いた状態でエクスポートできます。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
        };

        Content = new StackPanel
        {
            Margin = new Thickness(12),
            Children =
            {
                title,
                description,
                jsonExportButton,
                csvExportButton,
                status,
            },
        };
    }

    async Task ExportAsync(
        ReviewExportFileFormat format)
    {
        if (DataContext
            is not PronunciationAssistToolViewModel viewModel)
        {
            status.Text =
                "Voice Quality Assistの状態を取得できませんでした。";
            return;
        }

        SetExportButtonsEnabled(false);

        try
        {
            var prepared =
                viewModel.PrepareReviewExport(
                    "session-"
                    + Guid.NewGuid().ToString("N"),
                    DateTimeOffset.UtcNow);

            if (!prepared.IsSuccess
                || prepared.Session is null)
            {
                status.Text =
                    prepared.Message
                    ?? "Voice Reviewデータを作成できませんでした。";
                return;
            }

            if (prepared.Session.Package.Voices.Count == 0)
            {
                status.Text =
                    "現在のTimelineにVoiceItemがありません。";
                return;
            }

            var isJson =
                format == ReviewExportFileFormat.Json;

            var dialog = new SaveFileDialog
            {
                Title = isJson
                    ? "Voice Review JSONを保存"
                    : "Voice Review CSVを保存",
                Filter = isJson
                    ? "JSONファイル (*.json)|*.json|すべてのファイル (*.*)|*.*"
                    : "CSVファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*",
                DefaultExt = isJson
                    ? ".json"
                    : ".csv",
                AddExtension = true,
                FileName =
                    "ymm4-voice-review-"
                    + DateTime.Now.ToString(
                        "yyyyMMdd-HHmmss")
                    + (isJson
                        ? ".json"
                        : ".csv"),
                OverwritePrompt = true,
            };

            if (dialog.ShowDialog() != true)
            {
                status.Text =
                    "エクスポートをキャンセルしました。";
                return;
            }

            var text = isJson
                ? ReviewExportJson.Serialize(
                    prepared.Session.Package)
                : ReviewExportCsv.Serialize(
                    prepared.Session.Package);

            await File.WriteAllTextAsync(
                dialog.FileName,
                text,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier:
                        !isJson));

            viewModel.CommitReviewExport(
                prepared.Session);

            status.Text =
                $"{prepared.Session.Package.Voices.Count}件のVoiceItemを"
                + (isJson
                    ? "JSON"
                    : "CSV")
                + "へ書き出しました。\n"
                + dialog.FileName;
        }
        catch (Exception ex)
        {
            status.Text =
                "エクスポートに失敗しました: "
                + ex.GetBaseException().Message;
        }
        finally
        {
            SetExportButtonsEnabled(true);
        }
    }

    void SetExportButtonsEnabled(
        bool enabled)
    {
        jsonExportButton.IsEnabled = enabled;
        csvExportButton.IsEnabled = enabled;
    }
}

public sealed class PronunciationAssistToolViewModel :
    ITimelineToolViewModel
{
    Timeline? timeline;

    public ReviewExportSession? LastReviewExportSession
    {
        get;
        private set;
    }

    public void SetTimelineToolInfo(
        TimelineToolInfo info)
    {
        if (!ReferenceEquals(
            timeline,
            info.Timeline))
        {
            LastReviewExportSession = null;
        }

        timeline = info.Timeline;
    }

    public ReviewExportBuildResult PrepareReviewExport(
        string exportSessionId,
        DateTimeOffset exportedAt)
    {
        if (timeline is null)
        {
            return ReviewExportBuildResult.Failure(
                ReviewExportBuildStatus.InvalidVoiceSource,
                null,
                "Timelineを取得できませんでした。");
        }

        var voices = timeline.Items
            .OfType<VoiceItem>()
            .ToArray();

        return ReviewExportBuilder.Build(
            voices,
            exportSessionId,
            exportedAt);
    }

    public void CommitReviewExport(
        ReviewExportSession session)
    {
        ArgumentNullException.ThrowIfNull(
            session);

        LastReviewExportSession =
            session;
    }
}
