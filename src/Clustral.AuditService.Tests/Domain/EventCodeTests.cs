using System.Text.RegularExpressions;
using Clustral.AuditService.Domain;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.AuditService.Tests.Domain;

public sealed class EventCodeTests(ITestOutputHelper output)
{
    [Fact]
    public void AllCodes_AreUnique()
    {
        var duplicates = EventCodes.All
            .GroupBy(c => c)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        output.WriteLine($"Total codes: {EventCodes.All.Length}");
        duplicates.Should().BeEmpty("every event code must be unique");
    }

    [Fact]
    public void AllCodes_MatchPattern()
    {
        // Format: 2-3 uppercase prefix, 3 digits, severity letter (I/W/E)
        var pattern = new Regex(@"^[A-Z]{2,3}\d{3}[IWE]$");

        foreach (var code in EventCodes.All)
        {
            output.WriteLine($"Checking code: {code}");
            pattern.IsMatch(code).Should().BeTrue(
                $"code '{code}' should match pattern [A-Z]{{2,3}}\\d{{3}}[IWE]");
        }
    }

    [Theory]
    [InlineData("CAR001I")]
    [InlineData("CAR002I")]
    [InlineData("CAR004I")]
    [InlineData("CAR005I")]
    [InlineData("CCR001I")]
    [InlineData("CCR002I")]
    [InlineData("CCL001I")]
    [InlineData("CCL002I")]
    [InlineData("CCL004I")]
    [InlineData("CRL001I")]
    [InlineData("CRL002I")]
    [InlineData("CRL003I")]
    [InlineData("CUA001I")]
    [InlineData("CUA002I")]
    [InlineData("CUA003I")]
    [InlineData("CPR001I")]
    public void InfoCodes_EndWithI(string code)
    {
        EventCodes.All.Should().Contain(code);
        code.Should().EndWith("I");
    }

    [Theory]
    [InlineData("CAR003W")]
    [InlineData("CCL003W")]
    public void WarningCodes_EndWithW(string code)
    {
        EventCodes.All.Should().Contain(code);
        code.Should().EndWith("W");
    }

    [Fact]
    public void CodeCount_Is19()
    {
        output.WriteLine($"Event codes: {string.Join(", ", EventCodes.All)}");
        EventCodes.All.Should().HaveCount(19);
    }
}
