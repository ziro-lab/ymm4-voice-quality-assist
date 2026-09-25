using System.ComponentModel.DataAnnotations;
using Ymm4VoiceQualityAssist.Core;
using Ymm4VoiceQualityAssist.Editors;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Audio.Effects;
using YukkuriMovieMaker.Plugin.Effects;

namespace Ymm4VoiceQualityAssist.Effects;

[AudioEffect("発音補助", ["音声", "発音補助"], [])]
public sealed class PronunciationAssistAudioEffect
    : AudioEffectBase, IPronunciationAssistSettings
{
    public override string Label => "発音補助";

    [Display(
        GroupName = "発音補助",
        Name = "強制区切り入力記号",
        Description = "編集用の記号です。Voice Quality Assistツールの明示変換でcanonical <w0>へ置き換えます。")]
    [TextEditor]
    public string BoundaryInputToken
    {
        get => field;
        set
        {
            if (ForcedBoundaryInputToken.TryValidate(
                    value,
                    out _))
            {
                Set(ref field, value);
            }
        }
    } = ForcedBoundaryInputToken.Default;

    [Display(
        GroupName = "発音補助",
        Name = "補助モーラ",
        Description = "VOICEVOX解析時だけ補助モーラを一時挿入します。")]
    [HelperRulesEditor]
    public string HelperRulesJson
    {
        get => field;
        set => Set(ref field, value);
    } = string.Empty;

    [Display(
        GroupName = "発音補助",
        Name = "抑揚",
        Description = "VOICEVOXが生成した抑揚を基準に、軽い補正だけを加えます。")]
    [EnumComboBox]
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
