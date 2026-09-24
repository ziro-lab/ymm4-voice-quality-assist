using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
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

namespace Ymm4VoiceQualityAssistA3NativeProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "Voice Quality Assist A3 Native Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    static bool scheduled;
    static string output = string.Empty;
    static readonly List<object> requirements = [];

    static readonly double[] Baseline =
        [5.00, 5.08, 5.16, 5.08, 5.00];

    static readonly double[] Rise =
        [4.92, 5.04, 5.16, 5.12, 5.08];

    static readonly double[] Fall =
        [5.08, 5.12, 5.16, 5.04, 4.92];

    static readonly double[] Hold =
        [5.032, 5.072, 5.112, 5.072, 5.032];

    internal static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable(
            "VQA_A3_NATIVE_OUTPUT");
        var url = Environment.GetEnvironmentVariable(
            "VQA_A3_FAKE_VOICEVOX_URL");

        if (scheduled
            || string.IsNullOrWhiteSpace(dir)
            || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        scheduled = true;
        output = Path.GetFullPath(dir);
        Directory.CreateDirectory(output);

        Application.Current.Dispatcher.BeginInvoke(
            new Action(() => Start(url)),
            DispatcherPriority.ApplicationIdle);
    }

    static void Start(string url)
    {
        var ticks = 0;
        var created = false;

        var timer = new DispatcherTimer(
            DispatcherPriority.ApplicationIdle)
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
                    | BindingFlags.NonPublic)
                    ?.GetValue(main);

                if (active is null)
                {
                    if (!created)
                    {
                        created = true;
                        main.GetType().GetMethod(
                            "CreateProject",
                            Type.EmptyTypes)
                            ?.Invoke(main, null);
                    }

                    if (ticks > 160)
                    {
                        throw new TimeoutException(
                            "ActiveTimelineViewModel was not created.");
                    }

                    return;
                }

                timer.Stop();
                await RunAsync(main, active, url);
                Write(
                    "PASS_A3_PROSODY_PRODUCT_NATIVE_SMOKE",
                    null);
            }
            catch (Exception ex)
            {
                timer.Stop();
                Write(
                    "FAIL_A3_PROSODY_PRODUCT_NATIVE_SMOKE",
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
        var timeline = FindTimeline(active)
            ?? throw new InvalidOperationException(
                "Timeline could not be resolved.");

        Check("timeline_resolved", true);

        var engine = new VOICEVOXEngine(
            new VOICEVOXEngineContext())
        {
            Name = "VQA Prosody VOICEVOX",
            URL = url,
            Path = string.Empty,
            Timeout = 10_000,
        };

        const string fakeSpeakerUuid =
            "11111111-1111-1111-1111-111111111111";

        engine.SpeakerInfos.Add(
            new VOICEVOXSpeakerInfo(
                fakeSpeakerUuid,
                string.Empty));

        var speakerJson = JObject.Parse(
            """
            {
              "name": "VQA Prosody",
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

        engine.SpeakersJsonCache =
            new JArray(speakerJson)
                .ToString(
                    Newtonsoft.Json.Formatting.None);

        var vvCharacter = new VOICEVOXCharacter(
            speakerJson,
            Array.Empty<VOICEVOXSpeakerInfo>(),
            false);

        var speakerType = typeof(VOICEVOXEngine)
            .Assembly
            .GetType(
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
                "Built-in VOICEVOX speaker does not implement IVoiceSpeaker.");
        }

        var registration =
            RegisterEngineInYmmSettings(
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

            var parameter =
                speaker.CreateVoiceParameter();

            parameter.GetType().GetProperty(
                "StyleID",
                BindingFlags.Instance
                | BindingFlags.Public)
                ?.SetValue(parameter, 1);

            var character = new Character
            {
                Name = "VQA Prosody",
                Voice = new VoiceDescription(speaker),
                VoiceParameter = parameter,
            };

            var effectType = FindProductEffectType();
            Check(
                "product_effect_type_discovered",
                effectType is not null);

            if (effectType is null)
            {
                throw new InvalidOperationException(
                    "Product PronunciationAssistEffect was not discovered.");
            }

            var observation =
                await RunLifecycleAsync(
                    timeline,
                    character,
                    parameter,
                    effectType);

            var reload =
                await RunReloadLifecycleAsync(
                    main,
                    timeline,
                    character,
                    parameter,
                    effectType);

            File.WriteAllText(
                Path.Combine(
                    output,
                    "a3-prosody-observation.json"),
                JsonSerializer.Serialize(
                    new
                    {
                        host = "4.56.1.0 Lite",
                        speakerId = speaker.ID,
                        observation,
                        reload,
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

    static async Task<object> RunLifecycleAsync(
        Timeline timeline,
        Character character,
        IVoiceParameter parameter,
        Type effectType)
    {
        const string serif = "えええええ";
        const string hatsuon = "エエエエエ";

        var voice = CreateVoice(
            character,
            parameter,
            serif,
            hatsuon,
            "A3_PROSODY");

        Check(
            "prosody_voice_added",
            timeline.TryAddItems(
                [voice],
                180,
                5));

        await voice.CreateVoiceFileAsync();

        var path = RequireVoicePath(
            voice,
            "prosody");

        Check(
            "baseline_shape",
            File.Exists(path)
            && new FileInfo(path).Length == 4844
            && PitchMatches(voice, Baseline));

        var persistedSerif = voice.Serif;
        var persistedHatsuon = voice.Hatsuon;

        var effect = CreateAssistEffect(
            effectType,
            "LightRise");

        AppendEffect(
            voice,
            effect);

        await WaitUntil(
            "LightRise apply",
            () =>
                File.Exists(path)
                && new FileInfo(path).Length == 5844
                && PitchMatches(voice, Rise));

        Check(
            "light_rise_applied",
            PitchMatches(voice, Rise));

        Check(
            "source_unchanged_after_rise",
            voice.Serif == persistedSerif
            && voice.Hatsuon == persistedHatsuon);

        SetProsody(
            effect,
            "LightFall");

        await WaitUntil(
            "LightFall apply",
            () =>
                File.Exists(path)
                && new FileInfo(path).Length == 6044
                && PitchMatches(voice, Fall));

        Check(
            "light_fall_applied",
            PitchMatches(voice, Fall));

        SetProsody(
            effect,
            "None");

        await WaitUntil(
            "None restores baseline",
            () =>
                File.Exists(path)
                && new FileInfo(path).Length == 4844
                && PitchMatches(voice, Baseline));

        Check(
            "none_restores_baseline",
            PitchMatches(voice, Baseline));

        SetProsody(
            effect,
            "Hold");

        await WaitUntil(
            "Hold apply",
            () =>
                File.Exists(path)
                && new FileInfo(path).Length == 6244
                && PitchMatches(voice, Hold));

        Check(
            "hold_applied",
            PitchMatches(voice, Hold));

        effect.IsEnabled = false;

        await WaitUntil(
            "disable restores baseline",
            () =>
                File.Exists(path)
                && new FileInfo(path).Length == 4844
                && PitchMatches(voice, Baseline));

        Check(
            "disable_restores_baseline",
            PitchMatches(voice, Baseline));

        effect.IsEnabled = true;

        await WaitUntil(
            "re-enable reapplies hold",
            () =>
                File.Exists(path)
                && new FileInfo(path).Length == 6244
                && PitchMatches(voice, Hold));

        Check(
            "reenable_reapplies_hold",
            PitchMatches(voice, Hold));

        Check(
            "persisted_source_unchanged",
            voice.Serif == persistedSerif
            && voice.Hatsuon == persistedHatsuon);

        Check(
            "prosody_setting_stays_hold",
            string.Equals(
                GetProsody(effect),
                "Hold",
                StringComparison.Ordinal));

        return new
        {
            serif = voice.Serif,
            hatsuon = voice.Hatsuon,
            prosody = GetProsody(effect),
            pitches = CurrentPitches(voice),
            fileLength =
                new FileInfo(path).Length,
        };
    }

    static async Task<object> RunReloadLifecycleAsync(
        object main,
        Timeline timeline,
        Character character,
        IVoiceParameter parameter,
        Type effectType)
    {
        const string serif = "えええええ";
        const string hatsuon = "エエエエエ";
        const string remark = "A3_RELOAD";

        var voice = CreateVoice(
            character,
            parameter,
            serif,
            hatsuon,
            remark);

        var effect = CreateAssistEffect(
            effectType,
            "Hold");

        AppendEffect(
            voice,
            effect);

        Check(
            "reload_voice_added",
            timeline.TryAddItems(
                [voice],
                720,
                11));

        await voice.CreateVoiceFileAsync();

        var path = RequireVoicePath(
            voice,
            "reload");

        await WaitUntil(
            "reload initial Hold apply",
            () =>
                File.Exists(path)
                && new FileInfo(path).Length == 6244
                && PitchMatches(voice, Hold));

        Check(
            "reload_initial_hold_applied",
            PitchMatches(voice, Hold));

        // The fake speaker is a native-test fixture, not a persistent provider.
        // Remove it before saving while keeping the durable Assist Effect.
        foreach (var item in timeline.Items
            .OfType<VoiceItem>())
        {
            var name = item.CharacterName;

            item.Character = new Character
            {
                Name = string.IsNullOrWhiteSpace(name)
                    ? "VQA A3 Persist"
                    : name,
            };
            item.CharacterName =
                item.Character.Name;
        }

        voice.Serif = serif;
        voice.Hatsuon = hatsuon;

        var saveProject = PublicMethod(
            main,
            "SaveProject",
            typeof(string));

        var openProject = PublicMethod(
            main,
            "OpenProject",
            typeof(string));

        var pathA = Path.Combine(
            output,
            "a3-prosody-a.ymmp");

        saveProject.Invoke(
            main,
            [pathA]);

        await WaitUntil(
            "save prosody project A",
            () =>
                File.Exists(pathA)
                && new FileInfo(pathA).Length > 0);

        var rawA =
            File.ReadAllText(pathA);

        Check(
            "reload_project_a_saved",
            rawA.Contains(
                "Prosody",
                StringComparison.Ordinal)
            && rawA.Contains(
                "Hold",
                StringComparison.Ordinal));

        voice.Serif = "壊したB";
        effect.IsEnabled = false;
        SetProsody(
            effect,
            "None");

        var pathB = Path.Combine(
            output,
            "a3-prosody-b.ymmp");

        saveProject.Invoke(
            main,
            [pathB]);

        await WaitUntil(
            "save prosody project B",
            () =>
                File.Exists(pathB)
                && new FileInfo(pathB).Length > 0);

        openProject.Invoke(
            main,
            [pathA]);

        await WaitUntil(
            "open prosody project A",
            () =>
                SamePath(
                    GetProjectFilePath(main),
                    pathA)
                && FindVoice(
                    main,
                    remark) is not null,
            15_000);

        var reloaded = FindVoice(
            main,
            remark)
            ?? throw new InvalidOperationException(
                "Reloaded A3 VoiceItem was not found.");

        var reloadedEffect = GetAssistEffect(
            reloaded,
            effectType)
            ?? throw new InvalidOperationException(
                "Reloaded A3 Assist Effect was not found.");

        Check(
            "reload_source_survives",
            !ReferenceEquals(
                voice,
                reloaded)
            && reloaded.Serif == serif
            && reloaded.Hatsuon == hatsuon
            && reloadedEffect.IsEnabled);

        Check(
            "reload_prosody_exact",
            string.Equals(
                GetProsody(reloadedEffect),
                "Hold",
                StringComparison.Ordinal));

        reloaded.Character = character;
        reloaded.CharacterName =
            character.Name;
        reloaded.VoiceParameter = parameter;
        reloaded.Serif = serif;
        reloaded.Hatsuon = hatsuon;

        Check(
            "reload_fixture_speaker_rebound",
            reloaded.Character?.Voice?.Speaker
                is { } rebound
            && rebound.ID
                == character.Voice?.Speaker?.ID);

        if (string.IsNullOrWhiteSpace(
                reloaded.FilePath)
            || !File.Exists(
                reloaded.FilePath))
        {
            await reloaded.CreateVoiceFileAsync();
        }

        var reloadedPath = RequireVoicePath(
            reloaded,
            "reloaded");

        await WaitUntil(
            "reload Hold automatic reapply",
            () =>
                File.Exists(reloadedPath)
                && new FileInfo(reloadedPath).Length
                    == 6244
                && PitchMatches(
                    reloaded,
                    Hold),
            25_000);

        Check(
            "reload_hold_reapplied",
            PitchMatches(
                reloaded,
                Hold)
            && reloaded.Serif == serif
            && reloaded.Hatsuon == hatsuon);

        return new
        {
            pathA,
            sameObject =
                ReferenceEquals(
                    voice,
                    reloaded),
            reloaded.Serif,
            reloaded.Hatsuon,
            prosody =
                GetProsody(
                    reloadedEffect),
            fileLength =
                new FileInfo(
                    reloadedPath).Length,
            pitches =
                CurrentPitches(
                    reloaded),
        };
    }

    static MethodInfo PublicMethod(
        object target,
        string name,
        params Type[] types) =>
        target.GetType().GetMethod(
            name,
            BindingFlags.Instance
            | BindingFlags.Public,
            binder: null,
            types,
            modifiers: null)
        ?? throw new MissingMethodException(
            target.GetType().FullName,
            name);

    static VoiceItem? FindVoice(
        object main,
        string remark)
    {
        var active = main.GetType()
            .GetProperty(
                "ActiveTimelineViewModel",
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic)
            ?.GetValue(main);

        var timeline = active is null
            ? null
            : FindTimeline(active);

        return timeline?.Items
            .OfType<VoiceItem>()
            .FirstOrDefault(x =>
                x.Remark == remark);
    }

    static IVideoEffect? GetAssistEffect(
        VoiceItem voice,
        Type effectType)
    {
        if (voice.JimakuVideoEffects
            is not IEnumerable effects)
        {
            return null;
        }

        return effects.Cast<object>()
            .FirstOrDefault(x =>
                x.GetType() == effectType)
            as IVideoEffect;
    }

    static string? GetProjectFilePath(
        object main)
    {
        var property = main.GetType()
            .GetProperty(
                "ProjectFilePath",
                BindingFlags.Instance
                | BindingFlags.Public)
            ?? throw new MissingMemberException(
                main.GetType().FullName,
                "ProjectFilePath");

        var reactive =
            property.GetValue(main);

        return reactive?.GetType()
            .GetProperty(
                "Value",
                BindingFlags.Instance
                | BindingFlags.Public)
            ?.GetValue(reactive)
            as string;
    }

    static bool SamePath(
        string? actual,
        string expected) =>
        !string.IsNullOrWhiteSpace(actual)
        && string.Equals(
            Path.GetFullPath(actual),
            Path.GetFullPath(expected),
            StringComparison.OrdinalIgnoreCase);

    static Type? FindProductEffectType()
    {
        const string typeName =
            "Ymm4VoiceQualityAssist.Effects.PronunciationAssistEffect";

        return AppDomain.CurrentDomain
            .GetAssemblies()
            .Select(x =>
                x.GetType(
                    typeName,
                    throwOnError: false))
            .FirstOrDefault(x => x is not null);
    }

    static IVideoEffect CreateAssistEffect(
        Type effectType,
        string prosody)
    {
        if (Activator.CreateInstance(effectType)
            is not IVideoEffect effect)
        {
            throw new InvalidOperationException(
                "Product Assist Effect construction failed.");
        }

        effect.IsEnabled = true;
        SetProsody(
            effect,
            prosody);

        return effect;
    }

    static void SetProsody(
        IVideoEffect effect,
        string value)
    {
        var property = effect.GetType()
            .GetProperty(
                "Prosody",
                BindingFlags.Instance
                | BindingFlags.Public)
            ?? throw new MissingMemberException(
                effect.GetType().FullName,
                "Prosody");

        if (property.SetMethod?.IsPublic != true)
        {
            throw new InvalidOperationException(
                "Prosody property is not publicly writable.");
        }

        var parsed = Enum.Parse(
            property.PropertyType,
            value,
            ignoreCase: false);

        property.SetValue(
            effect,
            parsed);
    }

    static string? GetProsody(
        IVideoEffect effect) =>
        effect.GetType()
            .GetProperty(
                "Prosody",
                BindingFlags.Instance
                | BindingFlags.Public)
            ?.GetValue(effect)
            ?.ToString();

    static VoiceItem CreateVoice(
        Character character,
        IVoiceParameter parameter,
        string serif,
        string hatsuon,
        string remark)
    {
        var voice = new VoiceItem
        {
            Serif = serif,
            Hatsuon = hatsuon,
            CharacterName = character.Name,
            VoiceParameter = parameter,
            Remark = remark,
        };

        voice.Character = character;
        voice.VoiceParameter = parameter;
        voice.Serif = serif;
        voice.Hatsuon = hatsuon;
        voice.CharacterName = character.Name;

        return voice;
    }

    static void AppendEffect(
        VoiceItem voice,
        IVideoEffect effect)
    {
        var property = typeof(VoiceItem)
            .GetProperty(
                nameof(VoiceItem.JimakuVideoEffects),
                BindingFlags.Instance
                | BindingFlags.Public)
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

        var add = current.GetType()
            .GetMethods(
                BindingFlags.Instance
                | BindingFlags.Public)
            .FirstOrDefault(x =>
                x.Name == "Add"
                && x.GetParameters().Length == 1
                && (
                    x.GetParameters()[0]
                        .ParameterType
                        .IsAssignableFrom(
                            effect.GetType())
                    || x.GetParameters()[0]
                        .ParameterType
                        .IsAssignableFrom(
                            typeof(IVideoEffect))));

        if (add is not null)
        {
            var updated = add.Invoke(
                current,
                [effect]);

            if (updated is not null
                && property.SetMethod?.IsPublic == true)
            {
                property.SetValue(
                    voice,
                    updated);
            }

            return;
        }

        throw new InvalidOperationException(
            "JimakuVideoEffects is immutable without a supported Add route.");
    }

    static bool PitchMatches(
        VoiceItem voice,
        IReadOnlyList<double> expected)
    {
        var actual = CurrentPitches(voice);
        if (actual is null
            || actual.Count != expected.Count)
        {
            return false;
        }

        for (var i = 0; i < expected.Count; i++)
        {
            if (Math.Abs(
                actual[i] - expected[i])
                > 0.000001)
            {
                return false;
            }
        }

        return true;
    }

    static IReadOnlyList<double>? CurrentPitches(
        VoiceItem voice)
    {
        if (voice.Pronounce is null)
            return null;

        try
        {
            return GetFirstPhraseMoras(
                voice.Pronounce)
                .Select(x =>
                    GetDouble(
                        x,
                        "Pitch"))
                .ToArray();
        }
        catch
        {
            return null;
        }
    }

    static List<object> GetFirstPhraseMoras(
        IVoicePronounce pronounce)
    {
        var query = pronounce.GetType()
            .GetProperty(
                "AudioQuery",
                BindingFlags.Instance
                | BindingFlags.Public)
            ?.GetValue(pronounce)
            ?? throw new InvalidOperationException(
                "AudioQuery unavailable.");

        var phrases = query.GetType()
            .GetProperty(
                "AccentPhrases",
                BindingFlags.Instance
                | BindingFlags.Public)
            ?.GetValue(query) as IEnumerable
            ?? throw new InvalidOperationException(
                "AccentPhrases unavailable.");

        var phrase = phrases.Cast<object>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "AccentPhrases empty.");

        var moras = phrase.GetType()
            .GetProperty(
                "Moras",
                BindingFlags.Instance
                | BindingFlags.Public)
            ?.GetValue(phrase) as IEnumerable
            ?? throw new InvalidOperationException(
                "Moras unavailable.");

        return moras.Cast<object>().ToList();
    }

    static double GetDouble(
        object target,
        string propertyName)
    {
        var value = target.GetType()
            .GetProperty(
                propertyName,
                BindingFlags.Instance
                | BindingFlags.Public)
            ?.GetValue(target)
            ?? throw new InvalidOperationException(
                propertyName + " was null.");

        return Convert.ToDouble(
            value,
            CultureInfo.InvariantCulture);
    }

    static string RequireVoicePath(
        VoiceItem voice,
        string name)
    {
        var path = voice.FilePath;

        if (string.IsNullOrWhiteSpace(path)
            || !File.Exists(path))
        {
            throw new InvalidOperationException(
                name
                + " VoiceItem FilePath was unavailable.");
        }

        return path;
    }

    sealed record SettingsRegistration(
        object? ResolvedEngine,
        Action Restore);

    static SettingsRegistration RegisterEngineInYmmSettings(
        VOICEVOXEngine engine,
        string speakerId)
    {
        var settingsType = typeof(VOICEVOXEngine)
            .Assembly
            .GetType(
                "YukkuriMovieMaker.Settings.VOICEVOXSettings")
            ?? throw new InvalidOperationException(
                "VOICEVOXSettings type not found.");

        var defaultProperty = settingsType
            .GetProperty(
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

        var settings = defaultProperty
            .GetValue(null)
            ?? throw new InvalidOperationException(
                "VOICEVOXSettings.Default returned null.");

        var enginesProperty = settingsType
            .GetProperty(
                "Engines",
                BindingFlags.Instance
                | BindingFlags.Public)
            ?? throw new MissingMemberException(
                settingsType.FullName,
                "Engines");

        var original = enginesProperty
            .GetValue(settings)
            ?? throw new InvalidOperationException(
                "VOICEVOXSettings.Engines returned null.");

        var add = original.GetType()
            .GetMethods(
                BindingFlags.Instance
                | BindingFlags.Public)
            .FirstOrDefault(x =>
                x.Name == "Add"
                && x.GetParameters().Length == 1
                && x.GetParameters()[0]
                    .ParameterType
                    .IsAssignableFrom(
                        typeof(VOICEVOXEngine)))
            ?? throw new MissingMethodException(
                original.GetType().FullName,
                "Add(VOICEVOXEngine)");

        var augmented = add.Invoke(
            original,
            [engine])
            ?? throw new InvalidOperationException(
                "Engines.Add returned null.");

        enginesProperty.SetValue(
            settings,
            augmented);

        var findEngine = settingsType
            .GetMethod(
                "FindEngine",
                BindingFlags.Instance
                | BindingFlags.Public,
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

    static Timeline? FindTimeline(
        object active)
    {
        for (var type = active.GetType();
             type is not null;
             type = type.BaseType)
        {
            foreach (var field in type
                .GetFields(
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

            foreach (var property in type
                .GetProperties(
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
                    if (property.GetValue(active)
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

    static async Task WaitUntil(
        string name,
        Func<bool> predicate,
        int timeoutMs = 35_000)
    {
        var started =
            Environment.TickCount64;

        while (
            Environment.TickCount64 - started
            < timeoutMs)
        {
            if (predicate())
                return;

            await Task.Delay(100);
        }

        throw new TimeoutException(name);
    }

    static void Check(
        string id,
        bool passed)
    {
        requirements.Add(
            new { id, passed });

        if (!passed)
        {
            throw new InvalidOperationException(
                "Requirement failed: " + id);
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
                        "vqa.a3.prosody-product-native-smoke.v1",
                    status,
                    host = "4.56.1.0 Lite",
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
