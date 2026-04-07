using System.Reflection;
using Clustral.ControlPlane.Features.Shared;
using FluentAssertions;
using MediatR;
using Xunit.Abstractions;

namespace Clustral.ControlPlane.Tests.Features.Shared;

/// <summary>
/// Convention tests that verify all commands implement ICommand and all
/// queries implement IQuery. Enforces CQS (Command-Query Separation)
/// at compile time by scanning the assembly.
/// </summary>
public sealed class CqsConventionTests(ITestOutputHelper output)
{
    private static readonly Assembly ControlPlaneAssembly = typeof(Program).Assembly;

    private static IEnumerable<Type> GetMediatRRequests() =>
        ControlPlaneAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.GetInterfaces()
                .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)))
            .SelectMany(handler => handler.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>))
                .Select(i => i.GetGenericArguments()[0]))
            .Distinct();

    [Fact]
    public void AllCommandRecords_ImplementICommand()
    {
        var commandTypes = ControlPlaneAssembly.GetTypes()
            .Where(t => t.Name.EndsWith("Command") && (t.IsClass || t.IsValueType) && !t.IsAbstract)
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>))
                || typeof(IRequest<>).IsAssignableFrom(t));

        foreach (var type in commandTypes)
        {
            var implementsICommand =
                typeof(ICommand).IsAssignableFrom(type) ||
                type.GetInterfaces().Any(i =>
                    i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));

            output.WriteLine($"{type.Name}: implements ICommand = {implementsICommand}");
            implementsICommand.Should().BeTrue(
                $"{type.Name} is named *Command but does not implement ICommand<T> or ICommand");
        }
    }

    [Fact]
    public void AllQueryRecords_ImplementIQuery()
    {
        var queryTypes = ControlPlaneAssembly.GetTypes()
            .Where(t => t.Name.EndsWith("Query") && (t.IsClass || t.IsValueType) && !t.IsAbstract)
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)));

        foreach (var type in queryTypes)
        {
            var implementsIQuery = type.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>));

            output.WriteLine($"{type.Name}: implements IQuery = {implementsIQuery}");
            implementsIQuery.Should().BeTrue(
                $"{type.Name} is named *Query but does not implement IQuery<T>");
        }
    }

    [Fact]
    public void NoCommandImplementsIQuery()
    {
        var violations = ControlPlaneAssembly.GetTypes()
            .Where(t => t.Name.EndsWith("Command") && !t.IsAbstract)
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>)));

        foreach (var type in violations)
            output.WriteLine($"VIOLATION: {type.Name} is a Command but implements IQuery");

        violations.Should().BeEmpty("Commands must not implement IQuery<T>");
    }

    [Fact]
    public void NoQueryImplementsICommand()
    {
        var violations = ControlPlaneAssembly.GetTypes()
            .Where(t => t.Name.EndsWith("Query") && !t.IsAbstract)
            .Where(t => typeof(ICommand).IsAssignableFrom(t) ||
                        t.GetInterfaces().Any(i =>
                            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>)));

        foreach (var type in violations)
            output.WriteLine($"VIOLATION: {type.Name} is a Query but implements ICommand");

        violations.Should().BeEmpty("Queries must not implement ICommand<T>");
    }
}
