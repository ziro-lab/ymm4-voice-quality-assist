using System.Windows;
using System.Windows.Controls;
using YukkuriMovieMaker.Plugin;

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
    public PronunciationAssistToolView()
    {
        Content = new TextBlock
        {
            Text = "Voice Quality Assist\nバックグラウンド監視は自動で動作します。\n<w0> を含む発音補助アイテムだけを処理します。",
            Margin = new Thickness(12),
            TextWrapping = TextWrapping.Wrap,
        };
    }
}

public sealed class PronunciationAssistToolViewModel :
    ITimelineToolViewModel
{
    public void SetTimelineToolInfo(TimelineToolInfo info)
    {
        // The A1 runtime is started by IPlugin.Initialize and follows
        // MainViewModel.ActiveTimelineViewModel through public APIs.
        // The Tool is optional status/help UI and must not create a
        // second controller.
    }
}
