using Riok.Mapperly.Abstractions;

namespace SharedKernel.Core.Mapping;

/// <summary>
/// Shared Mapperly mapper configuration providing compile-time mappings reused across services.
/// </summary>
[Mapper(UseReferenceHandling = true, PropertyNameMappingStrategy = PropertyNameMappingStrategy.CaseInsensitive)]
public static partial class SharedMapperConfig
{
}
