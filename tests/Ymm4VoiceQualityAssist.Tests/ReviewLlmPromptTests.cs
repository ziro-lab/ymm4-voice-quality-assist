using Ymm4VoiceQualityAssist.Core;

namespace Ymm4VoiceQualityAssist.Tests;

public sealed class ReviewLlmPromptTests
{
    [Fact]
    public void Prompt_ContainsFrozenWorkflowAndEmbeddedPackage()
    {
        var package =
            new ReviewExportPackage(
                ReviewExportBuilder.Schema,
                "session-prompt",
                DateTimeOffset.UnixEpoch,
                [
                    new ReviewVoiceExportRecord(
                        new ReviewVoiceTarget(
                            "voice-000000",
                            0,
                            10,
                            2),
                        "sha256:"
                        + new string('a', 64),
                        "小夜",
                        new ReviewVoiceSpeaker(
                            "VOICEVOX",
                            "speaker-1"),
                        "東京<w0>大学",
                        "トウキョウダイガク",
                        new ReviewVoiceContext(
                            "前",
                            "後"),
                        new ReviewVoiceControls(
                            "東京大学",
                            [
                                new ReviewVoiceBoundary(
                                    2,
                                    "w0"),
                            ]),
                        new ReviewVoiceAssistSettings(
                            false,
                            []),
                        new ReviewVoicePronunciationSummary(
                            false,
                            null,
                            null)),
                ]);

        var prompt =
            ReviewLlmPrompt.Build(
                package);

        Assert.Contains(
            "Return ONLY valid JSON",
            prompt,
            StringComparison.Ordinal);

        Assert.Contains(
            "exactly one correction record for every exported voice",
            prompt,
            StringComparison.Ordinal);

        Assert.Contains(
            "proper nouns",
            prompt,
            StringComparison.Ordinal);

        Assert.Contains(
            "forced VOICEVOX automatic-accent phrase boundary",
            prompt,
            StringComparison.Ordinal);

        Assert.Contains(
            "does NOT mean manually editing VOICEVOX accent notation or merely zeroing an existing source punctuation pause",
            prompt,
            StringComparison.Ordinal);

        Assert.Contains(
            "source is \"w0\"",
            prompt,
            StringComparison.Ordinal);


        foreach (var operation in new[]
        {
            "setReading",
            "addBoundary",
            "removeBoundary",
            "helperVowelZero",
            "helperConsonantZero",
            "setProsodyGesture",
            "noChange",
        })
        {
            Assert.Contains(
                operation,
                prompt,
                StringComparison.Ordinal);
        }

        Assert.Contains(
            "\"exportSessionId\": \"session-prompt\"",
            prompt,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"exportRef\": \"voice-000000\"",
            prompt,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"serif\": \"東京<w0>大学\"",
            prompt,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"previousSerif\": \"前\"",
            prompt,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"cleanText\": \"東京大学\"",
            prompt,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            new string((char)96, 3),
            prompt,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Prompt_OnlyOffersSupportedProsodyGestures()
    {
        var prompt =
            ReviewLlmPrompt.Build(
                new ReviewExportPackage(
                    ReviewExportBuilder.Schema,
                    "session",
                    DateTimeOffset.UnixEpoch,
                    []));

        Assert.Contains(
            "\"none\"",
            prompt,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"lightRise\"",
            prompt,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"lightFall\"",
            prompt,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"hold\"",
            prompt,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "surprise-like",
            prompt,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "sigh-like",
            prompt,
            StringComparison.Ordinal);
    }
}
