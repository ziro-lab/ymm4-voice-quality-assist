using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Ymm4VoiceQualityAssist.Core;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4VoiceQualityAssist.Editors;

internal sealed class HelperRulesEditorAttribute
    : PropertyEditorAttribute2
{
    public HelperRulesEditorAttribute()
    {
        PropertyEditorSize =
            PropertyEditorSize.FullWidth;
    }

    public override FrameworkElement Create() =>
        new HelperRulesEditorControl();

    public override void SetBindings(
        FrameworkElement control,
        ItemProperty[] itemProperties)
    {
        if (control
            is HelperRulesEditorControl editor)
        {
            editor.Attach(
                itemProperties);
        }
    }

    public override void ClearBindings(
        FrameworkElement control)
    {
        if (control
            is HelperRulesEditorControl editor)
        {
            editor.Detach();
        }
    }
}

internal sealed class HelperRulesEditorControl
    : UserControl, IPropertyEditorControl, IDisposable
{
    readonly StackPanel root;
    ItemProperty[] properties = [];
    INotifyPropertyChanged? owner;

    public event EventHandler? BeginEdit;
    public event EventHandler? EndEdit;

    public HelperRulesEditorControl()
    {
        root =
            new StackPanel
            {
                Margin =
                    new Thickness(
                        0,
                        2,
                        0,
                        4),
            };

        Content =
            root;
    }

    public void Attach(
        ItemProperty[] itemProperties)
    {
        Detach();

        properties =
            itemProperties
            ?? [];

        if (properties.Length == 1
            && properties[0].PropertyOwner
                is INotifyPropertyChanged notify)
        {
            owner =
                notify;

            owner.PropertyChanged +=
                Owner_PropertyChanged;
        }

        Render();
    }

    public void Detach()
    {
        if (owner is not null)
        {
            owner.PropertyChanged -=
                Owner_PropertyChanged;
        }

        owner = null;
        properties = [];
        root.Children.Clear();
    }

    public void Dispose() =>
        Detach();

    void Owner_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (properties.Length != 1)
            return;

        if (string.IsNullOrEmpty(
                e.PropertyName)
            || string.Equals(
                e.PropertyName,
                properties[0]
                    .PropertyInfo.Name,
                StringComparison.Ordinal))
        {
            Dispatcher.BeginInvoke(
                new Action(Render));
        }
    }

    void Render()
    {
        root.Children.Clear();

        root.Children.Add(
            new TextBlock
            {
                Text =
                    "VOICEVOX解析時だけ補助モーラを挿入し、指定した母音長または子音長を0にします。字幕本文とHatsuonは書き換えません。",
                TextWrapping =
                    TextWrapping.Wrap,
                Opacity = 0.78,
                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        8),
            });

        if (properties.Length != 1)
        {
            AddMessage(
                "補助モーラ設定は1つのVoiceItemを選択して編集してください。複数選択への一括コピーは行いません。");

            return;
        }

        if (properties[0].Item
            is not VoiceItem voice)
        {
            AddMessage(
                "親VoiceItemを取得できないため、補助モーラ設定を編集できません。");

            return;
        }

        var parsed =
            BoundaryMarkerParser.Parse(
                voice.Serif
                ?? string.Empty);

        var cleanSerif =
            parsed.CleanText;

        root.Children.Add(
            new TextBlock
            {
                Text =
                    $"本文 {cleanSerif.Length}文字 / 強制区切り {parsed.ZeroWaitPositions.Count}個",
                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        8),
                Opacity = 0.72,
            });

        var json =
            properties[0]
                .GetValue<string>()
            ?? string.Empty;

        if (!HelperRuleCodec.TryDecode(
            json,
            out var ruleSet,
            out var decodeError))
        {
            AddMessage(
                "保存済みの補助モーラ設定を読み込めません。"
                + Environment.NewLine
                + (decodeError
                    ?? "不明なエラー"));

            return;
        }

        if (ruleSet.Rules.Count == 0)
        {
            root.Children.Add(
                new TextBlock
                {
                    Text =
                        "補助モーラは未設定です。",
                    Margin =
                        new Thickness(
                            0,
                            0,
                            0,
                            6),
                    Opacity = 0.72,
                });
        }
        else
        {
            for (var index = 0;
                 index < ruleSet.Rules.Count;
                 index++)
            {
                root.Children.Add(
                    CreateExistingRuleEditor(
                        cleanSerif,
                        index,
                        ruleSet.Rules[index]));
            }
        }

        root.Children.Add(
            CreateNewRuleEditor(
                cleanSerif));
    }

    FrameworkElement CreateExistingRuleEditor(
        string cleanSerif,
        int ruleIndex,
        HelperMoraRule rule)
    {
        var editor =
            CreateRuleForm(
                cleanSerif,
                rule.Helper,
                rule.Kind,
                rule.Anchor.Position,
                $"ルール {ruleIndex + 1}");

        editor.ApplyButton.Content =
            "適用";

        editor.ApplyButton.Click +=
            (_, _) =>
            {
                if (!TryReadForm(
                    editor,
                    out var helper,
                    out var kind,
                    out var position,
                    out var error))
                {
                    ShowFormError(
                        editor,
                        error);

                    return;
                }

                var result =
                    HelperRuleEditorService.Replace(
                        CurrentJson(),
                        cleanSerif,
                        ruleIndex,
                        helper!,
                        kind,
                        position);

                CommitResult(
                    editor,
                    result);
            };

        var remove =
            new Button
            {
                Content =
                    "削除",
                MinWidth = 64,
                Margin =
                    new Thickness(
                        6,
                        0,
                        0,
                        0),
            };

        remove.Click +=
            (_, _) =>
            {
                var result =
                    HelperRuleEditorService.Remove(
                        CurrentJson(),
                        ruleIndex);

                CommitResult(
                    editor,
                    result);
            };

        editor.ButtonPanel.Children.Add(
            remove);

        return editor.Container;
    }

    FrameworkElement CreateNewRuleEditor(
        string cleanSerif)
    {
        var editor =
            CreateRuleForm(
                cleanSerif,
                string.Empty,
                HelperMoraKind.ZeroVowel,
                0,
                "新しい補助モーラ");

        editor.ApplyButton.Content =
            "追加";

        editor.ApplyButton.Click +=
            (_, _) =>
            {
                if (!TryReadForm(
                    editor,
                    out var helper,
                    out var kind,
                    out var position,
                    out var error))
                {
                    ShowFormError(
                        editor,
                        error);

                    return;
                }

                var result =
                    HelperRuleEditorService.Add(
                        CurrentJson(),
                        cleanSerif,
                        helper!,
                        kind,
                        position);

                CommitResult(
                    editor,
                    result);
            };

        return editor.Container;
    }

    RuleForm CreateRuleForm(
        string cleanSerif,
        string helper,
        HelperMoraKind kind,
        int position,
        string title)
    {
        var panel =
            new StackPanel();

        panel.Children.Add(
            new TextBlock
            {
                Text = title,
                FontWeight =
                    FontWeights.SemiBold,
                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        6),
            });

        var helperBox =
            new TextBox
            {
                Text = helper,
                MinWidth = 90,
                VerticalContentAlignment =
                    VerticalAlignment.Center,
            };

        panel.Children.Add(
            CreateLabeledRow(
                "補助文字",
                helperBox));

        var kindBox =
            new ComboBox
            {
                MinWidth = 150,
                ItemsSource =
                    new[]
                    {
                        new HelperKindOption(
                            HelperMoraKind.ZeroVowel,
                            "母音長を0"),
                        new HelperKindOption(
                            HelperMoraKind.ZeroConsonant,
                            "子音長を0"),
                    },
                DisplayMemberPath =
                    nameof(
                        HelperKindOption.Label),
                SelectedValuePath =
                    nameof(
                        HelperKindOption.Kind),
                SelectedValue =
                    kind,
            };

        panel.Children.Add(
            CreateLabeledRow(
                "処理",
                kindBox));

        var positionBox =
            new TextBox
            {
                Text =
                    position.ToString(
                        System.Globalization
                            .CultureInfo.InvariantCulture),
                Width = 72,
                VerticalContentAlignment =
                    VerticalAlignment.Center,
            };

        panel.Children.Add(
            CreateLabeledRow(
                "本文位置",
                positionBox));

        var preview =
            new TextBlock
            {
                TextWrapping =
                    TextWrapping.Wrap,
                Opacity = 0.78,
                Margin =
                    new Thickness(
                        78,
                        2,
                        0,
                        4),
            };

        void UpdatePreview()
        {
            if (int.TryParse(
                positionBox.Text,
                out var parsedPosition))
            {
                preview.Text =
                    HelperRuleEditorService
                        .CreateContextPreview(
                            cleanSerif,
                            parsedPosition);
            }
            else
            {
                preview.Text =
                    "位置を整数で入力してください";
            }
        }

        positionBox.TextChanged +=
            (_, _) =>
                UpdatePreview();

        UpdatePreview();

        panel.Children.Add(
            preview);

        var errorText =
            new TextBlock
            {
                TextWrapping =
                    TextWrapping.Wrap,
                Margin =
                    new Thickness(
                        0,
                        2,
                        0,
                        4),
            };

        panel.Children.Add(
            errorText);

        var buttonPanel =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,
                HorizontalAlignment =
                    HorizontalAlignment.Right,
            };

        var apply =
            new Button
            {
                MinWidth = 72,
            };

        buttonPanel.Children.Add(
            apply);

        panel.Children.Add(
            buttonPanel);

        var border =
            new Border
            {
                BorderThickness =
                    new Thickness(1),
                CornerRadius =
                    new CornerRadius(4),
                Padding =
                    new Thickness(8),
                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        8),
                Child = panel,
            };

        return new RuleForm(
            border,
            helperBox,
            kindBox,
            positionBox,
            preview,
            errorText,
            buttonPanel,
            apply);
    }

    static FrameworkElement CreateLabeledRow(
        string label,
        FrameworkElement editor)
    {
        var grid =
            new Grid
            {
                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        4),
            };

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(72),
            });

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star),
            });

        var text =
            new TextBlock
            {
                Text = label,
                VerticalAlignment =
                    VerticalAlignment.Center,
                Margin =
                    new Thickness(
                        0,
                        0,
                        6,
                        0),
            };

        Grid.SetColumn(
            text,
            0);

        Grid.SetColumn(
            editor,
            1);

        grid.Children.Add(
            text);

        grid.Children.Add(
            editor);

        return grid;
    }

    bool TryReadForm(
        RuleForm form,
        out string? helper,
        out HelperMoraKind kind,
        out int position,
        out string error)
    {
        helper =
            form.HelperBox.Text;

        kind =
            form.KindBox.SelectedValue
                is HelperMoraKind selected
                    ? selected
                    : HelperMoraKind.ZeroVowel;

        if (!int.TryParse(
            form.PositionBox.Text,
            out position))
        {
            error =
                "本文位置を整数で入力してください。";

            return false;
        }

        if (string.IsNullOrWhiteSpace(
            helper))
        {
            error =
                "補助文字を入力してください。";

            return false;
        }

        error =
            string.Empty;

        return true;
    }

    string CurrentJson() =>
        properties.Length == 1
            ? properties[0]
                .GetValue<string>()
                ?? string.Empty
            : string.Empty;

    void CommitResult(
        RuleForm form,
        HelperRuleEditorResult result)
    {
        if (!result.IsSuccess
            || result.UpdatedJson is null)
        {
            ShowFormError(
                form,
                result.Error
                ?? "設定を変更できませんでした。");

            return;
        }

        form.ErrorText.Text =
            string.Empty;

        BeginEdit?.Invoke(
            this,
            EventArgs.Empty);

        try
        {
            properties[0]
                .SetValue(
                    result.UpdatedJson);
        }
        finally
        {
            EndEdit?.Invoke(
                this,
                EventArgs.Empty);
        }

        Render();
    }

    static void ShowFormError(
        RuleForm form,
        string error) =>
        form.ErrorText.Text =
            error;

    void AddMessage(
        string message) =>
        root.Children.Add(
            new TextBlock
            {
                Text =
                    message,
                TextWrapping =
                    TextWrapping.Wrap,
                Margin =
                    new Thickness(
                        0,
                        4,
                        0,
                        4),
            });

    sealed record HelperKindOption(
        HelperMoraKind Kind,
        string Label);

    sealed record RuleForm(
        Border Container,
        TextBox HelperBox,
        ComboBox KindBox,
        TextBox PositionBox,
        TextBlock Preview,
        TextBlock ErrorText,
        StackPanel ButtonPanel,
        Button ApplyButton);
}
