using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using Ymm4VoiceQualityAssist.Core;
using Ymm4VoiceQualityAssist.Effects;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.ViewModels;

namespace Ymm4VoiceQualityAssist.Runtime;

/// <summary>
/// Timeline-scoped controller for A1.
///
/// The controller intentionally treats the persisted Serif marker + Assist Effect
/// as source of truth. Generated Pronounce/WAV is derived runtime state.
/// </summary>
public sealed class PronunciationAssistController : IDisposable
{
    static readonly string[] RelevantVoiceProperties =
    [
        nameof(VoiceItem.Serif),
        nameof(VoiceItem.Hatsuon),
        nameof(VoiceItem.JimakuVideoEffects),
        nameof(VoiceItem.Pronounce),
        nameof(VoiceItem.VoiceParameter),
        "Character",
        "CharacterName",
        "FilePath",
    ];

    readonly TimelineViewModel timelineViewModel;
    readonly ZeroPauseApplyService service;
    readonly Dispatcher dispatcher;
    readonly DispatcherTimer rescanTimer;
    readonly Dictionary<VoiceItem, ItemState> states =
        new(ReferenceEqualityComparer.Instance);

    bool disposed;
    bool scanRunning;

    public PronunciationAssistController(
        TimelineViewModel timelineViewModel,
        ZeroPauseApplyService? service = null)
    {
        this.timelineViewModel = timelineViewModel
            ?? throw new ArgumentNullException(nameof(timelineViewModel));
        this.service = service ?? new ZeroPauseApplyService();

        dispatcher = Application.Current?.Dispatcher
            ?? Dispatcher.CurrentDispatcher;

        rescanTimer = new DispatcherTimer(
            DispatcherPriority.Background,
            dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        rescanTimer.Tick += (_, _) =>
        {
            rescanTimer.Stop();
            _ = ReconcileAllAsync();
        };

        timelineViewModel.PropertyChanged += OnTimelineViewModelPropertyChanged;

        QueueScan();
    }

    void OnTimelineViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName)
            || e.PropertyName == nameof(TimelineViewModel.Items))
        {
            QueueScan();
        }
    }

    void QueueScan()
    {
        if (disposed)
            return;

        if (!dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(
                new Action(QueueScan),
                DispatcherPriority.Background);
            return;
        }

        rescanTimer.Stop();
        rescanTimer.Start();
    }

    async Task ReconcileAllAsync()
    {
        if (disposed || scanRunning)
            return;

        scanRunning = true;
        try
        {
            RefreshItemSubscriptions();

            foreach (var pair in states.ToArray())
            {
                if (disposed)
                    return;

                await ReconcileOneAsync(pair.Key, pair.Value);
            }
        }
        finally
        {
            scanRunning = false;
        }
    }

    void RefreshItemSubscriptions()
    {
        IEqualityComparer<VoiceItem> comparer =
            ReferenceEqualityComparer.Instance;

        var current = timelineViewModel.Items
            .Select(x => x.Item)
            .OfType<VoiceItem>()
            .ToHashSet(comparer);

        foreach (var stale in states.Keys
            .Where(x => !current.Contains(x))
            .ToArray())
        {
            states[stale].Dispose();
            states.Remove(stale);
        }

        foreach (var voice in current)
        {
            if (!states.TryGetValue(voice, out var state))
            {
                state = new ItemState(
                    voice,
                    OnVoicePropertyChanged,
                    OnEffectPropertyChanged);
                states.Add(voice, state);
            }

            state.RefreshEffectSubscriptions();
            state.RefreshParameterSubscription();
        }
    }

    void OnVoicePropertyChanged(
        VoiceItem voice,
        ItemState state,
        string? propertyName)
    {
        if (disposed || state.IsReconciling)
            return;

        if (string.IsNullOrEmpty(propertyName)
            || RelevantVoiceProperties.Contains(
                propertyName,
                StringComparer.Ordinal))
        {
            if (propertyName == nameof(VoiceItem.JimakuVideoEffects))
                state.RefreshEffectSubscriptions();

            QueueScan();
        }
    }

    void OnEffectPropertyChanged(
        VoiceItem voice,
        ItemState state,
        string? propertyName)
    {
        if (disposed || state.IsReconciling)
            return;

        if (string.IsNullOrEmpty(propertyName)
            || propertyName == nameof(PronunciationAssistEffect.IsEnabled))
        {
            QueueScan();
        }
    }

    async Task ReconcileOneAsync(
        VoiceItem voice,
        ItemState state)
    {
        if (state.IsReconciling)
            return;

        var hasEffect = service.HasAssistEffect(voice);
        var enabled = service.IsAssistEnabled(voice);
        var markers = service.ParseMarkers(voice);
        var hasMarkers = markers.ZeroWaitPositions.Count > 0;

        if (!hasEffect)
        {
            if (state.HadAssistEffect || state.WasCorrected)
                await RestoreBaselineAsync(voice, state);

            state.HadAssistEffect = false;
            state.LastSourceKey = null;
            return;
        }

        state.HadAssistEffect = true;

        if (!enabled || !hasMarkers)
        {
            // Presence + disabled, or enabled with no durable marker, means
            // ordinary YMM4 audio. This also clears stale corrected audio
            // after marker removal.
            await RestoreBaselineAsync(voice, state);
            state.LastSourceKey = BuildSourceKey(voice);
            return;
        }

        var sourceKey = BuildSourceKey(voice);
        if (state.WasCorrected
            && string.Equals(
                state.LastSourceKey,
                sourceKey,
                StringComparison.Ordinal)
            && ReferenceEquals(
                voice.Pronounce,
                state.LastAppliedPronounce))
        {
            return;
        }

        state.IsReconciling = true;
        try
        {
            var result = await service.ApplyAsync(voice);

            if (result.IsApplied)
            {
                state.WasCorrected = true;
                state.LastSourceKey = sourceKey;
                state.LastAppliedPronounce = voice.Pronounce;
                state.LastResult = result;
                return;
            }

            // Fail closed. If the marker can no longer be resolved exactly
            // (manual Hatsuon edit, provider change, ambiguous phrase, etc.),
            // do not leave a previously corrected WAV behind.
            var baseline = await service.RestoreBaselineAsync(voice);
            state.WasCorrected = false;
            state.LastSourceKey = sourceKey;
            state.LastAppliedPronounce = voice.Pronounce;
            state.LastResult = result.Status == AssistApplyStatus.NoMarkers
                ? baseline
                : result;
        }
        finally
        {
            state.IsReconciling = false;
        }
    }

    async Task RestoreBaselineAsync(
        VoiceItem voice,
        ItemState state)
    {
        state.IsReconciling = true;
        try
        {
            var result = await service.RestoreBaselineAsync(voice);
            if (result.IsBaseline)
            {
                state.WasCorrected = false;
                state.LastAppliedPronounce = voice.Pronounce;
            }
            state.LastResult = result;
        }
        finally
        {
            state.IsReconciling = false;
        }
    }

    static string BuildSourceKey(VoiceItem voice)
    {
        var speaker = voice.Character?.Voice?.Speaker;

        return string.Join(
            "",
            voice.Serif ?? string.Empty,
            voice.Hatsuon ?? string.Empty,
            speaker?.API ?? string.Empty,
            speaker?.ID ?? string.Empty,
            RuntimeHelpers.GetHashCode(
                (object?)voice.VoiceParameter ?? voice));
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        rescanTimer.Stop();

        timelineViewModel.PropertyChanged -= OnTimelineViewModelPropertyChanged;

        foreach (var state in states.Values)
            state.Dispose();
        states.Clear();

        GC.SuppressFinalize(this);
    }

    sealed class ItemState : IDisposable
    {
        readonly VoiceItem voice;
        readonly Action<VoiceItem, ItemState, string?> voiceChanged;
        readonly Action<VoiceItem, ItemState, string?> effectChanged;
        readonly HashSet<INotifyPropertyChanged> effectSubscriptions =
            new(ReferenceEqualityComparer.Instance);

        INotifyPropertyChanged? parameterSubscription;

        public ItemState(
            VoiceItem voice,
            Action<VoiceItem, ItemState, string?> voiceChanged,
            Action<VoiceItem, ItemState, string?> effectChanged)
        {
            this.voice = voice;
            this.voiceChanged = voiceChanged;
            this.effectChanged = effectChanged;

            if (voice is INotifyPropertyChanged notify)
                notify.PropertyChanged += OnVoicePropertyChanged;
        }

        public bool IsReconciling { get; set; }
        public bool HadAssistEffect { get; set; }
        public bool WasCorrected { get; set; }
        public string? LastSourceKey { get; set; }
        public object? LastAppliedPronounce { get; set; }
        public AssistApplyResult? LastResult { get; set; }

        public void RefreshEffectSubscriptions()
        {
            IEqualityComparer<INotifyPropertyChanged> comparer =
                ReferenceEqualityComparer.Instance;

            var current = EnumerateEffects(voice)
                .OfType<INotifyPropertyChanged>()
                .ToHashSet(comparer);

            foreach (var old in effectSubscriptions
                .Where(x => !current.Contains(x))
                .ToArray())
            {
                old.PropertyChanged -= OnEffectPropertyChanged;
                effectSubscriptions.Remove(old);
            }

            foreach (var item in current)
            {
                if (effectSubscriptions.Add(item))
                    item.PropertyChanged += OnEffectPropertyChanged;
            }
        }

        public void RefreshParameterSubscription()
        {
            var current = voice.VoiceParameter as INotifyPropertyChanged;
            if (ReferenceEquals(current, parameterSubscription))
                return;

            if (parameterSubscription is not null)
                parameterSubscription.PropertyChanged -= OnParameterPropertyChanged;

            parameterSubscription = current;

            if (parameterSubscription is not null)
                parameterSubscription.PropertyChanged += OnParameterPropertyChanged;
        }

        void OnVoicePropertyChanged(
            object? sender,
            PropertyChangedEventArgs e) =>
            voiceChanged(voice, this, e.PropertyName);

        void OnEffectPropertyChanged(
            object? sender,
            PropertyChangedEventArgs e) =>
            effectChanged(voice, this, e.PropertyName);

        void OnParameterPropertyChanged(
            object? sender,
            PropertyChangedEventArgs e)
        {
            if (!IsReconciling)
                voiceChanged(voice, this, nameof(VoiceItem.VoiceParameter));
        }

        static IEnumerable<object> EnumerateEffects(VoiceItem voice)
        {
            if (voice.JimakuVideoEffects is not System.Collections.IEnumerable effects)
                yield break;

            foreach (var item in effects)
            {
                if (item is not null)
                    yield return item;
            }
        }

        public void Dispose()
        {
            if (voice is INotifyPropertyChanged notify)
                notify.PropertyChanged -= OnVoicePropertyChanged;

            foreach (var effect in effectSubscriptions)
                effect.PropertyChanged -= OnEffectPropertyChanged;
            effectSubscriptions.Clear();

            if (parameterSubscription is not null)
                parameterSubscription.PropertyChanged -= OnParameterPropertyChanged;
            parameterSubscription = null;
        }
    }
}
