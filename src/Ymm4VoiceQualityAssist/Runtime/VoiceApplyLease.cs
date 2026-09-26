using System.ComponentModel;
using System.Threading;
using Ymm4VoiceQualityAssist.Core;
using Ymm4VoiceQualityAssist.Effects;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4VoiceQualityAssist.Runtime;

/// <summary>A synthesis result may commit only while its original source is still current.</summary>
public sealed class VoiceApplyLease : IDisposable
{
    readonly VoiceItem voice;
    readonly Func<bool>? isCurrentTarget;
    readonly string? serif;
    readonly string? hatsuon;
    readonly string? characterName;
    readonly object? character;
    readonly object? parameter;
    readonly string? speakerId;
    readonly string? speakerApi;
    readonly EffectInput[] effects;
    readonly List<INotifyPropertyChanged> subscriptions = [];
    string? outputPath;
    object? outputPronounce;
    bool outputPinned;
    int invalidated;

    public VoiceApplyLease(VoiceItem voice, Func<bool>? isCurrentTarget = null)
    {
        this.voice = voice ?? throw new ArgumentNullException(nameof(voice));
        this.isCurrentTarget = isCurrentTarget;
        serif = voice.Serif;
        hatsuon = voice.Hatsuon;
        characterName = voice.CharacterName;
        character = voice.Character;
        parameter = voice.VoiceParameter;
        speakerId = voice.Character?.Voice?.Speaker?.ID;
        speakerApi = voice.Character?.Voice?.Speaker?.API;
        effects = ReadEffects(voice);
        Subscribe(voice, OnVoiceChanged);
        Subscribe(parameter, OnInputChanged);
        foreach (var effect in effects)
            Subscribe(effect.Effect, OnInputChanged);
    }

    public bool IsCurrent =>
        Volatile.Read(ref invalidated) == 0
        && (isCurrentTarget?.Invoke() ?? true)
        && voice.Serif == serif && voice.Hatsuon == hatsuon
        && voice.CharacterName == characterName
        && ReferenceEquals(voice.Character, character)
        && ReferenceEquals(voice.VoiceParameter, parameter)
        && voice.Character?.Voice?.Speaker?.ID == speakerId
        && voice.Character?.Voice?.Speaker?.API == speakerApi
        && ReadEffects(voice).SequenceEqual(effects)
        && (!outputPinned || (voice.FilePath == outputPath
            && ReferenceEquals(voice.Pronounce, outputPronounce)));

    // Normal host generation may create FilePath/Pronounce before detached work starts.
    public void PinOutput(string path)
    {
        outputPath = path;
        outputPronounce = voice.Pronounce;
        outputPinned = true;
    }

    void Subscribe(object? value, PropertyChangedEventHandler handler)
    {
        if (value is not INotifyPropertyChanged notify) return;
        notify.PropertyChanged += handler;
        subscriptions.Add(notify);
    }

    void OnVoiceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName)
            || e.PropertyName is "Serif" or "Hatsuon" or "Character" or "CharacterName"
                or "VoiceParameter"
            || PronunciationAssistSettingsStore
                .IsStorageCollectionProperty(
                    e.PropertyName)
            || (outputPinned && e.PropertyName is "Pronounce" or "FilePath" or "VoiceCache"))
        {
            Interlocked.Exchange(ref invalidated, 1);
        }
    }

    void OnInputChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is IPronunciationAssistSettings
            && string.Equals(
                e.PropertyName,
                nameof(IPronunciationAssistSettings.BoundaryInputToken),
                StringComparison.Ordinal))
        {
            return;
        }

        Interlocked.Exchange(ref invalidated, 1);
    }

    static EffectInput[] ReadEffects(VoiceItem voice) =>
        ReviewAssistEffectCollection.Enumerate(voice)
            .Select(e => new EffectInput(e, e.IsEnabled, e.HelperRulesJson, e.Prosody)).ToArray();

    sealed record EffectInput(IPronunciationAssistSettings Effect, bool Enabled,
        string Rules, ProsodyGesture Prosody);

    public void Dispose()
    {
        Interlocked.Exchange(ref invalidated, 1);
        foreach (var notify in subscriptions)
        {
            notify.PropertyChanged -= OnVoiceChanged;
            notify.PropertyChanged -= OnInputChanged;
        }
        subscriptions.Clear();
    }
}
