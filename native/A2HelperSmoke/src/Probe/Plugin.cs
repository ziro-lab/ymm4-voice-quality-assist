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
                await RunAsync(main, active, url);
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

            var combined = await RunCombinedLifecycleAsync(
                timeline,
                character,
                parameter,
                effectType);

            var reload = await RunReloadLifecycleAsync(
                main,
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
                        combined,
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

    static async Task<object> RunReloadLifecycleAsync(
        object main,
        Timeline timeline,
        Character character,
        IVoiceParameter parameter,
        Type effectType)
    {
        const string serif = "ええ";
        const string hatsuon = "エエ";
        const string remark = "A2_RELOAD";
        const string helperJson =
            "{\"version\":1,\"rules\":[{\"kind\":\"zeroConsonant\",\"helper\":\"セ\",\"anchor\":{\"position\":1,\"left\":\"え\",\"right\":\"え\"}}]}";

        var voice = CreateVoice(
            character,
            parameter,
            serif,
            hatsuon,
            remark);

        var effect = CreateAssistEffect(
            effectType,
            helperJson);

        AppendEffect(voice, effect);

        Check(
            "reload_voice_added",
            timeline.TryAddItems(
                [voice],
                800,
                11));

        await voice.CreateVoiceFileAsync();

        var path = RequireVoicePath(
            voice,
            "reload");

        await WaitUntil(
            "reload initial helper apply",
            () =>
                File.Exists(path)
                && new FileInfo(path).Length == 5644
                && ConsonantHelperIsZero(voice));

        Check(
            "reload_initial_helper_applied",
            ConsonantHelperIsZero(voice));

        // Fake VOICEVOX is a test fixture, not an installed persistent provider.
        // Detach it from every synthetic test item before the native save.
        foreach (var item in timeline.Items.OfType<VoiceItem>())
        {
            var name = item.CharacterName;
            item.Character = new Character
            {
                Name = string.IsNullOrWhiteSpace(name)
                    ? "VQA A2 Persist"
                    : name,
            };
            item.CharacterName = item.Character.Name;
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
            "a2-helper-a.ymmp");

        saveProject.Invoke(
            main,
            [pathA]);

        await WaitUntil(
            "save helper project A",
            () =>
                File.Exists(pathA)
                && new FileInfo(pathA).Length > 0);

        var rawA = File.ReadAllText(pathA);

        Check(
            "reload_project_a_saved",
            rawA.Contains(
                "zeroConsonant",
                StringComparison.Ordinal)
            && rawA.Contains(
                "HelperRulesJson",
                StringComparison.Ordinal));

        voice.Serif = "壊したB";
        effect.IsEnabled = false;
        SetHelperRulesJson(
            effect,
            string.Empty);

        var pathB = Path.Combine(
            output,
            "a2-helper-b.ymmp");

        saveProject.Invoke(
            main,
            [pathB]);

        await WaitUntil(
            "save helper project B",
            () =>
                File.Exists(pathB)
                && new FileInfo(pathB).Length > 0);

        openProject.Invoke(
            main,
            [pathA]);

        await WaitUntil(
            "open helper project A",
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
                "Reloaded A2 VoiceItem was not found.");

        var reloadedEffect = GetAssistEffect(
            reloaded,
            effectType)
            ?? throw new InvalidOperationException(
                "Reloaded A2 Assist Effect was not found.");

        Check(
            "reload_source_survives",
            !ReferenceEquals(
                voice,
                reloaded)
            && reloaded.Serif == serif
            && reloaded.Hatsuon == hatsuon
            && reloadedEffect.IsEnabled);

        Check(
            "reload_effect_rules_exact",
            string.Equals(
                GetHelperRulesJson(
                    reloadedEffect),
                helperJson,
                StringComparison.Ordinal));

        // Rebind only the synthetic provider after the real project reload.
        reloaded.Character = character;
        reloaded.CharacterName = character.Name;
        reloaded.VoiceParameter = parameter;
        reloaded.Serif = serif;
        reloaded.Hatsuon = hatsuon;

        Check(
            "reload_fixture_speaker_rebound",
            reloaded.Character?.Voice?.Speaker is { } rebound
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
            "reload helper automatic reapply",
            () =>
                File.Exists(reloadedPath)
                && new FileInfo(reloadedPath).Length == 5644
                && ConsonantHelperIsZero(reloaded),
            25_000);

        Check(
            "reload_helper_reapplied",
            ConsonantHelperIsZero(reloaded)
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
            helperRulesJson =
                GetHelperRulesJson(
                    reloadedEffect),
            fileLength =
                new FileInfo(
                    reloadedPath).Length,
            helperConsonantLength =
                GetMoras(reloaded)
                    .Where(x =>
                        GetText(x) == "セ")
                    .Select(x =>
                        GetNullableDouble(
                            x,
                            "ConsonantLength"))
                    .SingleOrDefault(),
        };
    }

    static MethodInfo PublicMethod(
        object target,
        string name,
        params Type[] types) =>
        target.GetType().GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.Public,
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
        var active = main.GetType().GetProperty(
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

        return effects
            .Cast<object>()
            .FirstOrDefault(x =>
                x.GetType() == effectType)
            as IVideoEffect;
    }

    static string? GetHelperRulesJson(
        IVideoEffect effect) =>
        effect.GetType().GetProperty(
            "HelperRulesJson",
            BindingFlags.Instance | BindingFlags.Public)
        ?.GetValue(effect)
        as string;

    static void SetHelperRulesJson(
        IVideoEffect effect,
        string value)
    {
        var property = effect.GetType().GetProperty(
            "HelperRulesJson",
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(
                effect.GetType().FullName,
                "HelperRulesJson");

        property.SetValue(
            effect,
            value);
    }

    static string? GetProjectFilePath(
        object main)
    {
        var property = main.GetType().GetProperty(
            "ProjectFilePath",
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMemberException(
                main.GetType().FullName,
                "ProjectFilePath");

        var reactive = property.GetValue(main);

        return reactive?.GetType().GetProperty(
            "Value",
            BindingFlags.Instance | BindingFlags.Public)
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

    static async Task<object> RunCombinedLifecycleAsync(
        Timeline timeline,
        Character character,
        IVoiceParameter parameter,
        Type effectType)
    {
        const string serif = "え<w0>ええ";
        const string hatsuon = "エエエ";

        var voice = CreateVoice(
            character,
            parameter,
            serif,
            hatsuon,
            "A2_COMBINED");

        Check(
            "combined_voice_added",
            timeline.TryAddItems(
                [voice],
                600,
                9));

        await voice.CreateVoiceFileAsync();

        var path = RequireVoicePath(
            voice,
            "combined");

        var effect = CreateAssistEffect(
            effectType,
            """
            {"version":1,"rules":[{"kind":"zeroVowel","helper":"ウ","anchor":{"position":2,"left":"ええ","right":"え"}}]}
            """);

        AppendEffect(voice, effect);

        await WaitUntil(
            "combined A1 A2 apply",
            () =>
                File.Exists(path)
                && new FileInfo(path).Length == 5444
                && CombinedCorrectionApplied(voice));

        Check(
            "combined_a1_a2_applied",
            CombinedCorrectionApplied(voice));

        Check(
            "combined_persisted_source_unchanged",
            voice.Serif == serif
            && voice.Hatsuon == hatsuon);

        return new
        {
            voice.Serif,
            voice.Hatsuon,
            fileLength = new FileInfo(path).Length,
            pauseVowelLength = GetFirstPauseLength(voice),
            helperVowelLength = GetMoras(voice)
                .Where(x => GetText(x) == "ウ")
                .Select(x =>
                    GetNullableDouble(
                        x,
                        "VowelLength"))
                .SingleOrDefault(),
        };
    }

    static bool CombinedCorrectionApplied(
        VoiceItem voice)
    {
        var helper = GetMoras(voice)
            .Where(x => GetText(x) == "ウ")
            .ToArray();

        return helper.Length == 1
            && GetNullableDouble(
                helper[0],
                "VowelLength") == 0.0
            && GetFirstPauseLength(voice) == 0.0;
    }

    static double? GetFirstPauseLength(
        VoiceItem voice)
    {
        var pronounce = voice.Pronounce;
        if (pronounce is null)
            return null;

        var audioQuery = pronounce.GetType()
            .GetProperty(
                "AudioQuery",
                BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(pronounce);

        if (audioQuery is null)
            return null;

        if (audioQuery.GetType()
            .GetProperty(
                "AccentPhrases",
                BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(audioQuery)
            is not IEnumerable phrases)
        {
            return null;
        }

        var first = phrases
            .Cast<object>()
            .FirstOrDefault();

        if (first is null)
            return null;

        var pause = first.GetType()
            .GetProperty(
                "PauseMora",
                BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(first);

        if (pause is null)
            return null;

        return GetNullableDouble(
            pause,
            "VowelLength");
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
