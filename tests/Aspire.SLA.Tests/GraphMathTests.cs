using static Aspire.SLA.SlaGraphValidator;

namespace Aspire.SLA.test;

/// <summary>Tests for the SLA graph math utilities.</summary>
public sealed class GraphMathTests
{
    [Theory]
    [InlineData(0.999, 1, 0.999)]
    [InlineData(0.999, 2, 0.999999)]
    [InlineData(0.999, 3, 0.999999999)]
    [InlineData(0.99, 2, 0.9999)]
    internal void ComputeParallelSla_ReturnsCorrectValues(double baseSla, int replicas, double expected)
    {
        var result = ComputeParallelSla(baseSla, replicas);
        Assert.Equal(expected, result, precision: 10);
    }

    [Theory]
    [InlineData(new[] { 0.999, 0.999 }, 0.998001)]
    [InlineData(new[] { 0.999, 0.999, 0.999 }, 0.997002999)]
    [InlineData(new[] { 0.999, 0.0 }, 0.0)]
    [InlineData(new[] { 1.0, 1.0, 1.0 }, 1.0)]
    internal void ComputeSeriesSla_ReturnsCorrectValues(double[] slas, double expected)
    {
        var result = ComputeSeriesSla(slas);
        Assert.Equal(expected, result, precision: 10);
    }

    [Fact]
    internal void ComputeParallelSla_ZeroReplicas_ReturnsBaseSla()
    {
        var result = ComputeParallelSla(0.999, 0);
        Assert.Equal(0.999, result);
    }

    [Fact]
    internal void ComputeSeriesSla_EmptyChain_ReturnsOne()
    {
        var result = ComputeSeriesSla();
        Assert.Equal(1.0, result);
    }
}
