using Clustral.ControlPlane.Features.Shared;
using Clustral.ControlPlane.Infrastructure;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Features.Shared;

public class MongoDbOptionsValidatorTests(ITestOutputHelper output)
{
    private readonly MongoDbOptionsValidator _validator = new();

    private static MongoDbOptions ValidOptions() => new()
    {
        ConnectionString = "mongodb://localhost:27017",
        DatabaseName = "clustral",
    };

    [Fact]
    public void EmptyConnectionString_Fails()
    {
        var opts = ValidOptions();
        opts.ConnectionString = string.Empty;

        var result = _validator.Validate(opts);

        output.WriteLine($"Errors: {string.Join("; ", result.Errors.Select(e => e.ErrorMessage))}");
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ConnectionString");
    }

    [Fact]
    public void InvalidScheme_Fails()
    {
        var opts = ValidOptions();
        opts.ConnectionString = "postgresql://localhost:5432";

        var result = _validator.Validate(opts);

        output.WriteLine($"Errors: {string.Join("; ", result.Errors.Select(e => e.ErrorMessage))}");
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ConnectionString");
    }

    [Fact]
    public void ValidMongodbScheme_Passes()
    {
        var opts = ValidOptions();
        opts.ConnectionString = "mongodb://localhost:27017";

        var result = _validator.Validate(opts);

        output.WriteLine($"IsValid: {result.IsValid}");
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidMongodbSrvScheme_Passes()
    {
        var opts = ValidOptions();
        opts.ConnectionString = "mongodb+srv://cluster0.example.mongodb.net";

        var result = _validator.Validate(opts);

        output.WriteLine($"IsValid: {result.IsValid}");
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void EmptyDatabaseName_Fails()
    {
        var opts = ValidOptions();
        opts.DatabaseName = string.Empty;

        var result = _validator.Validate(opts);

        output.WriteLine($"Errors: {string.Join("; ", result.Errors.Select(e => e.ErrorMessage))}");
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "DatabaseName");
    }

    [Fact]
    public void AllValid_Passes()
    {
        var opts = new MongoDbOptions
        {
            ConnectionString = "mongodb://admin:password@mongo.example.com:27017",
            DatabaseName = "my-database",
        };

        var result = _validator.Validate(opts);

        output.WriteLine($"IsValid: {result.IsValid}");
        result.IsValid.Should().BeTrue();
    }
}
