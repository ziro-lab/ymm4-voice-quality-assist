using System.Windows;
using System.Windows.Controls;
using Ymm4VoiceQualityAssist.Runtime;
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
            Text = "Voice Quality Assist\n<w0> がある発音補助アイテムを監視します。",
            Margin = new Thickness(12),
            TextWrapping = TextWrapping.Wrap,
        };
    }
}

public sealed class PronunciationAssistToolViewModel :
    ITimelineToolViewModel,
    IDisposable
{
    PronunciationAssistController? controller;

    public void SetTimelineToolInfo(TimelineToolInfo info)
    {
        controller?.Dispose();
        controller = null;

        if (info.Timeline is null
            || info.UndoRedoManager is null)
        {
            return;
        }

        controller = new PronunciationAssistController(
            info.Timeline,
            info.UndoRedoManager);
    }

    public void Dispose()
    {
        controller?.Dispose();
        controller = null;
        GC.SuppressFinalize(this);
    }
}
