using Clustral.Cli.Validation;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests.Validation;

public sealed class KubeLoginValidatorTests(ITestOutputHelper output)
{
    private readonly KubeLoginValidator _sut = new();

    [Fact]
    public void Valid_Guid_Passes()
    {
        var input = new KubeLoginInput(Guid.NewGuid().ToString(), null);
        var result = _sut.Validate(input);

        output.WriteLine($"Cluster: {input.Cluster} => IsValid: {result.IsValid}");
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("prod")]
    [InlineData("talos-lab")]
    [InlineData("my-cluster-123")]
    [InlineData("PROD")]
    public void Valid_Name_Passes(string name)
    {
        var input = new KubeLoginInput(name, null);
        var result = _sut.Validate(input);

        output.WriteLine($"Cluster: '{name}' => IsValid: {result.IsValid}");
        result.IsValid.Should().BeTrue("names should be accepted; resolution happens later");
    }

    [Theory]
    [InlineData("PT8H")]
    [InlineData("P1D")]
    [InlineData("PT30M")]
    [InlineData("P1DT12H30M")]
    [InlineData("PT1S")]
    [InlineData("P2W")]
    public void Valid_Ttl_Passes(string ttl)
    {
        var input = new KubeLoginInput(Guid.NewGuid().ToString(), ttl);
        var result = _sut.Validate(input);

        output.WriteLine($"TTL: {ttl} => IsValid: {result.IsValid}");
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Empty_Cluster_Fails(string? cluster)
    {
        var input = new KubeLoginInput(cluster!, null);
        var result = _sut.Validate(input);

        output.WriteLine($"Cluster: '{cluster}' => Errors: {string.Join(", ", result.Errors.Select(e => e.ErrorMessage))}");
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Cluster");
    }

    [Theory]
    [InlineData("8hours")]
    [InlineData("abc")]
    [InlineData("P")]
    [InlineData("PT")]
    [InlineData("8H")]
    public void Invalid_Ttl_Fails(string ttl)
    {
        var input = new KubeLoginInput(Guid.NewGuid().ToString(), ttl);
        var result = _sut.Validate(input);

        output.WriteLine($"TTL: '{ttl}' => Errors: {string.Join(", ", result.Errors.Select(e => e.ErrorMessage))}");
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Ttl");
    }

    [Fact]
    public void Empty_Cluster_And_Invalid_Ttl_Returns_Multiple_Errors()
    {
        var input = new KubeLoginInput("", "bad");
        var result = _sut.Validate(input);

        output.WriteLine($"Errors ({result.Errors.Count}):");
        foreach (var e in result.Errors)
            output.WriteLine($"  {e.PropertyName}: {e.ErrorMessage}");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterOrEqualTo(2);
    }
}
