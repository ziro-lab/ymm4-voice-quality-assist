using Ymm4VoiceQualityAssist.Core;

namespace Ymm4VoiceQualityAssist.Tests;

public sealed class ProsodyPitchPlannerTests
{
    [Fact]
    public void None_PreservesBaselineExactly()
    {
        var baseline = new[]
        {
            5.00,
            5.08,
            5.16,
            5.08,
            5.00,
        };

        var result = ProsodyPitchPlanner.Plan(
            ProsodyGesture.None,
            baseline);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(baseline, result.AdjustedPitches);
    }

    [Fact]
    public void LightRise_AddsSmallLinearRelativeCurve()
    {
        var result = ProsodyPitchPlanner.Plan(
            ProsodyGesture.LightRise,
            [5.00, 5.08, 5.16, 5.08, 5.00]);

        Assert.True(result.IsSuccess, result.Message);
        AssertClose(
            [4.92, 5.04, 5.16, 5.12, 5.08],
            result.AdjustedPitches);
    }

    [Fact]
    public void LightFall_AddsReverseRelativeCurve()
    {
        var result = ProsodyPitchPlanner.Plan(
            ProsodyGesture.LightFall,
            [5.00, 5.08, 5.16, 5.08, 5.00]);

        Assert.True(result.IsSuccess, result.Message);
        AssertClose(
            [5.08, 5.12, 5.16, 5.04, 4.92],
            result.AdjustedPitches);
    }

    [Fact]
    public void Hold_PreservesMeanAndHalvesDeviation()
    {
        var baseline = new[] { 5.00, 5.20, 5.00 };

        var result = ProsodyPitchPlanner.Plan(
            ProsodyGesture.Hold,
            baseline);

        Assert.True(result.IsSuccess, result.Message);

        var baselineMean = baseline.Average();
        var adjustedMean = result.AdjustedPitches.Average();

        Assert.InRange(
            Math.Abs(adjustedMean - baselineMean),
            0.0,
            0.000001);

        for (var i = 0; i < baseline.Length; i++)
        {
            var originalDeviation =
                baseline[i] - baselineMean;
            var adjustedDeviation =
                result.AdjustedPitches[i] - adjustedMean;

            Assert.InRange(
                Math.Abs(
                    adjustedDeviation
                    - (originalDeviation * 0.5)),
                0.0,
                0.000001);
        }
    }

    [Fact]
    public void NonNoneGesture_RequiresAtLeastTwoVoicedMoras()
    {
        var result = ProsodyPitchPlanner.Plan(
            ProsodyGesture.LightRise,
            [5.0]);

        Assert.Equal(
            ProsodyPitchPlanStatus.InsufficientVoicedMoras,
            result.Status);
        Assert.False(result.IsSuccess);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidVoicedPitch_FailsClosed(
        double invalid)
    {
        var result = ProsodyPitchPlanner.Plan(
            ProsodyGesture.LightFall,
            [5.0, invalid]);

        Assert.Equal(
            ProsodyPitchPlanStatus.InvalidPitch,
            result.Status);
        Assert.False(result.IsSuccess);
    }

    static void AssertClose(
        IReadOnlyList<double> expected,
        IReadOnlyList<double> actual)
    {
        Assert.Equal(expected.Count, actual.Count);

        for (var i = 0; i < expected.Count; i++)
        {
            Assert.InRange(
                Math.Abs(expected[i] - actual[i]),
                0.0,
                0.000001);
        }
    }
}
