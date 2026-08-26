// <copyright file="CustomerProfileTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Net;
using System.Net.Http.Json;
using Customers.Application.Database;
using Customers.Application.Customers.Responses;
using Customers.Domain.Entities;
using Finbuckle.MultiTenant.Extensions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Infrastructure.Database.EFCore;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Customers.IntegrationTests;

/// <summary>
/// HTTP integration tests for the customer profile flow: create, read, list, update profile, and
/// add an address. Boots <c>Customer.Host</c> via <see cref="CustomerIntegrationTestBase"/> against
/// a Testcontainers PostgreSQL database so these tests exercise the full DI, EF, WolverineFx and
/// auth pipeline end to end.
/// </summary>
[Collection("SharedTestcontainers")]
public sealed class CustomerProfileTests : CustomerIntegrationTestBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CustomerProfileTests"/> class.
    /// </summary>
    /// <param name="fixture">The shared testcontainers fixture providing Postgres.</param>
    public CustomerProfileTests(SharedTestcontainersFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task CreateCustomer_ReturnsCreatedWithRoundTrippedFields()
    {
        var response = await Client.PostAsJsonAsync(
            "/customers",
            new { Email = "ada@example.com", FirstName = "Ada", LastName = "Lovelace" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var customer = await response.Content.ReadFromJsonAsync<CustomerDto>();

        Assert.NotNull(customer);
        Assert.NotEqual(Guid.Empty, customer!.Id);
        Assert.Equal("ada@example.com", customer.Email);
        Assert.Equal("Ada", customer.FirstName);
        Assert.Equal("Lovelace", customer.LastName);

        // Proves CustomerIdentityAccessor read the mock's "sub" claim end-to-end through
        // CreateCustomerHandler rather than defaulting to an empty subject.
        Assert.Equal(MockBearerAuthenticationHandler.TestSubject, customer.KeycloakSubjectId);
    }

    [Fact]
    public async Task GetCustomer_AfterCreate_ReturnsSameCustomer()
    {
        var created = await CreateCustomerAsync();

        var getResponse = await Client.GetAsync($"/customers/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var fetched = await getResponse.Content.ReadFromJsonAsync<CustomerDto>();

        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal(created.Email, fetched.Email);
    }

    [Fact]
    public async Task GetCustomer_ForeignTenantCustomer_IsExcluded()
    {
        var foreignCustomer = Customer.Create(
            "tenant-b",
            "foreign-subject",
            "foreign@example.com",
            "Foreign",
            "Customer");

        await using (var seed = new CustomerDbContext(
            new DbContextOptionsBuilder<CustomerDbContext>()
                .UseNpgsql(DatabaseConnectionString)
                .UseTeckCloudTenant("tenant-b")
                .Options,
            null!))
        {
            seed.Customers.Add(foreignCustomer);
            await seed.SaveChangesAsync();
        }

        HttpResponseMessage response = await Client.GetAsync($"/customers/{foreignCustomer.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ListCustomers_AfterCreate_IncludesCreatedCustomer()
    {
        var created = await CreateCustomerAsync();

        var listResponse = await Client.GetAsync("/customers");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var customers = await listResponse.Content.ReadFromJsonAsync<List<CustomerDto>>();

        Assert.NotNull(customers);
        Assert.Contains(customers!, c => c.Id == created.Id);
    }

    [Fact]
    public async Task UpdateProfile_ReturnsUpdatedFields()
    {
        var created = await CreateCustomerAsync();

        var updateResponse = await Client.PutAsJsonAsync(
            $"/customers/{created.Id}/profile",
            new { FirstName = "Grace", LastName = "Hopper" });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updated = await updateResponse.Content.ReadFromJsonAsync<CustomerDto>();

        Assert.NotNull(updated);
        Assert.Equal(created.Id, updated!.Id);
        Assert.Equal("Grace", updated.FirstName);
        Assert.Equal("Hopper", updated.LastName);

        // Re-fetch from a fresh GET (not the in-memory handler result) to prove the update
        // persisted to Postgres rather than just echoing the request back.
        var reGet = await Client.GetFromJsonAsync<CustomerDto>($"/customers/{created.Id}");
        Assert.NotNull(reGet);
        Assert.Equal("Grace", reGet!.FirstName);
        Assert.Equal("Hopper", reGet.LastName);
    }

    [Fact]
    public async Task AddAddress_ReturnsCreatedAddressWithSentFields()
    {
        var created = await CreateCustomerAsync();

        var addressResponse = await Client.PostAsJsonAsync(
            $"/customers/{created.Id}/addresses",
            new
            {
                Line1 = "10 Downing Street",
                Line2 = (string?)null,
                City = "London",
                PostalCode = "SW1A 2AA",
                Country = "GB",
            });

        Assert.Equal(HttpStatusCode.Created, addressResponse.StatusCode);

        var address = await addressResponse.Content.ReadFromJsonAsync<AddressDto>();

        Assert.NotNull(address);
        Assert.NotEqual(Guid.Empty, address!.Id);
        Assert.Equal("10 Downing Street", address.Line1);
        Assert.Null(address.Line2);
        Assert.Equal("London", address.City);
        Assert.Equal("SW1A 2AA", address.PostalCode);
        Assert.Equal("GB", address.Country);
        Assert.True(address.IsPrimary);
    }

    private async Task<CustomerDto> CreateCustomerAsync()
    {
        var response = await Client.PostAsJsonAsync(
            "/customers",
            new { Email = $"{Guid.NewGuid()}@example.com", FirstName = "Ada", LastName = "Lovelace" });

        response.EnsureSuccessStatusCode();

        var customer = await response.Content.ReadFromJsonAsync<CustomerDto>();
        Assert.NotNull(customer);
        return customer!;
    }
}
