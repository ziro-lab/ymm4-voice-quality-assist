using Ymm4VoiceQualityAssist.Core;

namespace Ymm4VoiceQualityAssist.Tests;

public sealed class ReviewExportCsvTests
{
    [Fact]
    public void Serialize_UsesStableColumnOrderAndCrLf()
    {
        var csv =
            ReviewExportCsv.Serialize(
                Package(
                    serif: "本文",
                    hatsuon: "ホンブン"));

        var firstLine =
            csv.Split(
                "\r\n",
                StringSplitOptions.None)[0];

        Assert.Equal(
            "\"schema\",\"exportSessionId\",\"exportedAt\",\"exportRef\",\"exportIndex\",\"frame\",\"layer\",\"characterName\",\"speakerApi\",\"speakerId\",\"previousSerif\",\"serif\",\"hatsuon\",\"nextSerif\",\"cleanText\",\"boundaries\",\"prosody\",\"helperRules\",\"sourceFingerprint\"",
            firstLine);

        Assert.EndsWith(
            "\r\n",
            csv,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_EscapesCommaQuoteAndEmbeddedNewline()
    {
        const string serif =
            "A,\"B\"\nC";

        var csv =
            ReviewExportCsv.Serialize(
                Package(
                    serif,
                    hatsuon: "エー"));

        Assert.Contains(
            "\"A,\"\"B\"\"\nC\"",
            csv,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_FlattensBoundariesAndAssistForHumanReview()
    {
        var package =
            new ReviewExportPackage(
                ReviewExportBuilder.Schema,
                "session-csv",
                new DateTimeOffset(
                    2026,
                    9,
                    24,
                    9,
                    0,
                    0,
                    TimeSpan.Zero),
                [
                    new ReviewVoiceExportRecord(
                        new ReviewVoiceTarget(
                            "voice-000000",
                            0,
                            100,
                            2),
                        "sha256:"
                        + new string('a', 64),
                        "小夜",
                        new ReviewVoiceSpeaker(
                            "VOICEVOX",
                            "speaker-1"),
                        "A<w0>B",
                        "エービー",
                        new ReviewVoiceContext(
                            "前",
                            "後"),
                        new ReviewVoiceControls(
                            "AB",
                            [
                                new ReviewVoiceBoundary(
                                    1,
                                    "w0"),
                            ]),
                        new ReviewVoiceAssistSettings(
                            true,
                            [
                                new SourceFingerprintAssistProfile(
                                    ProsodyGesture.LightRise,
                                    [
                                        new SourceFingerprintHelperRule(
                                            HelperMoraKind.ZeroVowel,
                                            "ウ",
                                            1,
                                            "A",
                                            "B"),
                                    ]),
                            ]),
                        new ReviewVoicePronunciationSummary(
                            true,
                            "エービー",
                            1)),
                ]);

        var csv =
            ReviewExportCsv.Serialize(
                package);

        Assert.Contains(
            "\"1:w0\"",
            csv,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"lightRise\"",
            csv,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"zeroVowel:ウ@1[A|B]\"",
            csv,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"VOICEVOX\"",
            csv,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"2026-09-24T09:00:00.0000000+00:00\"",
            csv,
            StringComparison.Ordinal);
    }

    static ReviewExportPackage Package(
        string serif,
        string hatsuon) =>
        new(
            ReviewExportBuilder.Schema,
            "session-csv",
            DateTimeOffset.UnixEpoch,
            [
                new ReviewVoiceExportRecord(
                    new ReviewVoiceTarget(
                        "voice-000000",
                        0,
                        0,
                        0),
                    "sha256:"
                    + new string('a', 64),
                    null,
                    new ReviewVoiceSpeaker(
                        null,
                        null),
                    serif,
                    hatsuon,
                    new ReviewVoiceContext(
                        null,
                        null),
                    new ReviewVoiceControls(
                        serif,
                        []),
                    new ReviewVoiceAssistSettings(
                        false,
                        []),
                    new ReviewVoicePronunciationSummary(
                        false,
                        null,
                        null)),
            ]);
}
