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
        LlmPrompt,
    }

    readonly TextBlock status;
    readonly Button jsonExportButton;
    readonly Button csvExportButton;
    readonly Button llmPromptExportButton;

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
                + "Voice ReviewはJSON正本、閲覧用CSV、LLMへそのまま渡せるレビュー指示ファイルを書き出せます。",
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
            Margin = new Thickness(0, 0, 0, 6),
        };
        csvExportButton.Click +=
            async (_, _) =>
                await ExportAsync(
                    ReviewExportFileFormat.Csv);

        llmPromptExportButton = new Button
        {
            Content = "LLMレビュー用プロンプトをエクスポート",
            Padding = new Thickness(10, 6, 10, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 230,
        };
        llmPromptExportButton.Click +=
            async (_, _) =>
                await ExportAsync(
                    ReviewExportFileFormat.LlmPrompt);

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
                llmPromptExportButton,
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

            var title = format switch
            {
                ReviewExportFileFormat.Json =>
                    "Voice Review JSONを保存",
                ReviewExportFileFormat.Csv =>
                    "Voice Review CSVを保存",
                ReviewExportFileFormat.LlmPrompt =>
                    "LLMレビュー用プロンプトを保存",
                _ =>
                    throw new ArgumentOutOfRangeException(
                        nameof(format)),
            };

            var filter = format switch
            {
                ReviewExportFileFormat.Json =>
                    "JSONファイル (*.json)|*.json|すべてのファイル (*.*)|*.*",
                ReviewExportFileFormat.Csv =>
                    "CSVファイル (*.csv)|*.csv|すべてのファイル (*.*)|*.*",
                ReviewExportFileFormat.LlmPrompt =>
                    "テキストファイル (*.txt)|*.txt|すべてのファイル (*.*)|*.*",
                _ =>
                    throw new ArgumentOutOfRangeException(
                        nameof(format)),
            };

            var extension = format switch
            {
                ReviewExportFileFormat.Json =>
                    ".json",
                ReviewExportFileFormat.Csv =>
                    ".csv",
                ReviewExportFileFormat.LlmPrompt =>
                    ".txt",
                _ =>
                    throw new ArgumentOutOfRangeException(
                        nameof(format)),
            };

            var dialog = new SaveFileDialog
            {
                Title = title,
                Filter = filter,
                DefaultExt = extension,
                AddExtension = true,
                FileName =
                    "ymm4-voice-review-"
                    + DateTime.Now.ToString(
                        "yyyyMMdd-HHmmss")
                    + extension,
                OverwritePrompt = true,
            };

            if (dialog.ShowDialog() != true)
            {
                status.Text =
                    "エクスポートをキャンセルしました。";
                return;
            }

            var text = format switch
            {
                ReviewExportFileFormat.Json =>
                    ReviewExportJson.Serialize(
                        prepared.Session.Package),
                ReviewExportFileFormat.Csv =>
                    ReviewExportCsv.Serialize(
                        prepared.Session.Package),
                ReviewExportFileFormat.LlmPrompt =>
                    ReviewLlmPrompt.Build(
                        prepared.Session.Package),
                _ =>
                    throw new ArgumentOutOfRangeException(
                        nameof(format)),
            };

            await File.WriteAllTextAsync(
                dialog.FileName,
                text,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier:
                        format
                        == ReviewExportFileFormat.Csv));

            viewModel.CommitReviewExport(
                prepared.Session);

            var formatLabel = format switch
            {
                ReviewExportFileFormat.Json =>
                    "JSON",
                ReviewExportFileFormat.Csv =>
                    "CSV",
                ReviewExportFileFormat.LlmPrompt =>
                    "LLMレビュー用プロンプト",
                _ =>
                    format.ToString(),
            };

            status.Text =
                $"{prepared.Session.Package.Voices.Count}件のVoiceItemを"
                + formatLabel
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
        llmPromptExportButton.IsEnabled = enabled;
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
