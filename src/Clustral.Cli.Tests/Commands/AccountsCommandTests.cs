using System.Text;
using Clustral.Cli.Commands;
using FluentAssertions;
using Xunit.Abstractions;

namespace Clustral.Cli.Tests.Commands;

/// <summary>
/// Tests for <see cref="AccountsCommand"/> — multi-account management.
/// </summary>
[Collection(AccountsTestCollection.Name)]
public sealed class AccountsCommandTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _tempHome = Path.Combine(
        Path.GetTempPath(), $"clustral-acct-test-{Guid.NewGuid():N}");
    private readonly string? _origHome = Environment.GetEnvironmentVariable("HOME");
    private readonly string? _origUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");

    private string AccountsDir => Path.Combine(_tempHome, ".clustral", "accounts");

    private void SetupHome()
    {
        Directory.CreateDirectory(Path.Combine(_tempHome, ".clustral"));
        Environment.SetEnvironmentVariable("HOME", _tempHome);
        Environment.SetEnvironmentVariable("USERPROFILE", _tempHome);
        ProfileCommand.ClearActiveProfile();
    }

    private static string CreateFakeJwt(string email, DateTimeOffset expiresAt)
    {
        var header = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"alg":"none"}"""))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $$"""{"email":"{{email}}","exp":{{expiresAt.ToUnixTimeSeconds()}}}"""))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{header}.{payload}.";
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("HOME", _origHome);
        Environment.SetEnvironmentVariable("USERPROFILE", _origUserProfile);
        try { Directory.Delete(_tempHome, recursive: true); } catch { }
    }

    [Fact]
    public void ListAccounts_NoAccountsDir_ReturnsEmpty()
    {
        SetupHome();
        AccountsCommand.ListAccounts().Should().BeEmpty();
    }

    [Fact]
    public void ListAccounts_WithTokenFiles_ReturnsEmails()
    {
        SetupHome();
        Directory.CreateDirectory(AccountsDir);
        File.WriteAllText(Path.Combine(AccountsDir, "alice@corp.com.token"), "jwt1");
        File.WriteAllText(Path.Combine(AccountsDir, "bob@corp.com.token"), "jwt2");

        var accounts = AccountsCommand.ListAccounts();

        output.WriteLine($"Accounts: {string.Join(", ", accounts)}");
        accounts.Should().Equal("alice@corp.com", "bob@corp.com");
    }

    [Fact]
    public void SetAndGetActiveAccount_RoundTrips()
    {
        SetupHome();
        AccountsCommand.SetActiveAccount("alice@corp.com");

        AccountsCommand.GetActiveAccount().Should().Be("alice@corp.com");
    }

    [Fact]
    public void ClearActiveAccount_RemovesFile()
    {
        SetupHome();
        AccountsCommand.SetActiveAccount("alice@corp.com");
        AccountsCommand.ClearActiveAccount();

        AccountsCommand.GetActiveAccount().Should().BeNull();
    }

    [Fact]
    public void StoreAccountToken_CreatesFileAndSetsActive()
    {
        SetupHome();
        var jwt = CreateFakeJwt("alice@corp.com", DateTimeOffset.UtcNow.AddHours(8));

        AccountsCommand.StoreAccountToken("alice@corp.com", jwt);

        File.Exists(Path.Combine(AccountsDir, "alice@corp.com.token")).Should().BeTrue();
        AccountsCommand.GetActiveAccount().Should().Be("alice@corp.com");
        AccountsCommand.ReadAccountToken("alice@corp.com").Should().Be(jwt);
    }

    [Fact]
    public void StoreMultipleAccounts_BothExist()
    {
        SetupHome();
        var jwt1 = CreateFakeJwt("alice@corp.com", DateTimeOffset.UtcNow.AddHours(8));
        var jwt2 = CreateFakeJwt("bob@corp.com", DateTimeOffset.UtcNow.AddHours(4));

        AccountsCommand.StoreAccountToken("alice@corp.com", jwt1);
        AccountsCommand.StoreAccountToken("bob@corp.com", jwt2);

        AccountsCommand.ListAccounts().Should().HaveCount(2);
        // Last login becomes active.
        AccountsCommand.GetActiveAccount().Should().Be("bob@corp.com");
    }

    [Fact]
    public void ResolveActiveAccountTokenPath_WithActiveAccount_ReturnsPath()
    {
        SetupHome();
        var jwt = CreateFakeJwt("alice@corp.com", DateTimeOffset.UtcNow.AddHours(8));
        AccountsCommand.StoreAccountToken("alice@corp.com", jwt);

        var path = AccountsCommand.ResolveActiveAccountTokenPath();

        path.Should().NotBeNull();
        path.Should().Contain("alice@corp.com.token");
    }

    [Fact]
    public void ResolveActiveAccountTokenPath_NoActiveAccount_ReturnsNull()
    {
        SetupHome();
        AccountsCommand.ClearActiveAccount();

        AccountsCommand.ResolveActiveAccountTokenPath().Should().BeNull();
    }

    [Fact]
    public void ReadAccountToken_NonExistent_ReturnsNull()
    {
        SetupHome();
        AccountsCommand.ReadAccountToken("nobody@corp.com").Should().BeNull();
    }

    [Fact]
    public void ResolveTokenPath_WithAccount_UsesAccountPath()
    {
        SetupHome();
        var jwt = CreateFakeJwt("alice@corp.com", DateTimeOffset.UtcNow.AddHours(8));
        AccountsCommand.StoreAccountToken("alice@corp.com", jwt);

        var tokenPath = ProfileCommand.ResolveTokenPath();

        output.WriteLine($"Resolved: {tokenPath}");
        tokenPath.Should().Contain("accounts");
        tokenPath.Should().Contain("alice@corp.com");
    }

    [Fact]
    public void ResolveTokenPath_NoAccount_FallsBackToLegacy()
    {
        SetupHome();
        AccountsCommand.ClearActiveAccount();

        var tokenPath = ProfileCommand.ResolveTokenPath();

        tokenPath.Should().NotContain("accounts");
        tokenPath.Should().EndWith("token");
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AccountsTestCollection
{
    public const string Name = "Accounts (env-var redirected, no parallelisation)";
}
