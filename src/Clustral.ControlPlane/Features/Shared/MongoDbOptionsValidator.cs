using Clustral.ControlPlane.Infrastructure;
using FluentValidation;

namespace Clustral.ControlPlane.Features.Shared;

public sealed class MongoDbOptionsValidator : AbstractValidator<MongoDbOptions>
{
    public MongoDbOptionsValidator()
    {
        RuleFor(x => x.ConnectionString)
            .NotEmpty()
            .WithMessage("MongoDB connection string is required. Set via: MongoDB__ConnectionString env var.")
            .Must(s => s.StartsWith("mongodb://", StringComparison.OrdinalIgnoreCase)
                     || s.StartsWith("mongodb+srv://", StringComparison.OrdinalIgnoreCase))
            .WithMessage("MongoDB connection string must start with 'mongodb://' or 'mongodb+srv://'. Got: '{PropertyValue}'.");

        RuleFor(x => x.DatabaseName)
            .NotEmpty()
            .WithMessage("MongoDB database name is required. Set via: MongoDB__DatabaseName env var.");
    }
}
