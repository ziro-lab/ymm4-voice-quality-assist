using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Ymm4VoiceQualityAssist.Runtime;
using Microsoft.Win32;
using Ymm4VoiceQualityAssist.Core;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.UndoRedo;

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
    readonly Button normalizeBoundaryButton;
    readonly Button insertBoundaryButton;
    readonly TextBox boundaryPositionBox;
    readonly Button jsonExportButton;
    readonly Button csvExportButton;
    readonly Button llmPromptExportButton;
    readonly Button importButton;
    readonly Button migrateLegacyButton;

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

        var boundaryTitle = new TextBlock
        {
            Text = "強制区切り",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        };

        var boundaryDescription = new TextBlock
        {
            Text =
                "発音補助Audio Effectで設定した入力記号を、明示操作でcanonical <w0>へ変換します。"
                + " 自動入力監視やHarmonyによるキー横取りは行いません。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        };

        normalizeBoundaryButton = new Button
        {
            Content = "選択ボイスの入力記号を強制区切りへ変換",
            Padding = new Thickness(10, 6, 10, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 260,
            Margin = new Thickness(0, 0, 0, 6),
        };
        normalizeBoundaryButton.Click +=
            (_, _) =>
                NormalizeBoundaryTokens();

        var positionRow =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,
                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        12),
            };

        positionRow.Children.Add(
            new TextBlock
            {
                Text = "本文位置",
                VerticalAlignment =
                    VerticalAlignment.Center,
                Margin =
                    new Thickness(
                        0,
                        0,
                        6,
                        0),
            });

        boundaryPositionBox =
            new TextBox
            {
                Text = "1",
                Width = 64,
                VerticalContentAlignment =
                    VerticalAlignment.Center,
                Margin =
                    new Thickness(
                        0,
                        0,
                        6,
                        0),
            };

        positionRow.Children.Add(
            boundaryPositionBox);

        insertBoundaryButton =
            new Button
            {
                Content = "強制区切りを追加",
                Padding =
                    new Thickness(
                        10,
                        6,
                        10,
                        6),
                MinWidth = 140,
            };

        insertBoundaryButton.Click +=
            (_, _) =>
                InsertBoundaryAtPosition();

        positionRow.Children.Add(
            insertBoundaryButton);

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

        importButton = new Button
        {
            Content = "LLMレビュー結果をインポート",
            Padding = new Thickness(10, 6, 10, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 230,
            Margin = new Thickness(0, 12, 0, 0),
        };
        importButton.Click +=
            async (_, _) =>
                await ImportAsync();

        migrateLegacyButton = new Button
        {
            Content = "旧発音補助設定を音声エフェクトへ移行",
            Padding = new Thickness(10, 6, 10, 6),
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 230,
            Margin = new Thickness(0, 12, 0, 0),
        };
        migrateLegacyButton.Click +=
            (_, _) =>
                MigrateLegacySettings();

        status = new TextBlock
        {
            Text = "Timelineを開いた状態でエクスポートできます。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
        };

        var runtimeStatus = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        };
        runtimeStatus.SetBinding(TextBlock.TextProperty, new Binding(nameof(AssistRuntimeStatus.Message))
        { Source = AssistRuntimeStatus.Current });

        Content = new StackPanel
        {
            Margin = new Thickness(12),
            Children =
            {
                title,
                description,
                boundaryTitle,
                boundaryDescription,
                normalizeBoundaryButton,
                positionRow,
                jsonExportButton,
                csvExportButton,
                llmPromptExportButton,
                importButton,
                migrateLegacyButton,
                status,
                runtimeStatus,
            },
        };
    }

    void NormalizeBoundaryTokens()
    {
        if (DataContext
            is not PronunciationAssistToolViewModel viewModel)
        {
            status.Text =
                "Voice Quality Assistの状態を取得できませんでした。";
            return;
        }

        SetActionButtonsEnabled(false);

        try
        {
            var result =
                viewModel.NormalizeSelectedBoundaryTokens();

            status.Text =
                result.IsSuccess
                    ? result.ChangedBoundaryCount == 0
                        ? result.Message
                            ?? "変換対象の入力記号はありません。"
                        : $"{result.VoiceCount}件のVoiceItemで{result.ChangedBoundaryCount}個の強制区切りを確定しました。YMM4の元に戻す/やり直しに対応しています。"
                    : result.Message
                        ?? "強制区切りへ変換できませんでした。";
        }
        catch (Exception ex)
        {
            status.Text =
                "強制区切りへの変換に失敗しました: "
                + ex.GetBaseException().Message;
        }
        finally
        {
            SetActionButtonsEnabled(true);
        }
    }

    void InsertBoundaryAtPosition()
    {
        if (DataContext
            is not PronunciationAssistToolViewModel viewModel)
        {
            status.Text =
                "Voice Quality Assistの状態を取得できませんでした。";
            return;
        }

        if (!int.TryParse(
                boundaryPositionBox.Text,
                out var position))
        {
            status.Text =
                "本文位置を整数で入力してください。";
            return;
        }

        SetActionButtonsEnabled(false);

        try
        {
            var result =
                viewModel.InsertBoundaryAtSelectedVoice(
                    position);

            status.Text =
                result.IsSuccess
                    ? result.ChangedBoundaryCount == 0
                        ? result.Message
                            ?? "指定位置には既に強制区切りがあります。"
                        : $"本文位置 {position} に強制区切りを追加しました。YMM4の元に戻す/やり直しに対応しています。"
                    : result.Message
                        ?? "強制区切りを追加できませんでした。";
        }
        catch (Exception ex)
        {
            status.Text =
                "強制区切りの追加に失敗しました: "
                + ex.GetBaseException().Message;
        }
        finally
        {
            SetActionButtonsEnabled(true);
        }
    }

    async Task ImportAsync()
    {
        if (DataContext
            is not PronunciationAssistToolViewModel viewModel)
        {
            status.Text =
                "Voice Quality Assistの状態を取得できませんでした。";
            return;
        }

        SetActionButtonsEnabled(false);

        try
        {
            var correctionDialog =
                new OpenFileDialog
                {
                    Title =
                        "LLMレビュー結果JSONを選択",
                    Filter =
                        "JSONファイル (*.json)|*.json|すべてのファイル (*.*)|*.*",
                    DefaultExt = ".json",
                    Multiselect = false,
                    CheckFileExists = true,
                };

            if (correctionDialog.ShowDialog()
                != true)
            {
                status.Text =
                    "インポートをキャンセルしました。";
                return;
            }

            if (new FileInfo(correctionDialog.FileName).Length > 8 * 1024 * 1024)
                throw new InvalidDataException("レビュー結果JSONは8 MiB以下に分割してください。");

            var correctionJson =
                await File.ReadAllTextAsync(
                    correctionDialog.FileName,
                    Encoding.UTF8);

            var decoded =
                ReviewCorrectionJson.Decode(
                    correctionJson);

            if (decoded.WirePackage is null)
            {
                status.Text =
                    "レビュー結果JSONを読み込めませんでした。\n"
                    + FormatWireErrors(
                        decoded.Errors);
                return;
            }

            if (string.IsNullOrWhiteSpace(
                decoded.WirePackage
                    .ExportSessionId))
            {
                status.Text =
                    "レビュー結果にexportSessionIdがありません。";
                return;
            }

            ReviewExportPackage? sourcePackage =
                null;

            if (viewModel.LastReviewExportSession
                is { } live
                && string.Equals(
                    live.Package.ExportSessionId,
                    decoded.WirePackage
                        .ExportSessionId,
                    StringComparison.Ordinal))
            {
                sourcePackage =
                    live.Package;
            }
            else
            {
                var sourceDialog =
                    new OpenFileDialog
                    {
                        Title =
                            "元のVoice Review JSONを選択",
                        Filter =
                            "JSONファイル (*.json)|*.json|すべてのファイル (*.*)|*.*",
                        DefaultExt = ".json",
                        Multiselect = false,
                        CheckFileExists = true,
                    };

                if (sourceDialog.ShowDialog()
                    != true)
                {
                    status.Text =
                        "元のVoice Review JSONが必要です。";
                    return;
                }

                if (new FileInfo(sourceDialog.FileName).Length > 8 * 1024 * 1024)
                    throw new InvalidDataException("元のレビューJSONが8 MiBを超えています。");

                var sourceJson =
                    await File.ReadAllTextAsync(
                        sourceDialog.FileName,
                        Encoding.UTF8);

                if (!ReviewExportJson
                    .TryDeserialize(
                        sourceJson,
                        out sourcePackage,
                        out var sourceError)
                    || sourcePackage is null)
                {
                    status.Text =
                        "元のVoice Review JSONを読み込めませんでした。\n"
                        + (
                            sourceError
                            ?? "不明なエラー"
                        );
                    return;
                }
            }

            var prepared =
                viewModel.PrepareReviewImport(
                    correctionJson,
                    sourcePackage);

            if (!prepared.IsSuccess
                || prepared.Plan is null)
            {
                status.Text =
                    prepared.Message
                    ?? "Voice Review Import Planを作成できませんでした。";
                return;
            }

            var dialog =
                new ReviewImportDialog(
                    prepared.Plan)
                {
                    Owner =
                        Window.GetWindow(this),
                };

            if (dialog.ShowDialog()
                != true)
            {
                status.Text =
                    "レビュー結果の適用をキャンセルしました。";
                return;
            }

            var applied =
                viewModel.ApplyReviewImport(
                    prepared.Plan,
                    dialog.SelectedExportRefs);

            status.Text =
                applied.IsSuccess
                    ? $"{applied.AppliedCount}件のレビュー修正を適用しました。YMM4の元に戻す/やり直しに対応しています。"
                    : applied.Message
                        ?? "レビュー修正を適用できませんでした。";
        }
        catch (Exception ex)
        {
            status.Text =
                "インポートに失敗しました: "
                + ex.GetBaseException().Message;
        }
        finally
        {
            SetActionButtonsEnabled(true);
        }
    }

    void MigrateLegacySettings()
    {
        if (DataContext
            is not PronunciationAssistToolViewModel viewModel)
        {
            status.Text =
                "Voice Quality Assistの状態を取得できませんでした。";
            return;
        }

        SetActionButtonsEnabled(false);

        try
        {
            var result =
                viewModel.MigrateLegacySettings();

            status.Text =
                result.IsSuccess
                    ? result.MigratedCount == 0
                        ? result.Message
                            ?? "移行対象の旧設定はありません。"
                        : $"{result.MigratedCount}件の旧発音補助設定を音声エフェクトへ移行しました。YMM4の元に戻す/やり直しに対応しています。"
                    : result.Message
                        ?? "旧発音補助設定を移行できませんでした。";
        }
        catch (Exception ex)
        {
            status.Text =
                "旧発音補助設定の移行に失敗しました: "
                + ex.GetBaseException().Message;
        }
        finally
        {
            SetActionButtonsEnabled(true);
        }
    }

    static string FormatWireErrors(
        IReadOnlyList<
            ReviewCorrectionWireError>
            errors) =>
        errors.Count == 0
            ? "詳細なし"
            : string.Join(
                Environment.NewLine,
                errors
                    .Take(5)
                    .Select(x =>
                        x.ExportRef is null
                            ? $"{x.Code}: {x.Message}"
                            : $"{x.ExportRef} / {x.Code}: {x.Message}"));

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

        SetActionButtonsEnabled(false);

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

            string? sourceSidecar = null;
            if (format == ReviewExportFileFormat.LlmPrompt)
            {
                sourceSidecar = Path.Combine(Path.GetDirectoryName(dialog.FileName)!,
                    Path.GetFileNameWithoutExtension(dialog.FileName) + ".review-"
                    + prepared.Session.Package.ExportSessionId + ".json");
                // A unique session-qualified sidecar never overwrites an unrelated export.
                await using var stream = new FileStream(sourceSidecar, FileMode.CreateNew, FileAccess.Write);
                var bytes = new UTF8Encoding(false).GetBytes(ReviewExportJson.Serialize(prepared.Session.Package));
                await stream.WriteAsync(bytes);
            }
            try
            {
                await File.WriteAllTextAsync(dialog.FileName, text,
                    new UTF8Encoding(format == ReviewExportFileFormat.Csv));
            }
            catch
            {
                if (sourceSidecar is not null)
                {
                    try { File.Delete(sourceSidecar); } catch (IOException) { }
                }
                throw;
            }

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
                + dialog.FileName
                + (sourceSidecar is null ? string.Empty : "\n再起動後のImport用JSON: " + sourceSidecar);
        }
        catch (Exception ex)
        {
            status.Text =
                "エクスポートに失敗しました: "
                + ex.GetBaseException().Message;
        }
        finally
        {
            SetActionButtonsEnabled(true);
        }
    }

    void SetActionButtonsEnabled(
        bool enabled)
    {
        normalizeBoundaryButton.IsEnabled = enabled;
        insertBoundaryButton.IsEnabled = enabled;
        boundaryPositionBox.IsEnabled = enabled;
        jsonExportButton.IsEnabled = enabled;
        csvExportButton.IsEnabled = enabled;
        llmPromptExportButton.IsEnabled = enabled;
        importButton.IsEnabled = enabled;
        migrateLegacyButton.IsEnabled = enabled;
    }
}

public sealed class PronunciationAssistToolViewModel :
    ITimelineToolViewModel
{
    Timeline? timeline;
    UndoRedoManager? undoRedoManager;

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
        undoRedoManager =
            info.UndoRedoManager;
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

    public ReviewImportPreparationResult
        PrepareReviewImport(
            string correctionJson,
            ReviewExportPackage sourcePackage)
    {
        ArgumentNullException.ThrowIfNull(
            correctionJson);
        ArgumentNullException.ThrowIfNull(
            sourcePackage);

        if (timeline is null)
        {
            return ReviewImportPreparationResult
                .Failure(
                    "Timelineを取得できませんでした。");
        }

        var validated =
            ReviewCorrectionJson
                .DecodeAndValidateAgainstExport(
                    correctionJson,
                    sourcePackage);

        if (!validated.IsSuccess)
        {
            return ReviewImportPreparationResult
                .Failure(
                    "レビュー結果を検証できませんでした。\n"
                    + string.Join(
                        Environment.NewLine,
                        validated.Errors
                            .Take(8)
                            .Select(x =>
                                x.ExportRef is null
                                    ? $"{x.Code}: {x.Message}"
                                    : $"{x.ExportRef} / {x.Code}: {x.Message}")));
        }

        var liveSession =
            LastReviewExportSession is { } live
            && string.Equals(
                live.Package.ExportSessionId,
                sourcePackage.ExportSessionId,
                StringComparison.Ordinal)
                ? live
                : null;

        var currentVoices =
            timeline.Items
                .OfType<VoiceItem>()
                .ToArray();

        var plan =
            ReviewImportPlanner.Build(
                sourcePackage,
                validated,
                currentVoices,
                liveSession);

        if (!plan.IsSuccess)
        {
            return ReviewImportPreparationResult
                .Failure(
                    plan.Message
                    ?? "Import Planを作成できませんでした。");
        }

        return ReviewImportPreparationResult
            .Success(plan);
    }

    public ReviewImportExecutionResult
        ApplyReviewImport(
            ReviewImportPlan plan,
            IReadOnlyCollection<string>
                selectedExportRefs)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(
            selectedExportRefs);

        if (undoRedoManager is null)
        {
            return ReviewImportExecutionResult
                .Failure(
                    "YMM4のUndoRedoManagerを取得できませんでした。");
        }

        var currentTimeline = timeline;
        if (currentTimeline is null)
            return ReviewImportExecutionResult.Failure("対象のTimelineが閉じられました。");
        var prepared = ReviewImportApplier.Prepare(plan, selectedExportRefs,
            voice => ReferenceEquals(timeline, currentTimeline)
                && currentTimeline.Items.Any(x => ReferenceEquals(x, voice)));

        if (!prepared.IsSuccess
            || prepared.Journal is null)
        {
            return ReviewImportExecutionResult
                .Failure(
                    prepared.FailedExportRef is null
                        ? prepared.Message
                            ?? "適用準備に失敗しました。"
                        : $"{prepared.FailedExportRef}: {prepared.Message}");
        }

        var journal =
            prepared.Journal;

        try
        {
            // Close any pending host record before starting one logical
            // Voice Review apply unit.
            undoRedoManager.Record();

            var committed =
                journal.Commit();

            if (!committed.IsSuccess)
            {
                return ReviewImportExecutionResult
                    .Failure(
                        committed.FailedExportRef is null
                            ? committed.Message
                                ?? "適用に失敗しました。"
                            : $"{committed.FailedExportRef}: {committed.Message}");
            }

            // VoiceItem.Serif is a normal YMM4-managed property.
            // The host creates its own property-change undo commands while
            // the current record is open, so adding a second custom command
            // would duplicate the same Serif edit.
            undoRedoManager.Record();

            LastReviewExportSession =
                null;

            return ReviewImportExecutionResult
                .Success(
                    journal.ExportRefs.Count);
        }
        catch (Exception ex)
        {
            string? rollbackError = null;
            try { journal.UndoOrThrow(); }
            catch (Exception rollback) { rollbackError = rollback.GetBaseException().Message; }
            return ReviewImportExecutionResult.Failure(
                (rollbackError is null ? "履歴登録に失敗し、変更を戻しました: "
                    : "履歴登録と復旧に失敗しました。上書き保存せず、対象を確認してください: " + rollbackError + " / ")
                + ex.GetBaseException().Message);
        }
    }

    public ForcedBoundaryToolExecutionResult
        NormalizeSelectedBoundaryTokens()
    {
        if (timeline is null)
        {
            return ForcedBoundaryToolExecutionResult
                .Failure(
                    "Timelineを取得できませんでした。");
        }

        var selected =
            timeline.SelectedItems
                .OfType<VoiceItem>()
                .ToArray();

        var prepared =
            ForcedBoundaryEditPlanner
                .PrepareNormalizeTokens(
                    selected);

        return ExecuteForcedBoundaryEdit(
            prepared);
    }

    public ForcedBoundaryToolExecutionResult
        InsertBoundaryAtSelectedVoice(
            int cleanTextPosition)
    {
        if (timeline is null)
        {
            return ForcedBoundaryToolExecutionResult
                .Failure(
                    "Timelineを取得できませんでした。");
        }

        var selected =
            timeline.SelectedItems
                .OfType<VoiceItem>()
                .ToArray();

        if (selected.Length != 1)
        {
            return ForcedBoundaryToolExecutionResult
                .Failure(
                    "本文位置で強制区切りを追加する場合は、VoiceItemを1件だけ選択してください。");
        }

        var prepared =
            ForcedBoundaryEditPlanner
                .PrepareInsert(
                    selected[0],
                    cleanTextPosition);

        return ExecuteForcedBoundaryEdit(
            prepared);
    }

    ForcedBoundaryToolExecutionResult
        ExecuteForcedBoundaryEdit(
            ForcedBoundaryEditPrepareResult
                prepared)
    {
        if (prepared.Status
            == ForcedBoundaryEditPrepareStatus.NoChanges)
        {
            return ForcedBoundaryToolExecutionResult
                .Success(
                    0,
                    0,
                    prepared.Message);
        }

        if (!prepared.IsReady
            || prepared.Journal is null)
        {
            return ForcedBoundaryToolExecutionResult
                .Failure(
                    prepared.Message
                    ?? "強制区切り編集を準備できませんでした。");
        }

        if (undoRedoManager is null)
        {
            return ForcedBoundaryToolExecutionResult
                .Failure(
                    "YMM4のUndoRedoManagerを取得できませんでした。");
        }

        var currentTimeline =
            timeline;

        if (currentTimeline is null)
        {
            return ForcedBoundaryToolExecutionResult
                .Failure(
                    "対象のTimelineが閉じられました。");
        }

        var journal =
            prepared.Journal;

        try
        {
            undoRedoManager.Record();

            var committed =
                journal.Commit(
                    voice =>
                        ReferenceEquals(
                            timeline,
                            currentTimeline)
                        && currentTimeline.Items
                            .Any(x =>
                                ReferenceEquals(
                                    x,
                                    voice)));

            if (!committed.IsSuccess)
            {
                return ForcedBoundaryToolExecutionResult
                    .Failure(
                        committed.Message
                        ?? "強制区切り編集を適用できませんでした。");
            }

            undoRedoManager.AddCommand(
                new UndoRedoActionCommand(
                    journal.UndoOrThrow,
                    journal.RedoOrThrow));

            undoRedoManager.Record();

            LastReviewExportSession =
                null;

            return ForcedBoundaryToolExecutionResult
                .Success(
                    journal.VoiceCount,
                    journal.ChangedBoundaryCount,
                    null);
        }
        catch (Exception ex)
        {
            string? rollbackError =
                null;

            try
            {
                journal.UndoOrThrow();
            }
            catch (Exception rollback)
            {
                rollbackError =
                    rollback.GetBaseException()
                        .Message;
            }

            return ForcedBoundaryToolExecutionResult
                .Failure(
                    rollbackError is null
                        ? "履歴登録に失敗し、強制区切り編集を戻しました: "
                            + ex.GetBaseException()
                                .Message
                        : "履歴登録と復旧に失敗しました。上書き保存せず対象を確認してください: "
                            + rollbackError
                            + " / "
                            + ex.GetBaseException()
                                .Message);
        }
    }

    public PronunciationAssistMigrationExecutionResult
        MigrateLegacySettings()
    {
        if (timeline is null)
        {
            return PronunciationAssistMigrationExecutionResult
                .Failure(
                    "Timelineを取得できませんでした。");
        }

        if (undoRedoManager is null)
        {
            return PronunciationAssistMigrationExecutionResult
                .Failure(
                    "YMM4のUndoRedoManagerを取得できませんでした。");
        }

        var voices =
            timeline.Items
                .OfType<VoiceItem>()
                .ToArray();

        var prepared =
            PronunciationAssistMigrationBatch
                .Prepare(voices);

        if (prepared.Status
            == PronunciationAssistMigrationBatchPrepareStatus.NoChanges)
        {
            return PronunciationAssistMigrationExecutionResult
                .Success(
                    0,
                    prepared.Message);
        }

        if (!prepared.IsReady
            || prepared.Batch is null)
        {
            return PronunciationAssistMigrationExecutionResult
                .Failure(
                    prepared.Message
                    ?? "旧発音補助設定の移行準備に失敗しました。");
        }

        var batch =
            prepared.Batch;

        try
        {
            undoRedoManager.Record();

            var committed =
                batch.Commit();

            if (!committed.IsSuccess)
            {
                return PronunciationAssistMigrationExecutionResult
                    .Failure(
                        committed.Message
                        ?? "旧発音補助設定の移行に失敗しました。");
            }

            undoRedoManager.AddCommand(
                new UndoRedoActionCommand(
                    batch.UndoOrThrow,
                    batch.RedoOrThrow));

            undoRedoManager.Record();

            LastReviewExportSession =
                null;

            return PronunciationAssistMigrationExecutionResult
                .Success(
                    batch.ItemCount,
                    null);
        }
        catch (Exception ex)
        {
            string? rollbackError = null;

            try
            {
                batch.UndoOrThrow();
            }
            catch (Exception rollback)
            {
                rollbackError =
                    rollback
                        .GetBaseException()
                        .Message;
            }

            return PronunciationAssistMigrationExecutionResult
                .Failure(
                    rollbackError is null
                        ? "履歴登録に失敗し、移行を戻しました: "
                            + ex.GetBaseException().Message
                        : "履歴登録と復旧に失敗しました。上書き保存せず対象を確認してください: "
                            + rollbackError
                            + " / "
                            + ex.GetBaseException().Message);
        }
    }

}

public sealed record ForcedBoundaryToolExecutionResult(
    bool IsSuccess,
    int VoiceCount,
    int ChangedBoundaryCount,
    string? Message)
{
    public static ForcedBoundaryToolExecutionResult
        Success(
            int voiceCount,
            int changedBoundaryCount,
            string? message) =>
        new(
            true,
            voiceCount,
            changedBoundaryCount,
            message);

    public static ForcedBoundaryToolExecutionResult
        Failure(
            string message) =>
        new(
            false,
            0,
            0,
            message);
}


public sealed record ReviewImportPreparationResult(
    bool IsSuccess,
    ReviewImportPlan? Plan,
    string? Message)
{
    public static ReviewImportPreparationResult
        Success(
            ReviewImportPlan plan) =>
        new(
            true,
            plan,
            null);

    public static ReviewImportPreparationResult
        Failure(
            string message) =>
        new(
            false,
            null,
            message);
}

public sealed record ReviewImportExecutionResult(
    bool IsSuccess,
    int AppliedCount,
    string? Message)
{
    public static ReviewImportExecutionResult
        Success(
            int count) =>
        new(
            true,
            count,
            null);

    public static ReviewImportExecutionResult
        Failure(
            string message) =>
        new(
            false,
            0,
            message);
}


public sealed record PronunciationAssistMigrationExecutionResult(
    bool IsSuccess,
    int MigratedCount,
    string? Message)
{
    public static PronunciationAssistMigrationExecutionResult Success(
        int count,
        string? message) =>
        new(
            true,
            count,
            message);

    public static PronunciationAssistMigrationExecutionResult Failure(
        string message) =>
        new(
            false,
            0,
            message);
}
