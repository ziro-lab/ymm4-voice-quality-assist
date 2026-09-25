using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Ymm4VoiceQualityAssist.Effects;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4VoiceQualityAssist.Core;

public enum PronunciationAssistStorageKind
{
    AudioEffect,
    LegacySubtitleEffect,
}

public sealed record PronunciationAssistSettingsEntry(
    IPronunciationAssistSettings Settings,
    PronunciationAssistStorageKind Storage);

public sealed record PronunciationAssistSettingsSnapshot(
    bool IsEnabled,
    string HelperRulesJson,
    ProsodyGesture Prosody)
{
    public static PronunciationAssistSettingsSnapshot Capture(
        IPronunciationAssistSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new(
            settings.IsEnabled,
            settings.HelperRulesJson,
            settings.Prosody);
    }

    public void ApplyTo(
        IPronunciationAssistSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.IsEnabled = IsEnabled;
        settings.HelperRulesJson = HelperRulesJson;
        settings.Prosody = Prosody;
    }
}

public static class PronunciationAssistSettingsStore
{
    static readonly ConditionalWeakTable<
        IPronunciationAssistSettings,
        OwnerReference> Owners =
        new();

    static readonly HashSet<string> StorageCollectionProperties =
        new(StringComparer.Ordinal)
        {
            nameof(VoiceItem.AudioEffects),
            nameof(VoiceItem.JimakuVideoEffects),
        };

    public static bool IsStorageCollectionProperty(
        string? propertyName) =>
        propertyName is not null
        && StorageCollectionProperties.Contains(
            propertyName);

    public static IReadOnlyList<PronunciationAssistSettingsEntry>
        EnumerateEntries(
            VoiceItem voice)
    {
        ArgumentNullException.ThrowIfNull(voice);

        var result =
            new List<PronunciationAssistSettingsEntry>();

        if (voice.AudioEffects is IEnumerable audioEffects)
        {
            result.AddRange(
                audioEffects
                    .Cast<object>()
                    .OfType<PronunciationAssistAudioEffect>()
                    .Select(x =>
                        new PronunciationAssistSettingsEntry(
                            x,
                            PronunciationAssistStorageKind.AudioEffect)));
        }

        if (voice.JimakuVideoEffects is IEnumerable legacyEffects)
        {
            result.AddRange(
                legacyEffects
                    .Cast<object>()
                    .OfType<PronunciationAssistEffect>()
                    .Select(x =>
                        new PronunciationAssistSettingsEntry(
                            x,
                            PronunciationAssistStorageKind.LegacySubtitleEffect)));
        }

        foreach (var entry in result)
        {
            BindOwner(
                entry.Settings,
                voice);
        }

        return result;
    }

    public static bool TryGetOwner(
        IPronunciationAssistSettings settings,
        out VoiceItem? voice)
    {
        ArgumentNullException.ThrowIfNull(
            settings);

        if (Owners.TryGetValue(
                settings,
                out var owner))
        {
            voice =
                owner.Voice;

            return true;
        }

        voice = null;
        return false;
    }

    public static IReadOnlyList<IPronunciationAssistSettings>
        Enumerate(
            VoiceItem voice) =>
        EnumerateEntries(voice)
            .Select(x => x.Settings)
            .ToArray();

    public static IReadOnlyList<PronunciationAssistAudioEffect>
        EnumerateAudio(
            VoiceItem voice) =>
        EnumerateEntries(voice)
            .Where(x =>
                x.Storage
                    == PronunciationAssistStorageKind.AudioEffect)
            .Select(x =>
                (PronunciationAssistAudioEffect)x.Settings)
            .ToArray();

    public static IReadOnlyList<PronunciationAssistEffect>
        EnumerateLegacy(
            VoiceItem voice) =>
        EnumerateEntries(voice)
            .Where(x =>
                x.Storage
                    == PronunciationAssistStorageKind.LegacySubtitleEffect)
            .Select(x =>
                (PronunciationAssistEffect)x.Settings)
            .ToArray();

    public static IPronunciationAssistSettings
        CreateCanonical() =>
        new PronunciationAssistAudioEffect
        {
            IsEnabled = true,
            HelperRulesJson = string.Empty,
            Prosody = ProsodyGesture.None,
        };

    public static bool Contains(
        VoiceItem voice,
        IPronunciationAssistSettings settings)
    {
        ArgumentNullException.ThrowIfNull(voice);
        ArgumentNullException.ThrowIfNull(settings);

        return EnumerateEntries(voice)
            .Any(x =>
                ReferenceEquals(
                    x.Settings,
                    settings));
    }

    public static bool TryAdd(
        VoiceItem voice,
        IPronunciationAssistSettings settings,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(voice);
        ArgumentNullException.ThrowIfNull(settings);

        if (Contains(voice, settings))
        {
            BindOwner(
                settings,
                voice);

            error = null;
            return true;
        }

        var added =
            settings switch
            {
                PronunciationAssistAudioEffect audio =>
                    TryMutatePublicCollection(
                        voice,
                        nameof(VoiceItem.AudioEffects),
                        audio,
                        add: true,
                        out error),

                PronunciationAssistEffect legacy =>
                    TryMutatePublicCollection(
                        voice,
                        nameof(VoiceItem.JimakuVideoEffects),
                        legacy,
                        add: true,
                        out error),

                _ => Unsupported(
                    settings,
                    out error),
            };

        if (added)
        {
            BindOwner(
                settings,
                voice);
        }

        return added;
    }

    public static bool TryRemove(
        VoiceItem voice,
        IPronunciationAssistSettings settings,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(voice);
        ArgumentNullException.ThrowIfNull(settings);

        if (!Contains(voice, settings))
        {
            error = null;
            return true;
        }

        return settings switch
        {
            PronunciationAssistAudioEffect audio =>
                TryMutatePublicCollection(
                    voice,
                    nameof(VoiceItem.AudioEffects),
                    audio,
                    add: false,
                    out error),

            PronunciationAssistEffect legacy =>
                TryMutatePublicCollection(
                    voice,
                    nameof(VoiceItem.JimakuVideoEffects),
                    legacy,
                    add: false,
                    out error),

            _ => Unsupported(
                settings,
                out error),
        };
    }

    static bool Unsupported(
        IPronunciationAssistSettings settings,
        out string? error)
    {
        error =
            "Unsupported pronunciation assist settings type: "
            + settings.GetType().FullName;
        return false;
    }

    static bool TryMutatePublicCollection(
        VoiceItem voice,
        string propertyName,
        object effect,
        bool add,
        out string? error)
    {
        error = null;

        var property =
            typeof(VoiceItem).GetProperty(
                propertyName,
                BindingFlags.Instance
                | BindingFlags.Public);

        if (property?.GetMethod?.IsPublic != true)
        {
            error =
                $"VoiceItem.{propertyName} public getter was not found.";
            return false;
        }

        var current =
            property.GetValue(voice);

        if (current is null)
        {
            error =
                $"VoiceItem.{propertyName} returned null.";
            return false;
        }

        if (current is IList list
            && !list.IsReadOnly
            && !list.IsFixedSize)
        {
            try
            {
                if (add)
                    list.Add(effect);
                else
                    list.Remove(effect);

                if (add
                    == CollectionContains(
                        voice,
                        propertyName,
                        effect))
                {
                    return true;
                }

                error =
                    $"{propertyName} mutable collection did not reflect the requested membership change.";
                return false;
            }
            catch (Exception ex)
            {
                error =
                    ex.GetBaseException().Message;
                return false;
            }
        }

        var methodName =
            add ? "Add" : "Remove";

        var method =
            current.GetType()
                .GetMethods(
                    BindingFlags.Instance
                    | BindingFlags.Public)
                .FirstOrDefault(x =>
                    x.Name == methodName
                    && x.GetParameters().Length == 1
                    && x.GetParameters()[0]
                        .ParameterType
                        .IsAssignableFrom(
                            effect.GetType()));

        if (method is null)
        {
            error =
                $"{propertyName} exposes no supported public {methodName}(effect) route.";
            return false;
        }

        object? returned;

        try
        {
            returned =
                method.Invoke(
                    current,
                    [effect]);
        }
        catch (Exception ex)
        {
            error =
                ex.GetBaseException().Message;
            return false;
        }

        if (returned is not null
            && property.PropertyType
                .IsInstanceOfType(returned)
            && !ReferenceEquals(
                returned,
                current))
        {
            if (property.SetMethod?.IsPublic != true)
            {
                error =
                    $"{propertyName} returned an updated collection but has no public setter.";
                return false;
            }

            try
            {
                property.SetValue(
                    voice,
                    returned);
            }
            catch (Exception ex)
            {
                error =
                    ex.GetBaseException().Message;
                return false;
            }
        }

        if (add
            == CollectionContains(
                voice,
                propertyName,
                effect))
        {
            return true;
        }

        error =
            $"{propertyName} public {methodName} route did not produce the requested membership state.";
        return false;
    }

    static void BindOwner(
        IPronunciationAssistSettings settings,
        VoiceItem voice)
    {
        Owners.Remove(
            settings);

        Owners.Add(
            settings,
            new OwnerReference(
                voice));
    }

    sealed class OwnerReference(
        VoiceItem voice)
    {
        public VoiceItem Voice { get; } =
            voice;
    }

    static bool CollectionContains(
        VoiceItem voice,
        string propertyName,
        object effect)
    {
        var property =
            typeof(VoiceItem).GetProperty(
                propertyName,
                BindingFlags.Instance
                | BindingFlags.Public);

        return property?.GetValue(voice)
            is IEnumerable values
            && values
                .Cast<object>()
                .Any(x =>
                    ReferenceEquals(
                        x,
                        effect));
    }
}
