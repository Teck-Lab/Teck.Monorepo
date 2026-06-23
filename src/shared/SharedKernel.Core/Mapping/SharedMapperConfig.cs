using Riok.Mapperly.Abstractions;

namespace SharedKernel.Core.Mapping;

[Mapper(UseReferenceHandling = true, PropertyNameMappingStrategy = PropertyNameMappingStrategy.CaseInsensitive)]
public static partial class SharedMapperConfig
{
}
