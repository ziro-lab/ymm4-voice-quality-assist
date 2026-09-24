using System.Collections;
using System.Reflection;
using Ymm4VoiceQualityAssist.Effects;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4VoiceQualityAssist.Core;

public static class ReviewAssistEffectCollection
{
    public static IReadOnlyList<PronunciationAssistEffect> Enumerate(
        VoiceItem voice)
    {
        ArgumentNullException.ThrowIfNull(voice);

        if (voice.JimakuVideoEffects
            is not IEnumerable effects)
        {
            return [];
        }

        return effects
            .Cast<object>()
            .OfType<PronunciationAssistEffect>()
            .ToArray();
    }

    public static bool Contains(
        VoiceItem voice,
        PronunciationAssistEffect effect) =>
        Enumerate(voice).Any(
            x => ReferenceEquals(x, effect));

    public static bool TryAdd(
        VoiceItem voice,
        PronunciationAssistEffect effect,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(voice);
        ArgumentNullException.ThrowIfNull(effect);

        if (Contains(voice, effect))
        {
            error = null;
            return true;
        }

        return TryMutate(
            voice,
            effect,
            add: true,
            out error);
    }

    public static bool TryRemove(
        VoiceItem voice,
        PronunciationAssistEffect effect,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(voice);
        ArgumentNullException.ThrowIfNull(effect);

        if (!Contains(voice, effect))
        {
            error = null;
            return true;
        }

        return TryMutate(
            voice,
            effect,
            add: false,
            out error);
    }

    static bool TryMutate(
        VoiceItem voice,
        PronunciationAssistEffect effect,
        bool add,
        out string? error)
    {
        error = null;

        var property =
            typeof(VoiceItem).GetProperty(
                nameof(VoiceItem.JimakuVideoEffects),
                BindingFlags.Instance
                | BindingFlags.Public);

        if (property?.GetMethod?.IsPublic != true)
        {
            error =
                "VoiceItem.JimakuVideoEffects public getter was not found.";
            return false;
        }

        var current =
            property.GetValue(voice);

        if (current is null)
        {
            error =
                "VoiceItem.JimakuVideoEffects returned null.";
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

                if (add == Contains(voice, effect))
                    return true;

                error =
                    "Mutable JimakuVideoEffects did not reflect the requested membership change.";
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
                $"JimakuVideoEffects exposes no supported public {methodName}(effect) route.";
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

        // Immutable collection APIs commonly return the new collection.
        // A void/bool-returning mutator may already have updated in place.
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
                    "JimakuVideoEffects returned an updated collection but has no public setter.";
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

        if (add == Contains(voice, effect))
            return true;

        error =
            $"JimakuVideoEffects public {methodName} route did not produce the requested membership state.";
        return false;
    }
}
