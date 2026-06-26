using Catalog.Domain.Entities;
using SharedKernel.Core.Domain;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class ReadModelConstraintTests
{
    [Fact]
    public void Product_ImplementsIReadModelOfGuid()
    {
        Assert.True(typeof(IReadModel<System.Guid>).IsAssignableFrom(typeof(Product)));
    }
}
