using Vortice.Direct2D1;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Video;
using YukkuriMovieMaker.Plugin.Effects;

namespace Ymm4VoiceQualityAssist.Effects;

[VideoEffect("発音補助", ["音声", "発音補助"], [])]
public sealed class PronunciationAssistEffect : VideoEffectBase
{
    public override string Label => "発音補助";

    public override IEnumerable<string> CreateExoVideoFilters(
        int keyFrameIndex,
        ExoOutputDescription exoOutputDescription) => [];

    public override IVideoEffectProcessor CreateVideoEffect(
        IGraphicsDevicesAndContext devices) => new PassThroughProcessor();

    protected override IEnumerable<IAnimatable> GetAnimatables() => [];
}

internal sealed class PassThroughProcessor : IVideoEffectProcessor
{
    ID2D1Image? input;

    public ID2D1Image Output =>
        input ?? throw new InvalidOperationException("Input image is not set.");

    public DrawDescription Update(EffectDescription effectDescription) =>
        effectDescription.DrawDescription;

    public void SetInput(ID2D1Image? value) => input = value;
    public void ClearInput() => input = null;
    public void Dispose() { }
}
