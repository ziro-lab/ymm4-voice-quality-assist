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

namespace Ymm4VoiceQualityAssistA2NativeProbe;

public sealed class Entry : ILocalizePlugin
{
    public string Name => "VQA A2 Helper Native Probe";
    public void SetCulture(CultureInfo cultureInfo) => Probe.Schedule();
}

internal static class Probe
{
    static bool scheduled;
    static string output = "";
    static readonly List<object> requirements = [];

    internal static void Schedule()
    {
        var dir = Environment.GetEnvironmentVariable("VQA_A2_NATIVE_OUTPUT");
        var url = Environment.GetEnvironmentVariable("VQA_A2_FAKE_VOICEVOX_URL");

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
                        main.GetType()
                            .GetMethod(
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
                await RunAsync(active, url);
                Write("PASS_A2_HELPER_PRODUCT_NATIVE_SMOKE", null);
            }
            catch (Exception ex)
            {
                timer.Stop();
                Write(
                    "FAIL_A2_HELPER_PRODUCT_NATIVE_SMOKE",
                    ex.ToString());
            }
        };

        timer.Start();
    }

    static async Task RunAsync(
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
            Name = "VQA A2 Helper",
            URL = url,
            Path = "",
            Timeout = 10_000,
        };

        const string speakerUuid =
            "11111111-1111-1111-1111-111111111111";

        engine.SpeakerInfos.Add(
            new VOICEVOXSpeakerInfo(
                speakerUuid,
                ""));

        var speakerJson = JObject.Parse("""
        {
          "name": "CNWL Helper",
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

        var speakerType =
            typeof(VOICEVOXEngine).Assembly.GetType(
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

            var parameter = speaker.CreateVoiceParameter();
            parameter.GetType()
                .GetProperty(
                    "StyleID",
                    BindingFlags.Instance
                    | BindingFlags.Public)
                ?.SetValue(parameter, 1);

            var character = new Character
            {
                Name = "VQA A2 Helper",
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

            var vowel = await RunVowelLifecycleAsync(
                timeline,
                character,
                parameter,
                effectType);

            var consonant = await RunConsonantLifecycleAsync(
                timeline,
                character,
                parameter,
                effectType);

            File.WriteAllText(
                Path.Combine(
                    output,
                    "a2-helper-observation.json"),
                JsonSerializer.Serialize(
                    new
                    {
                        host = "4.56.1.0 Lite",
                        vowel,
                        consonant,
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

    static async Task<object> RunVowelLifecycleAsync(
        Timeline timeline,
        Character character,
        IVoiceParameter parameter,
        Type effectType)
    {
        const string originalSerif = "えええ";
        const string originalHatsuon = "エエエ";

        var voice = CreateVoice(
            character,
            parameter,
            originalSerif,
            originalHatsuon,
            "A2_VOWEL");

        Check(
            "vowel_voice_added",
            timeline.TryAddItems(
                [voice],
                200,
                5));

        await voice.CreateVoiceFileAsync();

        var path = RequireVoicePath(
            voice,
            "vowel");

        Check(
            "vowel_baseline_shape",
            File.Exists(path)
            && new FileInfo(path).Length == 4844);

        var effect = CreateAssistEffect(
            effectType,
            """
            {"version":1,"rules":[{"kind":"zeroVowel","helper":"ウ","anchor":{"position":1,"left":"え","right":"ええ"}},{"kind":"zeroVowel","helper":"ウ","anchor":{"position":2,"left":"ええ","right":"え"}}]}
            """);

        AppendEffect(voice, effect);

        await WaitUntil(
            "vowel helpers apply",
            () =>
                File.Exists(path)
                && new FileInfo(path).Length == 5444
                && VowelHelpersAreZero(voice));

        Check(
            "vowel_helpers_applied",
            new FileInfo(path).Length == 5444
            && VowelHelpersAreZero(voice));

        Check(
            "vowel_persisted_source_unchanged",
            voice.Serif == originalSerif
            && voice.Hatsuon == originalHatsuon);

        // Move both anchors by inserting source/reading content before them.
        voice.Serif = "あえええ";
        voice.Hatsuon = "アエエエ";

        await WaitUntil(
            "shifted helper anchors reapply",
            () =>
                File.Exists(path)
                && new FileInfo(path).Length == 5444
                && VowelHelpersAreZero(voice));

        Check(
            "shifted_anchor_reapplied",
            VowelHelpersAreZero(voice));

        Check(
            "shifted_source_not_rewritten",
            voice.Serif == "あえええ"
            && voice.Hatsuon == "アエエエ");

        // The same saved contexts are now repeated at multiple positions.
        // Product must fail closed and restore ordinary YMM4 audio.
        voice.Serif = "あえええええ";
        voice.Hatsuon = "アエエエエエ";

        await WaitUntil(
            "ambiguous helper anchor restores baseline",
            () =>
                File.Exists(path)
                && new FileInfo(path).Length == 4844);

        Check(
            "ambiguous_anchor_restores_baseline",
            new FileInfo(path).Length == 4844);

        voice.Serif = originalSerif;
        voice.Hatsuon = originalHatsuon;

        await WaitUntil(
            "original helper anchors reapply",
            () =>
                File.Exists(path)
                && new FileInfo(path).Length == 5444
                && VowelHelpersAreZero(voice));

        Check(
            "original_source_reapplies",
            VowelHelpersAreZero(voice));

        return new
        {
            finalSerif = voice.Serif,
            finalHatsuon = voice.Hatsuon,
            fileLength = new FileInfo(path).Length,
            moraTexts = GetMoras(voice)
                .Select(GetText)
                .ToArray(),
            helperVowelLengths = GetMoras(voice)
                .Where(x => GetText(x) == "ウ")
                .Select(x =>
                    GetNullableDouble(
                        x,
                        "VowelLength"))
                .ToArray(),
        };
    }

    static async Task<object> RunConsonantLifecycleAsync(
        Timeline timeline,
        Character character,
        IVoiceParameter parameter,
        Type effectType)
    {
        const string serif = "ええ";
        const string hatsuon = "エエ";

        var voice = CreateVoice(
            character,
            parameter,
            serif,
            hatsuon,
            "A2_CONSONANT");

        Check(
            "consonant_voice_added",
            timeline.TryAddItems(
                [voice],
                400,
                7));

        await voice.CreateVoiceFileAsync();

        var path = RequireVoicePath(
            voice,
            "consonant");

        var effect = CreateAssistEffect(
            effectType,
            """
            {"version":1,"rules":[{"kind":"zeroConsonant","helper":"セ","anchor":{"position":1,"left":"え","right":"え"}}]}
            """);

        AppendEffect(voice, effect);

        await WaitUntil(
            "consonant helper apply",
            () =>
                File.Exists(path)
                && new FileInfo(path).Length == 5644
                && ConsonantHelperIsZero(voice));

        Check(
            "consonant_helper_applied",
            ConsonantHelperIsZero(voice));

        var helper = GetMoras(voice)
            .Single(x => GetText(x) == "セ");

        Check(
            "consonant_helper_vowel_preserved",
            GetNullableDouble(
                helper,
                "VowelLength") is > 0.0);

        Check(
            "consonant_persisted_source_unchanged",
            voice.Serif == serif
            && voice.Hatsuon == hatsuon);

        return new
        {
            voice.Serif,
            voice.Hatsuon,
            fileLength = new FileInfo(path).Length,
            helperConsonantLength =
                GetNullableDouble(
                    helper,
                    "ConsonantLength"),
            helperVowelLength =
                GetNullableDouble(
                    helper,
                    "VowelLength"),
        };
    }

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
        string helperRulesJson)
    {
        if (Activator.CreateInstance(effectType)
            is not IVideoEffect effect)
        {
            throw new InvalidOperationException(
                "Product Assist Effect construction failed.");
        }

        effect.IsEnabled = true;

        var property = effectType.GetProperty(
            "HelperRulesJson",
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(
                effectType.FullName,
                "HelperRulesJson");

        property.SetValue(
            effect,
            helperRulesJson);

        return effect;
    }

    static bool VowelHelpersAreZero(
        VoiceItem voice)
    {
        var helpers = GetMoras(voice)
            .Where(x => GetText(x) == "ウ")
            .ToArray();

        return helpers.Length == 2
            && helpers.All(x =>
                GetNullableDouble(
                    x,
                    "VowelLength") == 0.0);
    }

    static bool ConsonantHelperIsZero(
        VoiceItem voice)
    {
        var helpers = GetMoras(voice)
            .Where(x => GetText(x) == "セ")
            .ToArray();

        if (helpers.Length != 1)
            return false;

        var helper = helpers[0];

        return GetNullableDouble(
                helper,
                "ConsonantLength") == 0.0
            && GetNullableDouble(
                helper,
                "VowelLength") is > 0.0;
    }

    static List<object> GetMoras(
        VoiceItem voice)
    {
        var pronounce = voice.Pronounce;
        if (pronounce is null)
            return [];

        var audioQuery = pronounce.GetType()
            .GetProperty(
                "AudioQuery",
                BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(pronounce);

        if (audioQuery is null)
            return [];

        if (audioQuery.GetType()
            .GetProperty(
                "AccentPhrases",
                BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(audioQuery)
            is not IEnumerable phrases)
        {
            return [];
        }

        return phrases.Cast<object>()
            .SelectMany(phrase =>
                (phrase.GetType()
                    .GetProperty(
                        "Moras",
                        BindingFlags.Instance | BindingFlags.Public)
                    ?.GetValue(phrase)
                    as IEnumerable
                    ?? Array.Empty<object>())
                .Cast<object>())
            .ToList();
    }

    static string GetText(object mora) =>
        mora.GetType()
            .GetProperty(
                "Text",
                BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(mora)?.ToString()
        ?? string.Empty;

    static double? GetNullableDouble(
        object target,
        string propertyName)
    {
        var value = target.GetType()
            .GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(target);

        return value is null
            ? null
            : Convert.ToDouble(
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
                $"{name} VoiceItem FilePath was unavailable.");
        }

        return path;
    }

    static void AppendEffect(
        VoiceItem voice,
        IVideoEffect effect)
    {
        var property = typeof(VoiceItem)
            .GetProperty(
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

        var add = current.GetType()
            .GetMethods(
                BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(x =>
                x.Name == "Add"
                && x.GetParameters().Length == 1
                && (
                    x.GetParameters()[0]
                        .ParameterType
                        .IsAssignableFrom(effect.GetType())
                    || x.GetParameters()[0]
                        .ParameterType
                        .IsAssignableFrom(
                            typeof(IVideoEffect))))
            ?? throw new MissingMethodException(
                current.GetType().FullName,
                "Add");

        var updated = add.Invoke(
            current,
            [effect]);

        if (updated is null
            || ReferenceEquals(
                updated,
                current))
        {
            return;
        }

        if (property.SetMethod?.IsPublic != true)
        {
            throw new InvalidOperationException(
                "JimakuVideoEffects is immutable without a public setter.");
        }

        property.SetValue(
            voice,
            updated);
    }

    static async Task WaitUntil(
        string name,
        Func<bool> condition,
        int timeoutMs = 25_000)
    {
        var started = Environment.TickCount64;

        while (
            Environment.TickCount64 - started
            < timeoutMs)
        {
            if (condition())
                return;

            await Task.Delay(100);
        }

        throw new TimeoutException(name);
    }

    sealed record SettingsRegistration(
        object? ResolvedEngine,
        Action Restore);

    static SettingsRegistration RegisterEngineInYmmSettings(
        VOICEVOXEngine engine,
        string speakerId)
    {
        var settingsType =
            typeof(VOICEVOXEngine).Assembly.GetType(
                "YukkuriMovieMaker.Settings.VOICEVOXSettings")
            ?? throw new InvalidOperationException(
                "VOICEVOXSettings type not found.");

        var defaultProperty =
            settingsType.GetProperty(
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

        var settings =
            defaultProperty.GetValue(null)
            ?? throw new InvalidOperationException(
                "VOICEVOXSettings.Default returned null.");

        var enginesProperty =
            settingsType.GetProperty(
                "Engines",
                BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(
                settingsType.FullName,
                "Engines");

        var original =
            enginesProperty.GetValue(settings)
            ?? throw new InvalidOperationException(
                "VOICEVOXSettings.Engines returned null.");

        var add = original.GetType()
            .GetMethods(
                BindingFlags.Instance | BindingFlags.Public)
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
                BindingFlags.Instance | BindingFlags.Public,
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

    static Timeline? FindTimeline(
        object active)
    {
        for (
            var type = active.GetType();
            type is not null;
            type = type.BaseType)
        {
            foreach (var field in type.GetFields(
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic))
            {
                if (
                    typeof(Timeline).IsAssignableFrom(
                        field.FieldType)
                    && field.GetValue(active)
                    is Timeline timeline)
                {
                    return timeline;
                }
            }

            foreach (var property in type.GetProperties(
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic))
            {
                if (
                    property.GetIndexParameters().Length != 0
                    || !typeof(Timeline)
                        .IsAssignableFrom(
                            property.PropertyType))
                {
                    continue;
                }

                try
                {
                    if (
                        property.GetValue(active)
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

    static void Check(
        string id,
        bool passed)
    {
        requirements.Add(new
        {
            id,
            passed,
        });

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
                        "vqa.a2.helper-product-native-smoke.v1",
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
