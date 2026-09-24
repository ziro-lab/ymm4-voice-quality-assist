using Ymm4VoiceQualityAssist.Core;
using Ymm4VoiceQualityAssist.Effects;
using YukkuriMovieMaker.Project.Items;

namespace Ymm4VoiceQualityAssist.Tests;

public sealed class ReviewAssistEffectCollectionTests
{
    [Fact]
    public void PublicAdapter_AddsAndRemovesProductEffect()
    {
        var voice = new VoiceItem();
        var effect =
            new PronunciationAssistEffect
            {
                IsEnabled = true,
                Prosody =
                    ProsodyGesture.LightRise,
            };

        Assert.Empty(
            ReviewAssistEffectCollection.Enumerate(
                voice));

        Assert.True(
            ReviewAssistEffectCollection.TryAdd(
                voice,
                effect,
                out var addError),
            addError);

        Assert.True(
            ReviewAssistEffectCollection.Contains(
                voice,
                effect));

        Assert.Same(
            effect,
            Assert.Single(
                ReviewAssistEffectCollection.Enumerate(
                    voice)));

        Assert.True(
            ReviewAssistEffectCollection.TryRemove(
                voice,
                effect,
                out var removeError),
            removeError);

        Assert.False(
            ReviewAssistEffectCollection.Contains(
                voice,
                effect));
    }

    [Fact]
    public void MembershipOperations_AreIdempotent()
    {
        var voice = new VoiceItem();
        var effect =
            new PronunciationAssistEffect();

        Assert.True(
            ReviewAssistEffectCollection.TryAdd(
                voice,
                effect,
                out var firstAdd),
            firstAdd);

        Assert.True(
            ReviewAssistEffectCollection.TryAdd(
                voice,
                effect,
                out var secondAdd),
            secondAdd);

        Assert.Single(
            ReviewAssistEffectCollection.Enumerate(
                voice));

        Assert.True(
            ReviewAssistEffectCollection.TryRemove(
                voice,
                effect,
                out var firstRemove),
            firstRemove);

        Assert.True(
            ReviewAssistEffectCollection.TryRemove(
                voice,
                effect,
                out var secondRemove),
            secondRemove);

        Assert.Empty(
            ReviewAssistEffectCollection.Enumerate(
                voice));
    }
}
