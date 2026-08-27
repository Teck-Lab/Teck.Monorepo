using Xunit;

namespace Customers.IntegrationTests;

/// <summary>Protects local Customer startup from recreating the obsolete hardcoded tenant.</summary>
public sealed class LocalTenantSeedRemovalTests
{
    /// <summary>Ensures Development startup migrates the database without creating the legacy dev tenant.</summary>
    [Fact]
    public void CustomerHost_WhenDevelopmentStartupIsInspected_MigratesWithoutLegacyTenantSeed()
    {
        string source = File.ReadAllText(FindCustomerProgram());

        Assert.Contains("MigrateAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SeedDevTenantAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("00000000-0000-0000-0000-0000000000a1", source, StringComparison.Ordinal);
        Assert.DoesNotContain("identifier: \"dev\"", source, StringComparison.Ordinal);
    }

    private static string FindCustomerProgram()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "src", "services", "commerce", "customer", "Customer.Host", "Program.cs");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Customer.Host Program.cs was not found.");
    }
}
