using Ymm4VoiceQualityAssist.Core;

namespace Ymm4VoiceQualityAssist.Tests;

public sealed class ReviewCorrectionJsonTests
{
    const string FingerprintA =
        "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    const string FingerprintB =
        "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void DecodeAndValidate_CompleteStructuredResponse_Passes()
    {
        var wire =
            new ReviewCorrectionWirePackage(
                ReviewCorrectionValidator.Schema,
                "session-b2",
                [
                    new ReviewCorrectionWireRecord(
                        "voice-000000",
                        FingerprintA,
                        [
                            new ReviewCorrectionWireOperation(
                                "setReading",
                                Reading: "トウキョウダイガク"),
                            new ReviewCorrectionWireOperation(
                                "addBoundary",
                                Position: 2),
                            new ReviewCorrectionWireOperation(
                                "helperVowelZero",
                                Position: 0,
                                Helper: "ウ"),
                            new ReviewCorrectionWireOperation(
                                "setProsodyGesture",
                                Gesture: "lightRise"),
                        ]),
                    new ReviewCorrectionWireRecord(
                        "voice-000001",
                        FingerprintB,
                        [
                            new ReviewCorrectionWireOperation(
                                "noChange"),
                        ]),
                ]);

        var json =
            ReviewCorrectionJson.Serialize(
                wire);

        var result =
            ReviewCorrectionJson
                .DecodeAndValidateAgainstExport(
                    json,
                    ExportPackage());

        Assert.True(
            result.IsSuccess,
            string.Join(
                Environment.NewLine,
                result.Errors.Select(
                    x => x.Code + ": " + x.Message)));

        Assert.Equal(
            2,
            result.Proposals.Count);

        var first =
            result.Proposals[0];

        Assert.Collection(
            first.Operations,
            x => Assert.IsType<
                SetReadingCorrection>(x),
            x => Assert.IsType<
                AddBoundaryCorrection>(x),
            x => Assert.IsType<
                HelperVowelZeroCorrection>(x),
            x =>
            {
                var prosody =
                    Assert.IsType<
                        SetProsodyGestureCorrection>(x);

                Assert.Equal(
                    ProsodyGesture.LightRise,
                    prosody.Gesture);
            });

        Assert.IsType<NoChangeCorrection>(
            Assert.Single(
                result.Proposals[1].Operations));
    }

    [Fact]
    public void AddBoundary_WireShape_RemainsTypeAndPositionOnly()
    {
        var wire =
            new ReviewCorrectionWirePackage(
                ReviewCorrectionValidator.Schema,
                "session-b2",
                [
                    new ReviewCorrectionWireRecord(
                        "voice-000000",
                        FingerprintA,
                        [
                            new ReviewCorrectionWireOperation(
                                "addBoundary",
                                Position: 2),
                        ]),
                ]);

        var json =
            ReviewCorrectionJson.Serialize(
                wire);

        Assert.Contains(
            "\"type\": \"addBoundary\"",
            json,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"position\": 2",
            json,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "pause",
            json,
            StringComparison.OrdinalIgnoreCase);

        var decoded =
            ReviewCorrectionJson.Decode(
                json);

        var proposal =
            Assert.Single(
                decoded.Proposals);

        var boundary =
            Assert.IsType<AddBoundaryCorrection>(
                Assert.Single(
                    proposal.Operations));

        Assert.Equal(
            2,
            boundary.CleanTextPosition);
    }

    [Fact]
    public void Decode_InvalidJson_Fails()
    {
        var result =
            ReviewCorrectionJson.Decode(
                "{broken");

        Assert.False(
            result.IsSuccess);

        Assert.Contains(
            result.Errors,
            x =>
                x.Code
                == ReviewCorrectionWireErrorCode
                    .InvalidJson);
    }

    [Fact]
    public void Decode_UnknownOperation_Fails()
    {
        var json =
            ReviewCorrectionJson.Serialize(
                new ReviewCorrectionWirePackage(
                    ReviewCorrectionValidator.Schema,
                    "session-b2",
                    [
                        new ReviewCorrectionWireRecord(
                            "voice-000000",
                            FingerprintA,
                            [
                                new ReviewCorrectionWireOperation(
                                    "rewriteEverything"),
                            ]),
                    ]));

        var result =
            ReviewCorrectionJson.Decode(
                json);

        Assert.Contains(
            result.Errors,
            x =>
                x.Code
                == ReviewCorrectionWireErrorCode
                    .UnknownOperation);
    }

    [Fact]
    public void Decode_MissingPayload_Fails()
    {
        var json =
            ReviewCorrectionJson.Serialize(
                new ReviewCorrectionWirePackage(
                    ReviewCorrectionValidator.Schema,
                    "session-b2",
                    [
                        new ReviewCorrectionWireRecord(
                            "voice-000000",
                            FingerprintA,
                            [
                                new ReviewCorrectionWireOperation(
                                    "addBoundary"),
                            ]),
                    ]));

        var result =
            ReviewCorrectionJson.Decode(
                json);

        Assert.Contains(
            result.Errors,
            x =>
                x.Code
                == ReviewCorrectionWireErrorCode
                    .InvalidPayload);
    }

    [Fact]
    public void Validate_OmittedVoice_FailsCompleteCoverage()
    {
        var json =
            ReviewCorrectionJson.Serialize(
                new ReviewCorrectionWirePackage(
                    ReviewCorrectionValidator.Schema,
                    "session-b2",
                    [
                        new ReviewCorrectionWireRecord(
                            "voice-000000",
                            FingerprintA,
                            [
                                new ReviewCorrectionWireOperation(
                                    "noChange"),
                            ]),
                    ]));

        var result =
            ReviewCorrectionJson
                .DecodeAndValidateAgainstExport(
                    json,
                    ExportPackage());

        Assert.Contains(
            result.Errors,
            x =>
                x.Code
                == ReviewCorrectionWireErrorCode
                    .IncompleteCoverage);
    }

    [Fact]
    public void Validate_DuplicateExportRef_IsRejected()
    {
        var json =
            ReviewCorrectionJson.Serialize(
                new ReviewCorrectionWirePackage(
                    ReviewCorrectionValidator.Schema,
                    "session-b2",
                    [
                        NoChange(
                            "voice-000000",
                            FingerprintA),
                        NoChange(
                            "voice-000000",
                            FingerprintA),
                        NoChange(
                            "voice-000001",
                            FingerprintB),
                    ]));

        var result =
            ReviewCorrectionJson
                .DecodeAndValidateAgainstExport(
                    json,
                    ExportPackage());

        Assert.Contains(
            result.Errors,
            x =>
                x.Code
                == ReviewCorrectionWireErrorCode
                    .DuplicateExportRef);
    }

    [Fact]
    public void Validate_UnknownExportRef_Fails()
    {
        var json =
            ReviewCorrectionJson.Serialize(
                new ReviewCorrectionWirePackage(
                    ReviewCorrectionValidator.Schema,
                    "session-b2",
                    [
                        NoChange(
                            "voice-000000",
                            FingerprintA),
                        NoChange(
                            "voice-999999",
                            FingerprintB),
                    ]));

        var result =
            ReviewCorrectionJson
                .DecodeAndValidateAgainstExport(
                    json,
                    ExportPackage());

        Assert.Contains(
            result.Errors,
            x =>
                x.Code
                == ReviewCorrectionWireErrorCode
                    .UnknownExportRef);
    }

    [Fact]
    public void Validate_FingerprintMismatch_Fails()
    {
        var json =
            ReviewCorrectionJson.Serialize(
                new ReviewCorrectionWirePackage(
                    ReviewCorrectionValidator.Schema,
                    "session-b2",
                    [
                        NoChange(
                            "voice-000000",
                            FingerprintB),
                        NoChange(
                            "voice-000001",
                            FingerprintB),
                    ]));

        var result =
            ReviewCorrectionJson
                .DecodeAndValidateAgainstExport(
                    json,
                    ExportPackage());

        Assert.Contains(
            result.Errors,
            x =>
                x.Code
                == ReviewCorrectionWireErrorCode
                    .FingerprintMismatch
                && x.ExportRef
                    == "voice-000000");
    }

    [Fact]
    public void Validate_SessionMismatch_Fails()
    {
        var json =
            ReviewCorrectionJson.Serialize(
                new ReviewCorrectionWirePackage(
                    ReviewCorrectionValidator.Schema,
                    "wrong-session",
                    [
                        NoChange(
                            "voice-000000",
                            FingerprintA),
                        NoChange(
                            "voice-000001",
                            FingerprintB),
                    ]));

        var result =
            ReviewCorrectionJson
                .DecodeAndValidateAgainstExport(
                    json,
                    ExportPackage());

        Assert.Contains(
            result.Errors,
            x =>
                x.Code
                == ReviewCorrectionWireErrorCode
                    .SessionMismatch);
    }

    [Fact]
    public void Validate_DomainRulesStillApplyAfterWireConversion()
    {
        var json =
            ReviewCorrectionJson.Serialize(
                new ReviewCorrectionWirePackage(
                    ReviewCorrectionValidator.Schema,
                    "session-b2",
                    [
                        new ReviewCorrectionWireRecord(
                            "voice-000000",
                            FingerprintA,
                            [
                                new ReviewCorrectionWireOperation(
                                    "noChange"),
                                new ReviewCorrectionWireOperation(
                                    "setProsodyGesture",
                                    Gesture: "hold"),
                            ]),
                        NoChange(
                            "voice-000001",
                            FingerprintB),
                    ]));

        var result =
            ReviewCorrectionJson
                .DecodeAndValidateAgainstExport(
                    json,
                    ExportPackage());

        Assert.Contains(
            result.Errors,
            x =>
                x.Code
                == ReviewCorrectionWireErrorCode
                    .DomainValidationFailed
                && x.Message.Contains(
                    nameof(
                        ReviewCorrectionValidationCode
                            .NoChangeMixedWithOperations),
                    StringComparison.Ordinal));
    }

    static ReviewCorrectionWireRecord NoChange(
        string exportRef,
        string fingerprint) =>
        new(
            exportRef,
            fingerprint,
            [
                new ReviewCorrectionWireOperation(
                    "noChange"),
            ]);

    static ReviewExportPackage ExportPackage() =>
        new(
            ReviewExportBuilder.Schema,
            "session-b2",
            DateTimeOffset.UnixEpoch,
            [
                Voice(
                    "voice-000000",
                    0,
                    FingerprintA,
                    "東京大学",
                    "トウキョウダイガク"),
                Voice(
                    "voice-000001",
                    1,
                    FingerprintB,
                    "次",
                    "ツギ"),
            ]);

    static ReviewVoiceExportRecord Voice(
        string exportRef,
        int exportIndex,
        string fingerprint,
        string cleanText,
        string hatsuon) =>
        new(
            new ReviewVoiceTarget(
                exportRef,
                exportIndex,
                exportIndex * 100,
                1),
            fingerprint,
            "小夜",
            new ReviewVoiceSpeaker(
                "VOICEVOX",
                "speaker-1"),
            cleanText,
            hatsuon,
            new ReviewVoiceContext(
                null,
                null),
            new ReviewVoiceControls(
                cleanText,
                []),
            new ReviewVoiceAssistSettings(
                false,
                []),
            new ReviewVoicePronunciationSummary(
                false,
                null,
                null));
}
