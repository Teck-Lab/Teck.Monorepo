using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using Assembly = System.Reflection.Assembly;

namespace Teck.Platform.Arch.Tests;

public abstract class SharedTestBase
{
    // Load only assemblies relevant to shared rules (not a specific service)
    protected static readonly Assembly SharedKernelAssembly = typeof(SharedKernel.Core.Domain.IAggregateRoot).Assembly;
    protected static readonly Assembly CoreAssembly = typeof(SharedKernel.Core.Database.IGenericReadRepository<,>).Assembly;
    protected static readonly Assembly PersistenceAssembly = typeof(SharedKernel.Infrastructure.Database.EFCore.GenericReadRepository<,,>).Assembly;

    protected static readonly Architecture SharedArchitecture = new ArchLoader()
        .LoadAssemblies(
            SharedKernelAssembly,
            CoreAssembly,
            PersistenceAssembly
        )
        .Build();
}
