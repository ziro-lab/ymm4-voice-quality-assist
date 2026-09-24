using Ymm4VoiceQualityAssist.Core;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Audio.Effects;
using YukkuriMovieMaker.Plugin.Effects;

namespace Ymm4VoiceQualityAssist.Effects;

[AudioEffect("発音補助", ["音声", "発音補助"], [])]
public sealed class PronunciationAssistAudioEffect
    : AudioEffectBase, IPronunciationAssistSettings
{
    public override string Label => "発音補助";

    public string HelperRulesJson
    {
        get => field;
        set => Set(ref field, value);
    } = string.Empty;

    public ProsodyGesture Prosody
    {
        get => field;
        set => Set(ref field, value);
    } = ProsodyGesture.None;

    public override IAudioEffectProcessor CreateAudioEffect(
        TimeSpan duration) =>
        new PronunciationAssistPassThroughAudioProcessor(duration);

    public override IEnumerable<string> CreateExoAudioFilters(
        int keyFrameIndex,
        ExoOutputDescription exoOutputDescription) => [];

    protected override IEnumerable<IAnimatable> GetAnimatables() => [];
}

internal sealed class PronunciationAssistPassThroughAudioProcessor
    : AudioEffectProcessorBase
{
    readonly TimeSpan duration;

    public PronunciationAssistPassThroughAudioProcessor(
        TimeSpan duration) =>
        this.duration = duration;

    public override int Hz => Input?.Hz ?? 0;

    public override long Duration =>
        Input is not null
            ? Input.Duration
            : (long)(duration.TotalSeconds * Hz) * 2;

    protected override void seek(long position) =>
        Input?.Seek(position);

    protected override int read(
        float[] destBuffer,
        int offset,
        int count) =>
        Input?.Read(destBuffer, offset, count) ?? 0;
}
