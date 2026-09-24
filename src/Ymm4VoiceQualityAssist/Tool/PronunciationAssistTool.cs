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
    readonly TextBlock status;
    readonly Button exportButton;

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
                + "Voice Review JSONでは、現在のボイス一覧をLLM/手動レビュー用に書き出せます。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        };

        exportButton = new Button
        {
            Content = "Voice Review JSONをエクスポート",
            Padding = new Thickness(10, 6, 10, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 220,
        };
        exportButton.Click += OnExportClick;

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
                exportButton,
                status,
            },
        };
    }

    async void OnExportClick(
        object sender,
        RoutedEventArgs e)
    {
        if (DataContext
            is not PronunciationAssistToolViewModel viewModel)
        {
            status.Text =
                "Voice Quality Assistの状態を取得できませんでした。";
            return;
        }

        exportButton.IsEnabled = false;

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
                    ?? "Voice Review JSONを作成できませんでした。";
                return;
            }

            if (prepared.Session.Package.Voices.Count == 0)
            {
                status.Text =
                    "現在のTimelineにVoiceItemがありません。";
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Voice Review JSONを保存",
                Filter =
                    "JSONファイル (*.json)|*.json|すべてのファイル (*.*)|*.*",
                DefaultExt = ".json",
                AddExtension = true,
                FileName =
                    "ymm4-voice-review-"
                    + DateTime.Now.ToString(
                        "yyyyMMdd-HHmmss")
                    + ".json",
                OverwritePrompt = true,
            };

            if (dialog.ShowDialog() != true)
            {
                status.Text =
                    "エクスポートをキャンセルしました。";
                return;
            }

            var json =
                ReviewExportJson.Serialize(
                    prepared.Session.Package);

            await File.WriteAllTextAsync(
                dialog.FileName,
                json,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false));

            viewModel.CommitReviewExport(
                prepared.Session);

            status.Text =
                $"{prepared.Session.Package.Voices.Count}件のVoiceItemを書き出しました。\n"
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
            exportButton.IsEnabled = true;
        }
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
