using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;
using Ymm4VoiceQualityAssist.Core;
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
            AcquireUndoManager(main);

        Check(
            "undo_manager_resolved",
            manager is not null);

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
        }
        finally
        {
            registration.Restore();
        }
    }

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
