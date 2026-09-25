using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;
using Ymm4VoiceQualityAssist.Core;
using Ymm4VoiceQualityAssist.Effects;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Voice;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.UndoRedo;
using YukkuriMovieMaker.Voice;

namespace Ymm4VoiceQualityAssistB3NativeProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name =>
        "Voice Quality Assist B3 Native Probe";

    public void SetCulture(
        CultureInfo cultureInfo) =>
        Probe.Schedule();
}

internal static class Probe
{
    static bool scheduled;
    static string output =
        string.Empty;

    static readonly List<object>
        requirements = [];

    internal static void Schedule()
    {
        var dir =
            Environment.GetEnvironmentVariable(
                "VQA_B3_NATIVE_OUTPUT");

        var url =
            Environment.GetEnvironmentVariable(
                "VQA_B3_FAKE_VOICEVOX_URL");

        if (scheduled
            || string.IsNullOrWhiteSpace(dir)
            || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        scheduled = true;
        output =
            Path.GetFullPath(dir);

        Directory.CreateDirectory(
            output);

        Application.Current.Dispatcher.BeginInvoke(
            new Action(
                () => Start(url)),
            DispatcherPriority.ApplicationIdle);
    }

    static void Start(
        string url)
    {
        var ticks = 0;
        var created = false;

        var timer =
            new DispatcherTimer(
                DispatcherPriority.ApplicationIdle)
            {
                Interval =
                    TimeSpan.FromMilliseconds(300),
            };

        timer.Tick +=
            async (_, _) =>
            {
                try
                {
                    ticks++;

                    var main =
                        Application.Current.Windows
                            .Cast<Window>()
                            .Select(x =>
                                x.DataContext)
                            .FirstOrDefault(x =>
                                x?.GetType().FullName
                                == "YukkuriMovieMaker.ViewModels.MainViewModel");

                    if (main is null)
                        return;

                    var active =
                        main.GetType()
                            .GetProperty(
                                "ActiveTimelineViewModel",
                                BindingFlags.Instance
                                | BindingFlags.Public
                                | BindingFlags.NonPublic)
                            ?.GetValue(main);

                    if (active is null)
                    {
                        if (!created)
                        {
                            created = true;

                            main.GetType()
                                .GetMethod(
                                    "CreateProject",
                                    Type.EmptyTypes)
                                ?.Invoke(
                                    main,
                                    null);
                        }

                        if (ticks > 160)
                        {
                            throw new TimeoutException(
                                "ActiveTimelineViewModel was not created.");
                        }

                        return;
                    }

                    timer.Stop();

                    await RunAsync(
                        main,
                        active,
                        url);

                    Write(
                        "PASS_B3_IMPORT_PRODUCT_NATIVE_SMOKE",
                        null);
                }
                catch (Exception ex)
                {
                    timer.Stop();

                    Write(
                        "FAIL_B3_IMPORT_PRODUCT_NATIVE_SMOKE",
                        ex.ToString());
                }
            };

        timer.Start();
    }

    static async Task RunAsync(
        object main,
        object active,
        string url)
    {
        var timeline =
            FindTimeline(active)
            ?? throw new InvalidOperationException(
                "Timeline could not be resolved.");

        Check(
            "timeline_resolved",
            true);

        var manager =
            AcquireUndoManager(main)
            ?? throw new InvalidOperationException(
                "UndoRedoManager was null.");

        Check(
            "undo_manager_resolved",
            true);

        var engine =
            new VOICEVOXEngine(
                new VOICEVOXEngineContext())
            {
                Name =
                    "VQA B3 VOICEVOX",
                URL =
                    url,
                Path =
                    string.Empty,
                Timeout =
                    10_000,
            };

        const string speakerUuid =
            "11111111-1111-1111-1111-111111111111";

        engine.SpeakerInfos.Add(
            new VOICEVOXSpeakerInfo(
                speakerUuid,
                string.Empty));

        var speakerJson =
            JObject.Parse(
                """
                {
                  "name": "VQA B3",
                  "speaker_uuid": "11111111-1111-1111-1111-111111111111",
                  "styles": [
                    {
                      "name": "Normal",
                      "id": 1,
                      "type": "talk"
                    }
                  ],
                  "version": "0.0.0",
                  "supported_features": {
                    "permitted_synthesis_morphing": "SELF_ONLY"
                  }
                }
                """);

        engine.SpeakersJsonCache =
            new JArray(
                speakerJson)
                .ToString(
                    Newtonsoft.Json
                        .Formatting.None);

        var vvCharacter =
            new VOICEVOXCharacter(
                speakerJson,
                Array.Empty<
                    VOICEVOXSpeakerInfo>(),
                false);

        var speakerType =
            typeof(VOICEVOXEngine)
                .Assembly
                .GetType(
                    "YukkuriMovieMaker.Voice.VOICEVOXVoiceSpeaker")
            ?? throw new TypeLoadException(
                "VOICEVOXVoiceSpeaker");

        var speakerObject =
            Activator.CreateInstance(
                speakerType,
                engine,
                vvCharacter)
            ?? throw new InvalidOperationException(
                "VOICEVOXVoiceSpeaker construction failed.");

        if (speakerObject
            is not IVoiceSpeaker speaker)
        {
            throw new InvalidOperationException(
                "Built-in VOICEVOX speaker interface missing.");
        }

        var registration =
            RegisterEngineInYmmSettings(
                engine,
                speaker.ID);

        try
        {
            Check(
                "fake_engine_registered",
                registration.ResolvedEngine
                    is not null
                && ReferenceEquals(
                    registration.ResolvedEngine,
                    engine));

            var parameter =
                speaker.CreateVoiceParameter();

            parameter
                .GetType()
                .GetProperty(
                    "StyleID",
                    BindingFlags.Instance
                    | BindingFlags.Public)
                ?.SetValue(
                    parameter,
                    1);

            var character =
                new Character
                {
                    Name =
                        "VQA B3",
                    Voice =
                        new VoiceDescription(
                            speaker),
                    VoiceParameter =
                        parameter,
                };

            var selected =
                CreateVoice(
                    character,
                    parameter,
                    "えええ",
                    "エエエ",
                    "B3_SELECTED");

            var stale =
                CreateVoice(
                    character,
                    parameter,
                    "ええ",
                    "エエ",
                    "B3_STALE");

            Check(
                "selected_voice_added",
                timeline.TryAddItems(
                    [selected],
                    120,
                    3));

            Check(
                "stale_voice_added",
                timeline.TryAddItems(
                    [stale],
                    360,
                    5));

            await selected
                .CreateVoiceFileAsync();

            await stale
                .CreateVoiceFileAsync();

            var selectedPath =
                RequireVoicePath(
                    selected,
                    "selected");

            var stalePath =
                RequireVoicePath(
                    stale,
                    "stale");

            Check(
                "baseline_files_created",
                new FileInfo(
                    selectedPath).Length
                    == 4844
                && new FileInfo(
                    stalePath).Length
                    == 4844);

            var exported =
                ReviewExportBuilder.Build(
                    [selected, stale],
                    "b3-native-session",
                    DateTimeOffset.UnixEpoch);

            Check(
                "review_export_built",
                exported.IsSuccess
                && exported.Session is not null
                && exported.Session
                    .Package.Voices.Count
                    == 2);

            var session =
                exported.Session
                ?? throw new InvalidOperationException(
                    "Review export session missing.");

            var selectedRecord =
                session.Package.Voices[0];

            var staleRecord =
                session.Package.Voices[1];

            var wire =
                new ReviewCorrectionWirePackage(
                    ReviewCorrectionValidator.Schema,
                    session.Package
                        .ExportSessionId,
                    [
                        new ReviewCorrectionWireRecord(
                            selectedRecord
                                .Target.ExportRef,
                            selectedRecord
                                .SourceFingerprint,
                            [
                                new ReviewCorrectionWireOperation(
                                    "addBoundary",
                                    Position: 1),
                                new ReviewCorrectionWireOperation(
                                    "helperVowelZero",
                                    Position: 2,
                                    Helper: "ウ"),
                                new ReviewCorrectionWireOperation(
                                    "setProsodyGesture",
                                    Gesture: "hold"),
                            ]),
                        new ReviewCorrectionWireRecord(
                            staleRecord
                                .Target.ExportRef,
                            staleRecord
                                .SourceFingerprint,
                            [
                                new ReviewCorrectionWireOperation(
                                    "setReading",
                                    Reading:
                                        "カワッタ"),
                            ]),
                    ]);

            var correctionJson =
                ReviewCorrectionJson.Serialize(
                    wire);

            var decoded =
                ReviewCorrectionJson
                    .DecodeAndValidateAgainstExport(
                        correctionJson,
                        session.Package);

            Check(
                "correction_package_valid",
                decoded.IsSuccess);

            stale.Hatsuon =
                "STALE";

            var plan =
                ReviewImportPlanner.Build(
                    session.Package,
                    decoded,
                    [selected, stale],
                    session);

            Check(
                "import_plan_built",
                plan.IsSuccess
                && plan.Items.Count == 2);

            var selectedPlan =
                plan.Items.Single(x =>
                    x.ExportRecord
                        .Target.ExportRef
                    == selectedRecord
                        .Target.ExportRef);

            var stalePlan =
                plan.Items.Single(x =>
                    x.ExportRecord
                        .Target.ExportRef
                    == staleRecord
                        .Target.ExportRef);

            Check(
                "selected_exact_session",
                selectedPlan
                    .ResolutionStatus
                == ReviewImportResolutionStatus
                    .ExactSessionMatch
                && selectedPlan.CanApply);

            Check(
                "changed_voice_is_stale",
                stalePlan
                    .ResolutionStatus
                == ReviewImportResolutionStatus
                    .Stale
                && !stalePlan.CanApply);

            var rejectBatch =
                ReviewImportApplier.Prepare(
                    plan,
                    [
                        selectedRecord
                            .Target.ExportRef,
                        staleRecord
                            .Target.ExportRef,
                    ]);

            Check(
                "stale_selection_rejected",
                rejectBatch.Status
                == ReviewImportPrepareStatus
                    .InvalidSelection
                && selected.Serif
                    == "えええ"
                && selected.Hatsuon
                    == "エエエ"
                && ReviewAssistEffectCollection
                    .Enumerate(selected)
                    .Count == 0);

            var prepared =
                ReviewImportApplier.Prepare(
                    plan,
                    [
                        selectedRecord
                            .Target.ExportRef,
                    ]);

            Check(
                "exact_selection_prepared",
                prepared.IsSuccess
                && prepared.Journal
                    is not null);

            var journal =
                prepared.Journal
                ?? throw new InvalidOperationException(
                    "Import journal missing.");

            manager.Record();

            var recorded = 0;
            var undoed = 0;
            var redoed = 0;

            EventHandler recordedHandler =
                (_, _) => recorded++;

            EventHandler undoedHandler =
                (_, _) => undoed++;

            EventHandler redoedHandler =
                (_, _) => redoed++;

            manager.Recorded +=
                recordedHandler;

            manager.Undoed +=
                undoedHandler;

            manager.Redoed +=
                redoedHandler;

            PronunciationAssistAudioEffect? typedUiEffect = null;

            try
            {
                var commit =
                    journal.Commit();

                Check(
                    "journal_committed",
                    commit.IsSuccess);

                manager.AddCommand(
                    new UndoRedoActionCommand(
                        journal.UndoOrThrow,
                        journal.RedoOrThrow));

                manager.Record();

                Check(
                    "import_recorded_once",
                    recorded == 1);

                Check(
                    "durable_source_applied",
                    selected.Serif
                        == "え<w0>ええ"
                    && selected.Hatsuon
                        == "エエエ"
                    && stale.Hatsuon
                        == "STALE");

                var effect =
                    ReviewAssistEffectCollection
                        .Enumerate(selected)
                        .SingleOrDefault(x =>
                            x.IsEnabled)
                    ?? throw new InvalidOperationException(
                        "Enabled Assist Effect missing after B3 apply.");

                typedUiEffect =
                    effect as PronunciationAssistAudioEffect
                    ?? throw new InvalidOperationException(
                        "B3 newly-created Assist Effect was not the canonical Audio Effect.");

                Check(
                    "assist_effect_created",
                    effect.Prosody
                        == ProsodyGesture.Hold
                    && HelperRuleCodec.TryDecode(
                        effect.HelperRulesJson,
                        out var rules,
                        out _)
                    && rules.Rules.Count == 1
                    && rules.Rules[0].Kind
                        == HelperMoraKind.ZeroVowel
                    && rules.Rules[0].Helper
                        == "ウ"
                    && rules.Rules[0]
                        .Anchor.Position
                        == 2);

                Check(
                    "assist_new_write_uses_audio_effects",
                    selected.AudioEffects
                        .Cast<object>()
                        .Any(x =>
                            ReferenceEquals(
                                x,
                                effect)));

                Check(
                    "assist_new_write_skips_legacy_collection",
                    !selected.JimakuVideoEffects
                        .Cast<object>()
                        .Any(x =>
                            ReferenceEquals(
                                x,
                                effect)));

                await WaitUntil(
                    "Track A regeneration after import",
                    () =>
                        File.Exists(
                            selectedPath)
                        && new FileInfo(
                            selectedPath).Length
                            == 6644,
                    35_000);

                var correctedHash =
                    HashFile(
                        selectedPath);

                Check(
                    "track_a_regenerated_corrected_audio",
                    new FileInfo(
                        selectedPath).Length
                        == 6644);

                Check(
                    "stale_voice_not_applied",
                    stale.Hatsuon
                        == "STALE"
                    && new FileInfo(
                        stalePath).Length
                        == 4844);

                await manager.UndoAsync();

                await WaitUntil(
                    "B3 undo baseline",
                    () =>
                        selected.Serif
                            == "えええ"
                        && selected.Hatsuon
                            == "エエエ"
                        && ReviewAssistEffectCollection
                            .Enumerate(
                                selected)
                            .Count == 0
                        && File.Exists(
                            selectedPath)
                        && new FileInfo(
                            selectedPath).Length
                            == 4844,
                    35_000);

                Check(
                    "undo_restores_durable_source",
                    selected.Serif
                        == "えええ"
                    && selected.Hatsuon
                        == "エエエ"
                    && ReviewAssistEffectCollection
                        .Enumerate(
                            selected)
                        .Count == 0);

                Check(
                    "undo_regenerates_baseline_audio",
                    new FileInfo(
                        selectedPath).Length
                        == 4844
                    && undoed == 1);

                await manager.RedoAsync();

                await WaitUntil(
                    "B3 redo corrected",
                    () =>
                        selected.Serif
                            == "え<w0>ええ"
                        && ReviewAssistEffectCollection
                            .Enumerate(
                                selected)
                            .Any(x =>
                                x.IsEnabled
                                && x.Prosody
                                    == ProsodyGesture.Hold)
                        && File.Exists(
                            selectedPath)
                        && new FileInfo(
                            selectedPath).Length
                            == 6644,
                    35_000);

                Check(
                    "redo_restores_durable_source",
                    selected.Serif
                        == "え<w0>ええ"
                    && selected.Hatsuon
                        == "エエエ");

                Check(
                    "redo_regenerates_same_corrected_audio",
                    new FileInfo(
                        selectedPath).Length
                        == 6644
                    && HashFile(
                        selectedPath)
                        == correctedHash
                    && redoed == 1);

                File.WriteAllText(
                    Path.Combine(
                        output,
                        "b3-import-observation.json"),
                    JsonSerializer.Serialize(
                        new
                        {
                            selected = new
                            {
                                selected.Serif,
                                selected.Hatsuon,
                                fileLength =
                                    new FileInfo(
                                        selectedPath).Length,
                                fileSha256 =
                                    HashFile(
                                        selectedPath),
                            },
                            stale = new
                            {
                                stale.Serif,
                                stale.Hatsuon,
                                fileLength =
                                    new FileInfo(
                                        stalePath).Length,
                            },
                            history = new
                            {
                                recorded,
                                undoed,
                                redoed,
                            },
                            plan = plan.Items
                                .Select(x =>
                                    new
                                    {
                                        exportRef =
                                            x.ExportRecord
                                                .Target.ExportRef,
                                        status =
                                            x.ResolutionStatus
                                                .ToString(),
                                        x.CanApply,
                                        x.SelectedByDefault,
                                    })
                                .ToArray(),
                        },
                        new JsonSerializerOptions
                        {
                            WriteIndented = true,
                        }));
            }
            finally
            {
                manager.Recorded -=
                    recordedHandler;

                manager.Undoed -=
                    undoedHandler;

                manager.Redoed -=
                    redoedHandler;
            }

            await RunTypedSettingsUiLifecycleAsync(
                timeline,
                main,
                selected,
                typedUiEffect
                    ?? throw new InvalidOperationException(
                        "Typed UI target effect was unavailable."),
                manager);

            await RunLegacyMigrationLifecycleAsync(
                timeline,
                character,
                parameter,
                manager);
        }
        finally
        {
            registration.Restore();
        }
    }

    static async Task RunTypedSettingsUiLifecycleAsync(
        Timeline timeline,
        object main,
        VoiceItem voice,
        PronunciationAssistAudioEffect effect,
        UndoRedoManager manager)
    {
        timeline.CurrentFrame =
            voice.Frame;

        timeline.SelectItems(
            [voice]);

        await WaitUntil(
            "typed UI VoiceItem selection",
            () =>
                timeline.SelectedItems
                    .Any(x =>
                        ReferenceEquals(
                            x,
                            voice)),
            5_000);

        var selectionAttempted =
            timeline.SelectedItems
                .Any(x =>
                    ReferenceEquals(
                        x,
                        voice));

        // Keep the ViewModel route only as a compatibility fallback if a
        // future host stops updating Item Editor from the public Timeline API.
        if (!selectionAttempted)
        {
            selectionAttempted =
                SelectVoiceInItemEditorFallback(
                    main,
                    voice);
        }

        await Task.Delay(500);

        RealizeItemEditor();

        await Task.Delay(300);

        WriteTypedUiDiagnostic(
            "before-effect-selection",
            effect);

        var effectSelected =
            SelectAudioEffectInEditor(
                effect);

        WriteTypedUiDiagnostic(
            "after-effect-selection",
            effect);

        await Task.Delay(500);

        RealizeItemEditor();

        await Task.Delay(350);

        var visible =
            Application.Current.Windows
                .Cast<Window>()
                .SelectMany(
                    EnumerateVisual)
                .OfType<FrameworkElement>()
                .Where(x =>
                    x.IsVisible)
                .ToArray();

        var texts =
            visible
                .Select(GetText)
                .OfType<string>()
                .Where(x =>
                    !string.IsNullOrWhiteSpace(
                        x))
                .Distinct(
                    StringComparer.Ordinal)
                .ToArray();

        var helperEditor =
            visible.FirstOrDefault(x =>
                string.Equals(
                    AutomationProperties
                        .GetAutomationId(x),
                    "VqaHelperRulesEditor",
                    StringComparison.Ordinal));

        var helperText =
            visible.OfType<TextBox>()
                .FirstOrDefault(x =>
                    string.Equals(
                        AutomationProperties
                            .GetAutomationId(x),
                        "VqaHelperNewText",
                        StringComparison.Ordinal));

        var helperKind =
            visible.OfType<ComboBox>()
                .FirstOrDefault(x =>
                    string.Equals(
                        AutomationProperties
                            .GetAutomationId(x),
                        "VqaHelperNewKind",
                        StringComparison.Ordinal));

        var helperPosition =
            visible.OfType<TextBox>()
                .FirstOrDefault(x =>
                    string.Equals(
                        AutomationProperties
                            .GetAutomationId(x),
                        "VqaHelperNewPosition",
                        StringComparison.Ordinal));

        var helperAdd =
            visible.OfType<Button>()
                .FirstOrDefault(x =>
                    string.Equals(
                        AutomationProperties
                            .GetAutomationId(x),
                        "VqaHelperNewAdd",
                        StringComparison.Ordinal));

        Check(
            "typed_ui_voice_selected",
            selectionAttempted);

        Check(
            "typed_ui_audio_effect_selected",
            effectSelected);

        Check(
            "typed_ui_helper_editor_visible",
            helperEditor is not null
            && helperText is not null
            && helperKind is not null
            && helperPosition is not null
            && helperAdd is not null
            && texts.Any(x =>
                x.Contains(
                    "補助文字",
                    StringComparison.Ordinal))
            && texts.Any(x =>
                x.Contains(
                    "本文位置",
                    StringComparison.Ordinal))
            && texts.Any(x =>
                x.Contains(
                    "新しい補助モーラ",
                    StringComparison.Ordinal)));

        Check(
            "typed_ui_prosody_editor_visible",
            texts.Any(x =>
                string.Equals(
                    x,
                    "抑揚",
                    StringComparison.Ordinal))
            && texts.Any(x =>
                x.Contains(
                    "平らに寄せる",
                    StringComparison.Ordinal)));

        var beforeJson =
            effect.HelperRulesJson;

        Check(
            "typed_ui_raw_json_hidden",
            !visible.OfType<TextBox>()
                .Any(x =>
                    string.Equals(
                        x.Text,
                        beforeJson,
                        StringComparison.Ordinal)
                    || x.Text.Contains(
                        "\"version\"",
                        StringComparison.Ordinal)));

        HelperRuleCodec.TryDecode(
            beforeJson,
            out var beforeRules,
            out _);

        Check(
            "typed_ui_initial_helper_rule_visible",
            beforeRules.Rules.Count == 1
            && texts.Any(x =>
                x.Contains(
                    "ルール 1",
                    StringComparison.Ordinal)));

        manager.Record();

        var recorded = 0;
        var undoed = 0;
        var redoed = 0;

        EventHandler recordedHandler =
            (_, _) =>
                recorded++;

        EventHandler undoedHandler =
            (_, _) =>
                undoed++;

        EventHandler redoedHandler =
            (_, _) =>
                redoed++;

        manager.Recorded +=
            recordedHandler;
        manager.Undoed +=
            undoedHandler;
        manager.Redoed +=
            redoedHandler;

        try
        {
            helperText!.Text =
                "ア";

            helperKind!.SelectedValue =
                HelperMoraKind.ZeroVowel;

            helperPosition!.Text =
                "3";

            helperAdd!.RaiseEvent(
                new RoutedEventArgs(
                    Button.ClickEvent));

            await WaitUntil(
                "typed helper UI add",
                () =>
                {
                    if (!HelperRuleCodec.TryDecode(
                        effect.HelperRulesJson,
                        out var decoded,
                        out _))
                    {
                        return false;
                    }

                    return decoded.Rules.Count == 2
                        && decoded.Rules.Any(x =>
                            x.Helper == "ア"
                            && x.Kind
                                == HelperMoraKind.ZeroVowel
                            && x.Anchor.Position == 3);
                },
                10_000);

            Check(
                "typed_ui_helper_edit_applied",
                recorded == 1);

            if (recorded != 1)
            {
                throw new InvalidOperationException(
                    "Typed helper editor did not create exactly one YMM4 undo record.");
            }

            await manager.UndoAsync();

            await WaitUntil(
                "typed helper UI undo",
                () =>
                    string.Equals(
                        effect.HelperRulesJson,
                        beforeJson,
                        StringComparison.Ordinal),
                10_000);

            Check(
                "typed_ui_helper_undo",
                undoed == 1);

            await manager.RedoAsync();

            await WaitUntil(
                "typed helper UI redo",
                () =>
                {
                    if (!HelperRuleCodec.TryDecode(
                        effect.HelperRulesJson,
                        out var decoded,
                        out _))
                    {
                        return false;
                    }

                    return decoded.Rules.Count == 2
                        && decoded.Rules.Any(x =>
                            x.Helper == "ア"
                            && x.Anchor.Position == 3);
                },
                10_000);

            Check(
                "typed_ui_helper_redo",
                redoed == 1
                && effect.Prosody
                    == ProsodyGesture.Hold);

            File.WriteAllText(
                Path.Combine(
                    output,
                    "typed-settings-ui-observation.json"),
                JsonSerializer.Serialize(
                    new
                    {
                        selectionAttempted,
                        effectSelected,
                        visibleLabels =
                            texts.Where(x =>
                                x.Contains(
                                    "補助",
                                    StringComparison.Ordinal)
                                || x.Contains(
                                    "抑揚",
                                    StringComparison.Ordinal)
                                || x.Contains(
                                    "母音",
                                    StringComparison.Ordinal)
                                || x.Contains(
                                    "本文位置",
                                    StringComparison.Ordinal)
                                || x.Contains(
                                    "平ら",
                                    StringComparison.Ordinal))
                            .Take(80)
                            .ToArray(),
                        recorded,
                        undoed,
                        redoed,
                        finalHelperRulesJson =
                            effect.HelperRulesJson,
                    },
                    new JsonSerializerOptions
                    {
                        WriteIndented = true,
                    }));
        }
        finally
        {
            manager.Recorded -=
                recordedHandler;
            manager.Undoed -=
                undoedHandler;
            manager.Redoed -=
                redoedHandler;
        }
    }

    static bool SelectVoiceInItemEditorFallback(
        object main,
        VoiceItem voice)
    {
        var active =
            main.GetType()
                .GetProperty(
                    "ActiveTimelineViewModel",
                    BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic)
                ?.GetValue(main);

        if (active is null)
            return false;

        var items =
            active.GetType()
                .GetProperty(
                    "Items",
                    BindingFlags.Instance
                    | BindingFlags.Public)
                ?.GetValue(active)
            as IEnumerable;

        if (items is null)
            return false;

        var vm =
            items.Cast<object>()
                .FirstOrDefault(x =>
                    ReferenceEquals(
                        x.GetType()
                            .GetProperty(
                                "Item",
                                BindingFlags.Instance
                                | BindingFlags.Public)
                            ?.GetValue(x),
                        voice));

        if (vm is null)
            return false;

        var attempted =
            false;

        foreach (var property
            in vm.GetType()
                .GetProperties(
                    BindingFlags.Instance
                    | BindingFlags.Public)
                .Where(x =>
                    x.Name.Contains(
                        "Select",
                        StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                if (property.PropertyType
                    == typeof(bool)
                    && property.SetMethod?.IsPublic
                        == true)
                {
                    property.SetValue(
                        vm,
                        true);

                    attempted =
                        true;
                }

                if (typeof(ICommand)
                    .IsAssignableFrom(
                        property.PropertyType)
                    && property.GetValue(vm)
                        is ICommand command)
                {
                    foreach (var parameter
                        in new object?[]
                        {
                            null,
                            vm,
                            voice,
                        })
                    {
                        if (!command.CanExecute(
                            parameter))
                        {
                            continue;
                        }

                        command.Execute(
                            parameter);

                        attempted =
                            true;

                        break;
                    }
                }
            }
            catch
            {
            }
        }

        foreach (var method
            in active.GetType()
                .GetMethods(
                    BindingFlags.Instance
                    | BindingFlags.Public)
                .Where(x =>
                    x.Name.Contains(
                        "Select",
                        StringComparison.OrdinalIgnoreCase)
                    && x.GetParameters()
                        .Length == 1))
        {
            try
            {
                var parameterType =
                    method.GetParameters()[0]
                        .ParameterType;

                if (parameterType
                    .IsInstanceOfType(vm))
                {
                    method.Invoke(
                        active,
                        [vm]);

                    attempted =
                        true;
                }
                else if (parameterType
                    .IsInstanceOfType(voice))
                {
                    method.Invoke(
                        active,
                        [voice]);

                    attempted =
                        true;
                }
            }
            catch
            {
            }
        }

        return attempted;
    }

    static void RealizeItemEditor()
    {
        foreach (Window window
            in Application.Current.Windows)
        {
            foreach (var viewer
                in EnumerateVisual(window)
                    .OfType<ScrollViewer>())
            {
                try
                {
                    if (viewer.IsVisible
                        && viewer.ScrollableHeight > 0)
                    {
                        viewer.ScrollToVerticalOffset(
                            viewer.ScrollableHeight);

                        viewer.UpdateLayout();
                    }
                }
                catch
                {
                }
            }

            foreach (var expander
                in EnumerateVisual(window)
                    .OfType<Expander>())
            {
                var header =
                    expander.Header
                        ?.ToString()
                    ?? string.Empty;

                if (header.Contains(
                        "発音補助",
                        StringComparison.Ordinal)
                    || header.Contains(
                        "音声",
                        StringComparison.Ordinal)
                    || header.Contains(
                        "Audio",
                        StringComparison.OrdinalIgnoreCase)
                    || header.Contains(
                        "エフェクト",
                        StringComparison.Ordinal)
                    || header.Contains(
                        "Effect",
                        StringComparison.OrdinalIgnoreCase))
                {
                    expander.IsExpanded =
                        true;

                    try
                    {
                        expander.UpdateLayout();
                    }
                    catch
                    {
                    }
                }
            }
        }
    }

    static void WriteTypedUiDiagnostic(
        string phase,
        PronunciationAssistAudioEffect effect)
    {
        try
        {
            var windows =
                Application.Current.Windows
                    .Cast<Window>()
                    .ToArray();

            var visual =
                windows
                    .SelectMany(
                        EnumerateVisual)
                    .OfType<FrameworkElement>()
                    .ToArray();

            object DescribeElement(
                FrameworkElement element) =>
                new
                {
                    type =
                        element.GetType()
                            .FullName,
                    element.Name,
                    visible =
                        element.IsVisible,
                    text =
                        GetText(element),
                    dataContextType =
                        element.DataContext
                            ?.GetType()
                            .FullName,
                    dataContextIsTarget =
                        ReferenceEquals(
                            element.DataContext,
                            effect),
                    automationId =
                        AutomationProperties
                            .GetAutomationId(
                                element),
                    element.ActualWidth,
                    element.ActualHeight,
                };

            var listBoxes =
                visual
                    .OfType<ListBox>()
                    .Select(list =>
                        new
                        {
                            element =
                                DescribeElement(
                                    list),
                            items =
                                list.Items
                                    .Cast<object>()
                                    .Select((item, index) =>
                                    {
                                        var container =
                                            list.ItemContainerGenerator
                                                .ContainerFromIndex(
                                                    index)
                                            as ListBoxItem;

                                        return new
                                        {
                                            index,
                                            itemType =
                                                item.GetType()
                                                    .FullName,
                                            itemIsTarget =
                                                ReferenceEquals(
                                                    item,
                                                    effect),
                                            itemDataContextType =
                                                (item as FrameworkElement)
                                                    ?.DataContext
                                                    ?.GetType()
                                                    .FullName,
                                            itemDataContextIsTarget =
                                                ReferenceEquals(
                                                    (item as FrameworkElement)
                                                        ?.DataContext,
                                                    effect),
                                            container =
                                                container is null
                                                    ? null
                                                    : DescribeElement(
                                                        container),
                                        };
                                    })
                                    .ToArray(),
                        })
                    .ToArray();

            var targetDataContexts =
                visual
                    .Where(x =>
                        ReferenceEquals(
                            x.DataContext,
                            effect))
                    .Select(
                        DescribeElement)
                    .ToArray();

            var relevant =
                visual
                    .Where(x =>
                    {
                        var text =
                            GetText(x)
                            ?? string.Empty;

                        var type =
                            x.GetType()
                                .FullName
                            ?? string.Empty;

                        var dc =
                            x.DataContext
                                ?.GetType()
                                .FullName
                            ?? string.Empty;

                        return text.Contains(
                                "発音補助",
                                StringComparison.Ordinal)
                            || text.Contains(
                                "音声エフェクト",
                                StringComparison.Ordinal)
                            || text.Contains(
                                "Audio",
                                StringComparison.OrdinalIgnoreCase)
                            || text.Contains(
                                "補助モーラ",
                                StringComparison.Ordinal)
                            || text.Contains(
                                "抑揚",
                                StringComparison.Ordinal)
                            || type.Contains(
                                "AudioEffect",
                                StringComparison.OrdinalIgnoreCase)
                            || dc.Contains(
                                "AudioEffect",
                                StringComparison.OrdinalIgnoreCase)
                            || ReferenceEquals(
                                x.DataContext,
                                effect);
                    })
                    .Take(250)
                    .Select(
                        DescribeElement)
                    .ToArray();

            File.WriteAllText(
                Path.Combine(
                    output,
                    $"typed-ui-{phase}.json"),
                JsonSerializer.Serialize(
                    new
                    {
                        phase,
                        effectType =
                            effect.GetType()
                                .FullName,
                        audioEffectCount =
                            Application.Current.Windows
                                .Cast<Window>()
                                .Count(),
                        targetDataContextCount =
                            targetDataContexts.Length,
                        listBoxes,
                        targetDataContexts,
                        relevant,
                    },
                    new JsonSerializerOptions
                    {
                        WriteIndented = true,
                    }));
        }
        catch (Exception ex)
        {
            File.WriteAllText(
                Path.Combine(
                    output,
                    $"typed-ui-{phase}-diagnostic-error.txt"),
                ex.ToString());
        }
    }

    static bool SelectAudioEffectInEditor(
        PronunciationAssistAudioEffect effect)
    {
        var selected =
            false;

        foreach (Window window
            in Application.Current.Windows)
        {
            var visual =
                EnumerateVisual(window)
                    .ToArray();

            // Real YMM4's AudioEffectSelector may expose wrapper items in the
            // ListBox.Items collection while the realized ListBoxItem itself
            // carries the effect as DataContext. Prefer the realized host shape.
            foreach (var container
                in visual
                    .OfType<ListBoxItem>()
                    .Where(x =>
                        ReferenceEquals(
                            x.DataContext,
                            effect)))
            {
                try
                {
                    container.IsSelected =
                        true;

                    container.Focus();
                    container.BringIntoView();
                    container.UpdateLayout();

                    var parent =
                        FindVisualAncestor<ListBox>(
                            container);

                    if (parent is not null)
                    {
                        try
                        {
                            var item =
                                parent.ItemContainerGenerator
                                    .ItemFromContainer(
                                        container);

                            if (item is not null
                                && item
                                    != DependencyProperty.UnsetValue)
                            {
                                parent.SelectedItem =
                                    item;
                            }

                            parent.UpdateLayout();
                        }
                        catch
                        {
                        }
                    }

                    selected |=
                        container.IsSelected;
                }
                catch
                {
                }
            }

            if (selected)
                continue;

            // Fallback for hosts where the effect is the item directly.
            foreach (var listBox
                in visual
                    .OfType<ListBox>())
            {
                try
                {
                    var target =
                        listBox.Items
                            .Cast<object>()
                            .FirstOrDefault(x =>
                                ReferenceEquals(
                                    x,
                                    effect)
                                || ReferenceEquals(
                                    (x as FrameworkElement)
                                        ?.DataContext,
                                    effect));

                    if (target is null)
                        continue;

                    listBox.SelectedItem =
                        target;

                    listBox.UpdateLayout();

                    if (listBox.ItemContainerGenerator
                        .ContainerFromItem(
                            target)
                        is ListBoxItem container)
                    {
                        container.IsSelected =
                            true;

                        container.Focus();
                        container.BringIntoView();
                        container.UpdateLayout();

                        selected |=
                            container.IsSelected;
                    }
                    else
                    {
                        selected |=
                            ReferenceEquals(
                                listBox.SelectedItem,
                                target);
                    }
                }
                catch
                {
                }
            }
        }

        return selected;
    }

    static T? FindVisualAncestor<T>(
        DependencyObject start)
        where T : DependencyObject
    {
        DependencyObject? current =
            start;

        while (current is not null)
        {
            if (current is T typed)
                return typed;

            try
            {
                current =
                    VisualTreeHelper.GetParent(
                        current);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    static IEnumerable<DependencyObject>
        EnumerateVisual(
            DependencyObject root)
    {
        yield return root;

        int count;

        try
        {
            count =
                VisualTreeHelper
                    .GetChildrenCount(
                        root);
        }
        catch
        {
            yield break;
        }

        for (var index = 0;
             index < count;
             index++)
        {
            DependencyObject child;

            try
            {
                child =
                    VisualTreeHelper
                        .GetChild(
                            root,
                            index);
            }
            catch
            {
                continue;
            }

            foreach (var descendant
                in EnumerateVisual(
                    child))
            {
                yield return descendant;
            }
        }
    }

    static string? GetText(
        DependencyObject value) =>
        value switch
        {
            TextBlock text =>
                text.Text,
            Label label =>
                label.Content?.ToString(),
            GroupBox group =>
                group.Header?.ToString(),
            Expander expander =>
                expander.Header?.ToString(),
            ContentControl content
                when content.Content
                    is string text =>
                text,
            _ =>
                null,
        };

    static async Task RunLegacyMigrationLifecycleAsync(
        Timeline timeline,
        Character character,
        IVoiceParameter parameter,
        UndoRedoManager manager)
    {
        var voice =
            CreateVoice(
                character,
                parameter,
                "えええ",
                "エエエ",
                "B3_LEGACY_MIGRATION");

        Check(
            "migration_voice_added",
            timeline.TryAddItems(
                [voice],
                720,
                7));

        var rulesJson =
            HelperRuleCodec.Encode(
                new HelperRuleSet(
                    HelperRuleSet.CurrentVersion,
                    [
                        HelperRuleFactory.Create(
                            "えええ",
                            1,
                            "ウ",
                            HelperMoraKind.ZeroVowel,
                            contextLength: 1),
                    ]));

        var legacy =
            new PronunciationAssistEffect
            {
                IsEnabled = false,
                HelperRulesJson = rulesJson,
                Prosody = ProsodyGesture.Hold,
            };

        Check(
            "migration_legacy_seeded",
            ReviewAssistEffectCollection.TryAdd(
                voice,
                legacy,
                out var seedError)
            && seedError is null
            && voice.JimakuVideoEffects
                .Cast<object>()
                .Any(x =>
                    ReferenceEquals(
                        x,
                        legacy))
            && !voice.AudioEffects
                .Cast<object>()
                .Any(x =>
                    x is PronunciationAssistAudioEffect));

        var prepared =
            PronunciationAssistMigrationBatch.Prepare(
                [voice]);

        Check(
            "migration_prepared",
            prepared.IsReady
            && prepared.Batch is not null
            && prepared.Batch.ItemCount == 1);

        var batch =
            prepared.Batch
            ?? throw new InvalidOperationException(
                "Migration batch missing.");

        manager.Record();

        var recorded = 0;
        var undoed = 0;
        var redoed = 0;

        EventHandler recordedHandler =
            (_, _) => recorded++;

        EventHandler undoedHandler =
            (_, _) => undoed++;

        EventHandler redoedHandler =
            (_, _) => redoed++;

        manager.Recorded += recordedHandler;
        manager.Undoed += undoedHandler;
        manager.Redoed += redoedHandler;

        try
        {
            var commit =
                batch.Commit();

            Check(
                "migration_committed_to_audio",
                commit.IsSuccess
                && ReviewAssistEffectCollection
                    .Enumerate(voice)
                    .Count == 1
                && PronunciationAssistSettingsStore
                    .EnumerateLegacy(voice)
                    .Count == 0
                && PronunciationAssistSettingsStore
                    .EnumerateAudio(voice)
                    .Count == 1);

            var audio =
                AssertSingleAudio(voice);

            Check(
                "migration_settings_exact",
                !audio.IsEnabled
                && string.Equals(
                    audio.HelperRulesJson,
                    rulesJson,
                    StringComparison.Ordinal)
                && audio.Prosody
                    == ProsodyGesture.Hold
                && voice.AudioEffects
                    .Cast<object>()
                    .Any(x =>
                        ReferenceEquals(
                            x,
                            audio))
                && !voice.JimakuVideoEffects
                    .Cast<object>()
                    .Any(x =>
                        x is PronunciationAssistEffect));

            manager.AddCommand(
                new UndoRedoActionCommand(
                    batch.UndoOrThrow,
                    batch.RedoOrThrow));

            manager.Record();

            Check(
                "migration_recorded_once",
                recorded == 1);

            await manager.UndoAsync();

            Check(
                "migration_undo_restores_legacy",
                undoed == 1
                && PronunciationAssistSettingsStore
                    .EnumerateAudio(voice)
                    .Count == 0
                && ReferenceEquals(
                    AssertSingleLegacy(voice),
                    legacy)
                && !legacy.IsEnabled
                && string.Equals(
                    legacy.HelperRulesJson,
                    rulesJson,
                    StringComparison.Ordinal)
                && legacy.Prosody
                    == ProsodyGesture.Hold);

            await manager.RedoAsync();

            var redone =
                AssertSingleAudio(voice);

            Check(
                "migration_redo_restores_audio",
                redoed == 1
                && ReferenceEquals(
                    redone,
                    audio)
                && PronunciationAssistSettingsStore
                    .EnumerateLegacy(voice)
                    .Count == 0
                && !redone.IsEnabled
                && string.Equals(
                    redone.HelperRulesJson,
                    rulesJson,
                    StringComparison.Ordinal)
                && redone.Prosody
                    == ProsodyGesture.Hold);

            File.WriteAllText(
                Path.Combine(
                    output,
                    "migration-observation.json"),
                JsonSerializer.Serialize(
                    new
                    {
                        recorded,
                        undoed,
                        redoed,
                        storage = "AudioEffects",
                        settings = new
                        {
                            redone.IsEnabled,
                            redone.HelperRulesJson,
                            prosody =
                                redone.Prosody.ToString(),
                        },
                    },
                    new JsonSerializerOptions
                    {
                        WriteIndented = true,
                    }));
        }
        finally
        {
            manager.Recorded -= recordedHandler;
            manager.Undoed -= undoedHandler;
            manager.Redoed -= redoedHandler;
        }
    }

    static PronunciationAssistAudioEffect
        AssertSingleAudio(
            VoiceItem voice) =>
        PronunciationAssistSettingsStore
            .EnumerateAudio(voice)
            .Single();

    static PronunciationAssistEffect
        AssertSingleLegacy(
            VoiceItem voice) =>
        PronunciationAssistSettingsStore
            .EnumerateLegacy(voice)
            .Single();

    static VoiceItem CreateVoice(
        Character character,
        IVoiceParameter parameter,
        string serif,
        string hatsuon,
        string remark)
    {
        var voice =
            new VoiceItem
            {
                Serif = serif,
                Hatsuon = hatsuon,
                CharacterName =
                    character.Name,
                VoiceParameter =
                    parameter,
                Remark =
                    remark,
            };

        voice.Character =
            character;

        voice.VoiceParameter =
            parameter;

        voice.Serif =
            serif;

        voice.Hatsuon =
            hatsuon;

        voice.CharacterName =
            character.Name;

        return voice;
    }

    static UndoRedoManager
        AcquireUndoManager(
            object main)
    {
        const BindingFlags all =
            BindingFlags.Instance
            | BindingFlags.Public
            | BindingFlags.NonPublic;

        var modelField =
            main.GetType()
                .GetField(
                    "model",
                    all)
            ?? throw new MissingMemberException(
                main.GetType().FullName,
                "model");

        var model =
            modelField.GetValue(main)
            ?? throw new InvalidOperationException(
                "MainViewModel.model was null.");

        var managerProperty =
            model.GetType()
                .GetProperty(
                    "UndoRedoManager",
                    all)
            ?? throw new MissingMemberException(
                model.GetType().FullName,
                "UndoRedoManager");

        return managerProperty
            .GetValue(model)
            as UndoRedoManager
            ?? throw new InvalidOperationException(
                "MainModel.UndoRedoManager was unavailable.");
    }

    static Timeline? FindTimeline(
        object active)
    {
        for (var type =
                active.GetType();
             type is not null;
             type = type.BaseType)
        {
            foreach (var field
                in type.GetFields(
                    BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic))
            {
                if (typeof(Timeline)
                    .IsAssignableFrom(
                        field.FieldType)
                    && field.GetValue(active)
                        is Timeline timeline)
                {
                    return timeline;
                }
            }

            foreach (var property
                in type.GetProperties(
                    BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic))
            {
                if (property
                    .GetIndexParameters()
                    .Length != 0
                    || !typeof(Timeline)
                        .IsAssignableFrom(
                            property.PropertyType))
                {
                    continue;
                }

                try
                {
                    if (property
                        .GetValue(active)
                        is Timeline timeline)
                    {
                        return timeline;
                    }
                }
                catch
                {
                }
            }
        }

        return null;
    }

    sealed record SettingsRegistration(
        object? ResolvedEngine,
        Action Restore);

    static SettingsRegistration
        RegisterEngineInYmmSettings(
            VOICEVOXEngine engine,
            string speakerId)
    {
        var settingsType =
            typeof(VOICEVOXEngine)
                .Assembly
                .GetType(
                    "YukkuriMovieMaker.Settings.VOICEVOXSettings")
            ?? throw new InvalidOperationException(
                "VOICEVOXSettings type not found.");

        var defaultProperty =
            settingsType.GetProperty(
                "Default",
                BindingFlags.Static
                | BindingFlags.Public
                | BindingFlags.FlattenHierarchy)
            ?? settingsType.BaseType
                ?.GetProperty(
                    "Default",
                    BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.FlattenHierarchy)
            ?? throw new MissingMemberException(
                settingsType.FullName,
                "Default");

        var settings =
            defaultProperty.GetValue(null)
            ?? throw new InvalidOperationException(
                "VOICEVOXSettings.Default returned null.");

        var enginesProperty =
            settingsType.GetProperty(
                "Engines",
                BindingFlags.Instance
                | BindingFlags.Public)
            ?? throw new MissingMemberException(
                settingsType.FullName,
                "Engines");

        var original =
            enginesProperty.GetValue(
                settings)
            ?? throw new InvalidOperationException(
                "VOICEVOXSettings.Engines returned null.");

        var add =
            original.GetType()
                .GetMethods(
                    BindingFlags.Instance
                    | BindingFlags.Public)
                .FirstOrDefault(x =>
                    x.Name == "Add"
                    && x.GetParameters()
                        .Length == 1
                    && x.GetParameters()[0]
                        .ParameterType
                        .IsAssignableFrom(
                            typeof(
                                VOICEVOXEngine)))
            ?? throw new MissingMethodException(
                original.GetType().FullName,
                "Add(VOICEVOXEngine)");

        var augmented =
            add.Invoke(
                original,
                [engine])
            ?? throw new InvalidOperationException(
                "Engines.Add returned null.");

        enginesProperty.SetValue(
            settings,
            augmented);

        var findEngine =
            settingsType.GetMethod(
                "FindEngine",
                BindingFlags.Instance
                | BindingFlags.Public,
                binder: null,
                types: [typeof(string)],
                modifiers: null)
            ?? throw new MissingMethodException(
                settingsType.FullName,
                "FindEngine(string)");

        var resolved =
            findEngine.Invoke(
                settings,
                [speakerId]);

        return new SettingsRegistration(
            resolved,
            () =>
            {
                try
                {
                    enginesProperty.SetValue(
                        settings,
                        original);
                }
                catch
                {
                }
            });
    }

    static string RequireVoicePath(
        VoiceItem voice,
        string name)
    {
        var path =
            voice.FilePath;

        if (string.IsNullOrWhiteSpace(
                path)
            || !File.Exists(path))
        {
            throw new InvalidOperationException(
                name
                + " VoiceItem FilePath was unavailable.");
        }

        return path;
    }

    static string HashFile(
        string path)
    {
        using var stream =
            File.OpenRead(path);

        return Convert.ToHexString(
            SHA256.HashData(stream))
            .ToLowerInvariant();
    }

    static async Task WaitUntil(
        string name,
        Func<bool> predicate,
        int timeoutMs = 35_000)
    {
        var started =
            Environment.TickCount64;

        while (
            Environment.TickCount64
            - started
            < timeoutMs)
        {
            if (predicate())
                return;

            await Task.Delay(100);
        }

        throw new TimeoutException(
            name);
    }

    static void Check(
        string id,
        bool passed)
    {
        requirements.Add(
            new
            {
                id,
                passed,
            });

        if (!passed)
        {
            throw new InvalidOperationException(
                "Requirement failed: "
                + id);
        }
    }

    static void Write(
        string status,
        string? error)
    {
        File.WriteAllText(
            Path.Combine(
                output,
                "result.json"),
            JsonSerializer.Serialize(
                new
                {
                    schema =
                        "vqa.b3.import-product-native-smoke.v1",
                    status,
                    host =
                        "4.56.1.0 Lite",
                    sourceHead =
                        Environment.GetEnvironmentVariable(
                            "GITHUB_SHA"),
                    requirements,
                    error,
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                }));
    }
}
