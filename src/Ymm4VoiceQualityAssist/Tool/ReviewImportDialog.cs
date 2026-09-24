using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ymm4VoiceQualityAssist.Core;

namespace Ymm4VoiceQualityAssist.Tool;

public sealed class ReviewImportDialog : Window
{
    readonly Dictionary<string, CheckBox> selectors =
        new(StringComparer.Ordinal);

    readonly Button applyButton;

    public ReviewImportDialog(
        ReviewImportPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        Title = "Voice Review Import";
        Width = 760;
        Height = 640;
        MinWidth = 620;
        MinHeight = 420;
        WindowStartupLocation =
            WindowStartupLocation.CenterOwner;

        var root =
            new DockPanel
            {
                Margin =
                    new Thickness(12),
            };

        var header =
            new StackPanel
            {
                Margin =
                    new Thickness(0, 0, 0, 10),
            };

        header.Children.Add(
            new TextBlock
            {
                Text = "Voice Review Import",
                FontSize = 18,
                FontWeight =
                    FontWeights.SemiBold,
            });

        header.Children.Add(
            new TextBlock
            {
                Text =
                    "EXACTのみ適用できます。STALE / MISSING / AMBIGUOUSは自動適用しません。",
                TextWrapping =
                    TextWrapping.Wrap,
                Margin =
                    new Thickness(0, 4, 0, 0),
            });

        DockPanel.SetDock(
            header,
            Dock.Top);

        root.Children.Add(
            header);

        var footer =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,
                HorizontalAlignment =
                    HorizontalAlignment.Right,
                Margin =
                    new Thickness(0, 10, 0, 0),
            };

        var selectAll =
            new Button
            {
                Content = "適用可能をすべて選択",
                Padding =
                    new Thickness(10, 5, 10, 5),
                Margin =
                    new Thickness(0, 0, 6, 0),
            };

        selectAll.Click +=
            (_, _) =>
            {
                foreach (var selector
                    in selectors.Values)
                {
                    if (selector.IsEnabled)
                        selector.IsChecked = true;
                }

                RefreshApplyButton();
            };

        var clear =
            new Button
            {
                Content = "選択解除",
                Padding =
                    new Thickness(10, 5, 10, 5),
                Margin =
                    new Thickness(0, 0, 6, 0),
            };

        clear.Click +=
            (_, _) =>
            {
                foreach (var selector
                    in selectors.Values)
                {
                    selector.IsChecked = false;
                }

                RefreshApplyButton();
            };

        var cancel =
            new Button
            {
                Content = "キャンセル",
                Padding =
                    new Thickness(10, 5, 10, 5),
                Margin =
                    new Thickness(0, 0, 6, 0),
                IsCancel = true,
            };

        applyButton =
            new Button
            {
                Content = "選択項目を適用",
                Padding =
                    new Thickness(12, 5, 12, 5),
                IsDefault = true,
            };

        applyButton.Click +=
            (_, _) =>
            {
                DialogResult = true;
            };

        footer.Children.Add(
            selectAll);

        footer.Children.Add(
            clear);

        footer.Children.Add(
            cancel);

        footer.Children.Add(
            applyButton);

        DockPanel.SetDock(
            footer,
            Dock.Bottom);

        root.Children.Add(
            footer);

        var list =
            new StackPanel();

        foreach (var item
            in plan.Items)
        {
            list.Children.Add(
                BuildRow(item));
        }

        var scroll =
            new ScrollViewer
            {
                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility =
                    ScrollBarVisibility.Disabled,
                Content = list,
            };

        root.Children.Add(
            scroll);

        Content = root;

        RefreshApplyButton();
    }

    public IReadOnlyCollection<string>
        SelectedExportRefs =>
        selectors
            .Where(x =>
                x.Value.IsChecked == true)
            .Select(x => x.Key)
            .ToArray();

    FrameworkElement BuildRow(
        ReviewImportItem item)
    {
        var border =
            new Border
            {
                BorderBrush =
                    Brushes.Gray,
                BorderThickness =
                    new Thickness(1),
                CornerRadius =
                    new CornerRadius(4),
                Padding =
                    new Thickness(8),
                Margin =
                    new Thickness(0, 0, 0, 8),
            };

        var panel =
            new StackPanel();

        var top =
            new DockPanel();

        var canSelect =
            item.CanApply
            && item.Preview is
                { IsNoChange: false };

        var selector =
            new CheckBox
            {
                Content =
                    item.ExportRecord
                        .Target.ExportRef,
                IsEnabled =
                    canSelect,
                IsChecked =
                    canSelect
                    && item.SelectedByDefault,
                FontWeight =
                    FontWeights.SemiBold,
            };

        selector.Checked +=
            (_, _) =>
                RefreshApplyButton();

        selector.Unchecked +=
            (_, _) =>
                RefreshApplyButton();

        selectors[
            item.ExportRecord
                .Target.ExportRef] =
            selector;

        var state =
            new TextBlock
            {
                Text =
                    StatusLabel(
                        item.ResolutionStatus),
                FontWeight =
                    FontWeights.SemiBold,
                HorizontalAlignment =
                    HorizontalAlignment.Right,
            };

        DockPanel.SetDock(
            state,
            Dock.Right);

        top.Children.Add(
            state);

        top.Children.Add(
            selector);

        panel.Children.Add(
            top);

        var identity =
            new TextBlock
            {
                Text =
                    $"{item.ExportRecord.CharacterName ?? "(characterなし)"}"
                    + $"  Frame {item.ExportRecord.Target.Frame}"
                    + $" / Layer {item.ExportRecord.Target.Layer}",
                TextWrapping =
                    TextWrapping.Wrap,
                Margin =
                    new Thickness(0, 4, 0, 0),
            };

        panel.Children.Add(
            identity);

        panel.Children.Add(
            new TextBlock
            {
                Text =
                    "Serif: "
                    + (item.ExportRecord.Serif
                        ?? string.Empty),
                TextWrapping =
                    TextWrapping.Wrap,
                Margin =
                    new Thickness(0, 3, 0, 0),
            });

        if (item.Preview is
            { } preview)
        {
            var summary =
                PreviewSummary(
                    preview);

            panel.Children.Add(
                new TextBlock
                {
                    Text = summary,
                    TextWrapping =
                        TextWrapping.Wrap,
                    Margin =
                        new Thickness(0, 4, 0, 0),
                });
        }

        if (!string.IsNullOrWhiteSpace(
            item.Message))
        {
            panel.Children.Add(
                new TextBlock
                {
                    Text =
                        item.Message,
                    TextWrapping =
                        TextWrapping.Wrap,
                    Margin =
                        new Thickness(0, 4, 0, 0),
                    FontStyle =
                        FontStyles.Italic,
                });
        }

        border.Child =
            panel;

        return border;
    }

    void RefreshApplyButton()
    {
        applyButton.IsEnabled =
            selectors.Values.Any(x =>
                x.IsEnabled
                && x.IsChecked == true);
    }

    static string StatusLabel(
        ReviewImportResolutionStatus status) =>
        status switch
        {
            ReviewImportResolutionStatus.ExactSessionMatch =>
                "EXACT / same session",
            ReviewImportResolutionStatus.ExactFingerprintMatch =>
                "EXACT / fingerprint",
            ReviewImportResolutionStatus.Stale =>
                "STALE",
            ReviewImportResolutionStatus.Missing =>
                "MISSING",
            ReviewImportResolutionStatus.Ambiguous =>
                "AMBIGUOUS",
            _ =>
                status.ToString(),
        };

    static string PreviewSummary(
        ReviewImportPreview preview)
    {
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
                "Boundary: ["
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
