using Ymm4VoiceQualityAssist.Effects;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4VoiceQualityAssist.Core;

/// <summary>
/// Compatibility facade used by Review Bridge and existing tests.
///
/// Product storage policy now lives in <see cref="PronunciationAssistSettingsStore"/>:
/// Audio Effect is canonical/new-write, while legacy subtitle/video Effects remain
/// readable and removable for candidate compatibility and migration.
/// </summary>
public static class ReviewAssistEffectCollection
{
    public static IReadOnlyList<IPronunciationAssistSettings> Enumerate(
        VoiceItem voice) =>
        PronunciationAssistSettingsStore.Enumerate(voice);

    public static bool Contains(
        VoiceItem voice,
        IPronunciationAssistSettings effect) =>
        PronunciationAssistSettingsStore.Contains(
            voice,
            effect);

    public static bool TryAdd(
        VoiceItem voice,
        IPronunciationAssistSettings effect,
        out string? error) =>
        PronunciationAssistSettingsStore.TryAdd(
            voice,
            effect,
            out error);

    public static bool TryRemove(
        VoiceItem voice,
        IPronunciationAssistSettings effect,
        out string? error) =>
        PronunciationAssistSettingsStore.TryRemove(
            voice,
            effect,
            out error);

    public static IPronunciationAssistSettings CreateCanonical() =>
        PronunciationAssistSettingsStore.CreateCanonical();
}
