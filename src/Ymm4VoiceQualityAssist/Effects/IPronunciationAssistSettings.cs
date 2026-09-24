using Ymm4VoiceQualityAssist.Core;

namespace Ymm4VoiceQualityAssist.Effects;

public interface IPronunciationAssistSettings
{
    bool IsEnabled { get; set; }
    string HelperRulesJson { get; set; }
    ProsodyGesture Prosody { get; set; }
}
