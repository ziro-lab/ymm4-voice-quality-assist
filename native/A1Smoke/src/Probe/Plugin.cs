using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;
using YukkuriMovieMaker.Plugin;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Plugin.Voice;
using YukkuriMovieMaker.Project;
using YukkuriMovieMaker.Project.Items;
using YukkuriMovieMaker.Voice;

namespace Ymm4VoiceQualityAssistNativeProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "VQA A1 Native Smoke Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    static bool scheduled;
    static string output = "";
    static readonly List<object> requirements = [];

    internal static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("VQA_A1_NATIVE_OUTPUT");
        var url = Environment.GetEnvironmentVariable("VQA_A1_FAKE_VOICEVOX_URL");
        if (scheduled || string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(url))
            return;

        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);

        Application.Current.Dispatcher.BeginInvoke(
            new Action(() => Start(url)),
            DispatcherPriority.ApplicationIdle);
    }

    static void Start(string url)
    {
        int ticks = 0;
        bool created = false;

        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(300),
        };

        timer.Tick += async (_, _) =>
        {
            try
            {
                ticks++;

                var main = Application.Current.Windows
                    .Cast<Window>()
                    .Select(x => x.DataContext)
                    .FirstOrDefault(x =>
                        x?.GetType().FullName
                        == "YukkuriMovieMaker.ViewModels.MainViewModel");

                if (main is null)
                    return;

                var active = main.GetType().GetProperty(
                    "ActiveTimelineViewModel",
                    BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic)?.GetValue(main);

                if (active is null)
                {
                    if (!created)
                    {
                        created = true;
                        main.GetType()
                            .GetMethod("CreateProject", Type.EmptyTypes)
                            ?.Invoke(main, null);
                    }

                    if (ticks > 160)
                        throw new TimeoutException(
                            "ActiveTimelineViewModel was not created.");

                    return;
                }

                timer.Stop();
                await RunAsync(active, url);
                Write("PASS_A1_PRODUCT_NATIVE_SMOKE", null);
            }
            catch (Exception ex)
            {
                timer.Stop();
                Write("FAIL_A1_PRODUCT_NATIVE_SMOKE", ex.ToString());
            }
        };

        timer.Start();
    }

    static async Task RunAsync(object active, string url)
    {
        var timeline = FindTimeline(active)
            ?? throw new InvalidOperationException(
                "Timeline could not be resolved.");

        Check("timeline_resolved", true);

        var engine = new VOICEVOXEngine(new VOICEVOXEngineContext())
        {
            Name = "VQA A1 Native Smoke",
            URL = url,
            Path = "",
            Timeout = 10_000,
        };

        const string speakerUuid =
            "11111111-1111-1111-1111-111111111111";

        engine.SpeakerInfos.Add(
            new VOICEVOXSpeakerInfo(speakerUuid, ""));

        var speakerJson = JObject.Parse("""
        {
          "name": "VQA Native Smoke",
          "speaker_uuid": "11111111-1111-1111-1111-111111111111",
          "styles": [
            { "name": "Normal", "id": 1, "type": "talk" }
          ],
          "version": "0.0.0",
          "supported_features": {
            "permitted_synthesis_morphing": "SELF_ONLY"
          }
        }
        """);

        engine.SpeakersJsonCache = new JArray(speakerJson)
            .ToString(Newtonsoft.Json.Formatting.None);

        var vvCharacter = new VOICEVOXCharacter(
            speakerJson,
            Array.Empty<VOICEVOXSpeakerInfo>(),
            false);

        var speakerType = typeof(VOICEVOXEngine).Assembly.GetType(
            "YukkuriMovieMaker.Voice.VOICEVOXVoiceSpeaker")
            ?? throw new TypeLoadException(
                "VOICEVOXVoiceSpeaker");

        var speakerObject = Activator.CreateInstance(
            speakerType,
            engine,
            vvCharacter)
            ?? throw new InvalidOperationException(
                "VOICEVOXVoiceSpeaker construction failed.");

        if (speakerObject is not IVoiceSpeaker speaker)
        {
            throw new InvalidOperationException(
                "Built-in VOICEVOX speaker interface missing.");
        }

        var registration = RegisterEngineInYmmSettings(
            engine,
            speaker.ID);

        try
        {
            Check(
                "fake_engine_registered",
                registration.ResolvedEngine is not null
                && ReferenceEquals(
                    registration.ResolvedEngine,
                    engine));

            var parameter = speaker.CreateVoiceParameter();
            parameter.GetType()
                .GetProperty(
                    "StyleID",
                    BindingFlags.Instance | BindingFlags.Public)
                ?.SetValue(parameter, 1);

            var character = new Character
            {
                Name = "VQA A1 Native",
                Voice = new VoiceDescription(speaker),
                VoiceParameter = parameter,
            };

            var voice = new VoiceItem
            {
                Serif = "東京<w0>大学",
                Hatsuon = "トウキョウダイガク",
                CharacterName = character.Name,
                VoiceParameter = parameter,
                Remark = "VQA_A1_NATIVE",
            };

            voice.Character = character;
            voice.VoiceParameter = parameter;
            voice.Serif = "東京<w0>大学";
            voice.Hatsuon = "トウキョウダイガク";
            voice.CharacterName = character.Name;

            Check(
                "voice_added_to_timeline",
                timeline.TryAddItems([voice], 240, 6));

            // Establish ordinary YMM4 baseline before enabling the product.
            await voice.CreateVoiceFileAsync();

            var voicePath = voice.FilePath;
            Check(
                "baseline_file_exists",
                !string.IsNullOrWhiteSpace(voicePath)
                && File.Exists(voicePath));

            if (string.IsNullOrWhiteSpace(voicePath)
                || !File.Exists(voicePath))
            {
                throw new InvalidOperationException(
                    "Baseline VoiceItem file was unavailable.");
            }

            var baselineHash = HashFile(voicePath);
            var baselineLength = new FileInfo(voicePath).Length;
            Check("baseline_wav_shape", baselineLength == 4844);

            var effect = CreateProductAssistEffect();
            Check("product_effect_type_discovered", effect is not null);
            AppendEffect(voice, effect!);

            await WaitUntil(
                "initial correction",
                () => IsCorrected(voice, voicePath));

            var correctedHash = HashFile(voicePath);
            Check(
                "initial_correction_applied",
                correctedHash != baselineHash
                && new FileInfo(voicePath).Length == 5444
                && GetFirstPauseLength(voice) == 0.0);

            effect.IsEnabled = false;

            await WaitUntil(
                "disable restores baseline",
                () =>
                    File.Exists(voicePath)
                    && new FileInfo(voicePath).Length == 4844
                    && HashFile(voicePath) == baselineHash);

            Check(
                "disable_restores_baseline",
                HashFile(voicePath) == baselineHash);

            effect.IsEnabled = true;

            await WaitUntil(
                "re-enable reapplies correction",
                () => IsCorrected(voice, voicePath));

            Check(
                "reenable_restores_same_corrected_wav",
                HashFile(voicePath) == correctedHash);

            voice.Serif = "東京大学";

            await WaitUntil(
                "marker removal restores baseline",
                () =>
                    File.Exists(voicePath)
                    && new FileInfo(voicePath).Length == 4844
                    && HashFile(voicePath) == baselineHash);

            Check(
                "marker_removal_restores_baseline",
                HashFile(voicePath) == baselineHash);

            voice.Serif = "東京<w0>大学";

            await WaitUntil(
                "marker restore reapplies correction",
                () => IsCorrected(voice, voicePath));

            Check(
                "marker_restore_reapplies_correction",
                HashFile(voicePath) == correctedHash);

            voice.Hatsuon = "トウキョーダイガク";

            await WaitUntil(
                "incompatible hatsuon fails closed",
                () =>
                    File.Exists(voicePath)
                    && new FileInfo(voicePath).Length == 4844
                    && HashFile(voicePath) == baselineHash);

            Check(
                "incompatible_hatsuon_restores_baseline",
                HashFile(voicePath) == baselineHash);

            voice.Hatsuon = "トウキョウダイガク";

            await WaitUntil(
                "compatible hatsuon reapplies correction",
                () => IsCorrected(voice, voicePath));

            Check(
                "compatible_hatsuon_reapplies_same_corrected_wav",
                HashFile(voicePath) == correctedHash);

            File.WriteAllText(
                Path.Combine(output, "observation.json"),
                JsonSerializer.Serialize(
                    new
                    {
                        host = "4.56.1.0 Lite",
                        baseline = new
                        {
                            length = baselineLength,
                            sha256 = baselineHash,
                        },
                        corrected = new
                        {
                            length = new FileInfo(voicePath).Length,
                            sha256 = correctedHash,
                            pause = GetFirstPauseLength(voice),
                        },
                        final = new
                        {
                            voice.Serif,
                            voice.Hatsuon,
                            sha256 = HashFile(voicePath),
                            pause = GetFirstPauseLength(voice),
                        },
                    },
                    new JsonSerializerOptions
                    {
                        WriteIndented = true,
                    }));
        }
        finally
        {
            registration.Restore();
        }
    }

    static bool IsCorrected(
        VoiceItem voice,
        string path) =>
        File.Exists(path)
        && new FileInfo(path).Length == 5444
        && GetFirstPauseLength(voice) == 0.0;

    static IVideoEffect? CreateProductAssistEffect()
    {
        const string typeName =
            "Ymm4VoiceQualityAssist.Effects.PronunciationAssistEffect";

        var type = AppDomain.CurrentDomain
            .GetAssemblies()
            .Select(assembly =>
                assembly.GetType(
                    typeName,
                    throwOnError: false))
            .FirstOrDefault(x => x is not null);

        if (type is null)
            return null;

        return Activator.CreateInstance(type) as IVideoEffect;
    }

    static double? GetFirstPauseLength(VoiceItem voice)
    {
        var pronounce = voice.Pronounce;
        if (pronounce is null)
            return null;

        var audioQuery = pronounce.GetType().GetProperty(
            "AudioQuery",
            BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(pronounce);

        if (audioQuery is null)
            return null;

        if (audioQuery.GetType().GetProperty(
                "AccentPhrases",
                BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(audioQuery)
            is not IEnumerable phrases)
        {
            return null;
        }

        var first = phrases.Cast<object>().FirstOrDefault();
        if (first is null)
            return null;

        var pause = first.GetType().GetProperty(
            "PauseMora",
            BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(first);

        if (pause is null)
            return null;

        var value = pause.GetType().GetProperty(
            "VowelLength",
            BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(pause);

        return value is null
            ? null
            : Convert.ToDouble(
                value,
                CultureInfo.InvariantCulture);
    }

    static void AppendEffect(
        VoiceItem voice,
        IVideoEffect effect)
    {
        var property = typeof(VoiceItem).GetProperty(
            nameof(VoiceItem.JimakuVideoEffects),
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(
                typeof(VoiceItem).FullName,
                nameof(VoiceItem.JimakuVideoEffects));

        var current = property.GetValue(voice)
            ?? throw new InvalidOperationException(
                "JimakuVideoEffects returned null.");

        if (current is IList list
            && !list.IsReadOnly
            && !list.IsFixedSize)
        {
            list.Add(effect);
            return;
        }

        var add = current.GetType().GetMethods(
                BindingFlags.Instance | BindingFlags.Public)
            .Where(x =>
                x.Name == "Add"
                && x.GetParameters().Length == 1)
            .FirstOrDefault(x =>
                x.GetParameters()[0].ParameterType
                    .IsAssignableFrom(effect.GetType())
                || x.GetParameters()[0].ParameterType
                    .IsAssignableFrom(typeof(IVideoEffect)))
            ?? throw new MissingMethodException(
                current.GetType().FullName,
                "Add");

        var updated = add.Invoke(current, [effect]);

        if (updated is null
            || ReferenceEquals(updated, current))
        {
            return;
        }

        if (property.SetMethod?.IsPublic != true)
        {
            throw new InvalidOperationException(
                "JimakuVideoEffects is immutable without a public setter.");
        }

        property.SetValue(voice, updated);
    }

    static async Task WaitUntil(
        string name,
        Func<bool> predicate,
        int timeoutMs = 25_000)
    {
        var started = Environment.TickCount64;

        while (Environment.TickCount64 - started < timeoutMs)
        {
            if (predicate())
                return;

            await Task.Delay(100);
        }

        throw new TimeoutException(name);
    }

    static string HashFile(string path) =>
        Convert.ToHexString(
            SHA256.HashData(File.ReadAllBytes(path)))
        .ToLowerInvariant();

    sealed record SettingsRegistration(
        object? ResolvedEngine,
        Action Restore);

    static SettingsRegistration RegisterEngineInYmmSettings(
        VOICEVOXEngine engine,
        string speakerId)
    {
        var settingsType = typeof(VOICEVOXEngine).Assembly.GetType(
            "YukkuriMovieMaker.Settings.VOICEVOXSettings")
            ?? throw new InvalidOperationException(
                "VOICEVOXSettings type not found.");

        var defaultProperty = settingsType.GetProperty(
            "Default",
            BindingFlags.Static
            | BindingFlags.Public
            | BindingFlags.FlattenHierarchy)
            ?? settingsType.BaseType?.GetProperty(
                "Default",
                BindingFlags.Static
                | BindingFlags.Public
                | BindingFlags.FlattenHierarchy)
            ?? throw new MissingMemberException(
                settingsType.FullName,
                "Default");

        var settings = defaultProperty.GetValue(null)
            ?? throw new InvalidOperationException(
                "VOICEVOXSettings.Default returned null.");

        var enginesProperty = settingsType.GetProperty(
            "Engines",
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(
                settingsType.FullName,
                "Engines");

        var original = enginesProperty.GetValue(settings)
            ?? throw new InvalidOperationException(
                "VOICEVOXSettings.Engines returned null.");

        var add = original.GetType().GetMethods(
                BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(x =>
                x.Name == "Add"
                && x.GetParameters().Length == 1
                && x.GetParameters()[0].ParameterType
                    .IsAssignableFrom(typeof(VOICEVOXEngine)))
            ?? throw new MissingMethodException(
                original.GetType().FullName,
                "Add(VOICEVOXEngine)");

        var augmented = add.Invoke(original, [engine])
            ?? throw new InvalidOperationException(
                "Engines.Add returned null.");

        enginesProperty.SetValue(settings, augmented);

        var findEngine = settingsType.GetMethod(
            "FindEngine",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: [typeof(string)],
            modifiers: null)
            ?? throw new MissingMethodException(
                settingsType.FullName,
                "FindEngine(string)");

        var resolved = findEngine.Invoke(
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

    static Timeline? FindTimeline(object active)
    {
        for (var type = active.GetType();
             type is not null;
             type = type.BaseType)
        {
            foreach (var field in type.GetFields(
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic))
            {
                if (typeof(Timeline).IsAssignableFrom(field.FieldType)
                    && field.GetValue(active) is Timeline timeline)
                {
                    return timeline;
                }
            }

            foreach (var property in type.GetProperties(
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic))
            {
                if (property.GetIndexParameters().Length != 0
                    || !typeof(Timeline).IsAssignableFrom(
                        property.PropertyType))
                {
                    continue;
                }

                try
                {
                    if (property.GetValue(active) is Timeline timeline)
                        return timeline;
                }
                catch
                {
                }
            }
        }

        return null;
    }

    static void Check(string id, bool passed)
    {
        requirements.Add(new
        {
            id,
            passed,
        });

        if (!passed)
            throw new InvalidOperationException(
                "Requirement failed: " + id);
    }

    static void Write(
        string status,
        string? error)
    {
        File.WriteAllText(
            Path.Combine(output, "result.json"),
            JsonSerializer.Serialize(
                new
                {
                    schema = "vqa.a1.product-native-smoke.v1",
                    status,
                    host = "4.56.1.0 Lite",
                    sourceHead =
                        Environment.GetEnvironmentVariable("GITHUB_SHA"),
                    requirements,
                    error,
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                }));
    }
}
