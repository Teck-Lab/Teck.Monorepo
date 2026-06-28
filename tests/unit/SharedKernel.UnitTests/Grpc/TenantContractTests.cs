using SharedKernel.Grpc.Contracts.Remote.V1.Tenants;
using Xunit;

namespace SharedKernel.UnitTests.Grpc;

public sealed class TenantContractTests
{
    [Fact]
    public void Result_DefaultsAreSafe()
    {
        var result = new TenantDatabaseInfoRpcResult();
        Assert.False(result.Found);
        Assert.Equal(string.Empty, result.TenantId);
        Assert.Null(result.ErrorDetail);
    }

    [Fact]
    public void Command_CarriesTenantAndServiceName()
    {
        var command = new GetTenantDatabaseInfoCommand { TenantId = "abc", ServiceName = "order" };
        Assert.Equal("abc", command.TenantId);
        Assert.Equal("order", command.ServiceName);
    }
}
