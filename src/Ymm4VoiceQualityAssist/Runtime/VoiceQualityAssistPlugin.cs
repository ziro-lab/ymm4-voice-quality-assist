using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using YukkuriMovieMaker.Plugin;
using System.Reflection;
using YukkuriMovieMaker.ViewModels;

namespace Ymm4VoiceQualityAssist.Runtime;

/// <summary>
/// Public-surface startup host.
///
/// A1 does not require the user to open the optional Tool window.
/// The runtime discovers the public MainViewModel / ActiveTimelineViewModel
/// and attaches the derived-state controller to the active timeline.
/// </summary>
public sealed class VoiceQualityAssistPlugin : IPlugin, IDisposable
{
    public string Name => "Voice Quality Assist";

    public void Initialize() =>
        VoiceQualityAssistRuntime.Start();

    public void Dispose() =>
        VoiceQualityAssistRuntime.Stop();
}

internal static class VoiceQualityAssistRuntime
{
    static DispatcherTimer? discoveryTimer;
    static object? mainViewModel;
    static INotifyPropertyChanged? mainNotify;
    static TimelineViewModel? timelineViewModel;
    static PronunciationAssistController? controller;
    static bool started;

    internal static void Start()
    {
        if (started)
            return;

        started = true;

        var dispatcher = Application.Current?.Dispatcher
            ?? Dispatcher.CurrentDispatcher;

        if (!dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(
                new Action(StartOnDispatcher),
                DispatcherPriority.ApplicationIdle);
            return;
        }

        StartOnDispatcher();
    }

    static void StartOnDispatcher()
    {
        if (!started)
            return;

        discoveryTimer ??= new DispatcherTimer(
            DispatcherPriority.Background,
            Application.Current?.Dispatcher
                ?? Dispatcher.CurrentDispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(300),
        };

        discoveryTimer.Tick -= OnDiscoveryTick;
        discoveryTimer.Tick += OnDiscoveryTick;
        discoveryTimer.Start();

        DiscoverMainViewModel();
    }

    static void OnDiscoveryTick(
        object? sender,
        EventArgs e) =>
        DiscoverMainViewModel();

    static void DiscoverMainViewModel()
    {
        if (!started)
            return;

        var current = Application.Current?.Windows
            .Cast<Window>()
            .Select(x => x.DataContext)
            .FirstOrDefault(x =>
                x?.GetType().FullName
                == "YukkuriMovieMaker.ViewModels.MainViewModel");

        if (!ReferenceEquals(current, mainViewModel))
        {
            if (mainNotify is not null)
                mainNotify.PropertyChanged -= OnMainViewModelPropertyChanged;

            mainViewModel = current;
            mainNotify = current as INotifyPropertyChanged;

            if (mainNotify is not null)
                mainNotify.PropertyChanged += OnMainViewModelPropertyChanged;
        }

        if (mainViewModel is null)
            return;

        // MainViewModel itself is internal in YMM4 4.56.1.0, but its
        // ActiveTimelineViewModel getter is public. Keep reflection bounded
        // to that public getter; never traverse private fields here.
        AttachTimeline(TryGetActiveTimelineViewModel(mainViewModel));
    }

    static void OnMainViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (!started || mainViewModel is null)
            return;

        if (string.IsNullOrEmpty(e.PropertyName)
            || e.PropertyName == "ActiveTimelineViewModel")
        {
            AttachTimeline(
                TryGetActiveTimelineViewModel(mainViewModel));
        }
    }

    static TimelineViewModel? TryGetActiveTimelineViewModel(
        object main)
    {
        var property = main.GetType().GetProperty(
            "ActiveTimelineViewModel",
            BindingFlags.Instance | BindingFlags.Public);

        if (property?.GetMethod?.IsPublic != true)
            return null;

        return property.GetValue(main) as TimelineViewModel;
    }

    static void AttachTimeline(
        TimelineViewModel? next)
    {
        if (ReferenceEquals(next, timelineViewModel))
            return;

        controller?.Dispose();
        controller = null;
        timelineViewModel = next;

        if (timelineViewModel is not null)
        {
            controller = new PronunciationAssistController(
                timelineViewModel);
        }
    }

    internal static void Stop()
    {
        started = false;

        discoveryTimer?.Stop();
        discoveryTimer = null;

        if (mainNotify is not null)
            mainNotify.PropertyChanged -= OnMainViewModelPropertyChanged;

        mainNotify = null;
        mainViewModel = null;
        timelineViewModel = null;

        controller?.Dispose();
        controller = null;
    }
}
