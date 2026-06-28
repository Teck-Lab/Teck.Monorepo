using SharedKernel.Infrastructure.Endpoints;
using Xunit;

namespace SharedKernel.UnitTests.Endpoints;

/// <summary>
/// Tests for <see cref="EndpointPermission"/>.
/// </summary>
public sealed class EndpointPermissionTests
{
    /// <summary>
    /// Verifies that <see cref="EndpointPermission.Anonymous"/> produces a permission with empty
    /// resource and scope, sets <see cref="EndpointPermission.IsAnonymous"/> to <see langword="true"/>,
    /// and preserves the supplied audience.
    /// </summary>
    [Fact]
    public void Anonymous_HasEmptyResourceAndScope_AndIsAnonymous()
    {
        var permission = EndpointPermission.Anonymous("public");

        Assert.True(permission.IsAnonymous);
        Assert.Equal(string.Empty, permission.Resource);
        Assert.Equal(string.Empty, permission.Scope);
        Assert.Equal("public", permission.Audience);
    }

    /// <summary>
    /// Verifies that a non-anonymous permission has <see cref="EndpointPermission.IsAnonymous"/>
    /// set to <see langword="false"/> and carries the supplied resource, scope, and audience.
    /// </summary>
    [Fact]
    public void Protected_IsNotAnonymous_AndCarriesResourceScopeAudience()
    {
        var permission = new EndpointPermission("order", "create", "public");

        Assert.False(permission.IsAnonymous);
        Assert.Equal("order", permission.Resource);
        Assert.Equal("create", permission.Scope);
        Assert.Equal("public", permission.Audience);
    }
}
