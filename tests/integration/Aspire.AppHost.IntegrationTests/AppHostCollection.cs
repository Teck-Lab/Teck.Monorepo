using Xunit;

namespace Aspire.AppHost.IntegrationTests;

/// <summary>Serializes AppHost tests because its stateful infrastructure resources use data volumes.</summary>
[CollectionDefinition("AppHost", DisableParallelization = true)]
public sealed class AppHostCollection : ICollectionFixture<AppHostFixture>;
