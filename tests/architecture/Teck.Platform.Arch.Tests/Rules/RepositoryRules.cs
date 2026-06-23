using ArchUnitNET.Domain;
using SharedKernel.Core.Domain;
using Xunit;

namespace Teck.Platform.Arch.Tests.Rules;

public static class RepositoryRules
{
    public static void WriteRepositories_Should_UseEntitiesImplementingIAggregateRoot(Architecture architecture)
    {
        // Find write repository classes in the loaded architecture
        var repoClasses = architecture.Classes
            .Where(c => c.Name.EndsWith("WriteRepository"))
            .ToList();

        foreach (var repoClass in repoClasses)
        {
            // Get the System.Type for the repo class
            var systemType = Type.GetType(repoClass.FullName);
            if (systemType == null)
            {
                throw new Exception($"Type not found for {repoClass.FullName}");
            }

            var baseType = systemType.BaseType;
            if (baseType == null || !baseType.IsGenericType)
            {
                // Could skip or fail depending on your conventions
                continue;
            }

            if (!baseType.GetGenericTypeDefinition().Name.StartsWith("GenericWriteRepository", StringComparison.Ordinal))
            {
                continue;
            }

            var entityType = baseType.GetGenericArguments()[0];
            bool implementsAggregateRoot = typeof(IAggregateRoot).IsAssignableFrom(entityType);

            Assert.True(implementsAggregateRoot,
                $"{systemType.Name} uses entity {entityType.Name} which does not implement IAggregateRoot");
        }
    }

    public static void ReadRepositories_Should_UseReadModels(Architecture architecture)
    {
        // Find read repository classes in the loaded architecture
        var repoClasses = architecture.Classes
            .Where(c => c.Name.EndsWith("ReadRepository", StringComparison.Ordinal))
            .ToList();

        foreach (var repoClass in repoClasses)
        {
            // Get the System.Type for the repo class
            var systemType = Type.GetType(repoClass.FullName);
            if (systemType == null)
            {
                throw new Exception($"Type not found for {repoClass.FullName}");
            }

            var baseType = systemType.BaseType;
            if (baseType == null || !baseType.IsGenericType)
            {
                // Could skip or fail depending on your conventions
                continue;
            }

            if (!baseType.GetGenericTypeDefinition().Name.StartsWith("GenericReadRepository", StringComparison.Ordinal))
            {
                continue;
            }

            var readModelType = baseType.GetGenericArguments()[0];
            bool isAggregateRoot = typeof(IAggregateRoot).IsAssignableFrom(readModelType);

            Assert.False(isAggregateRoot,
                $"{systemType.Name} uses type {readModelType.Name} which implements IAggregateRoot but read repositories should use read models");
        }
    }
}
