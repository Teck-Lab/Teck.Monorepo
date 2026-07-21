using Customers.Domain.DomainEvents;
using SharedKernel.Core.Domain;

namespace Customers.Domain.Entities;

/// <summary>The customer aggregate root. Owns its addresses.</summary>
public sealed class Customer : BaseEntity, IAggregateRoot, ITenantScoped
{
    private readonly List<Address> _addresses = new();

    private Customer()
    {
    }

    /// <inheritdoc/>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets the linked Keycloak subject id.</summary>
    public string KeycloakSubjectId { get; private set; } = string.Empty;

    /// <summary>Gets the email address.</summary>
    public string Email { get; private set; } = string.Empty;

    /// <summary>Gets the first name.</summary>
    public string FirstName { get; private set; } = string.Empty;

    /// <summary>Gets the last name.</summary>
    public string LastName { get; private set; } = string.Empty;

    /// <summary>Gets a value indicating whether the customer is active.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Gets the customer's addresses.</summary>
    public IReadOnlyList<Address> Addresses => _addresses;

    /// <summary>Creates a new active customer and raises <see cref="CustomerCreated"/>.</summary>
    /// <param name="tenantId">The owning tenant id.</param>
    /// <param name="keycloakSubjectId">The linked Keycloak subject id.</param>
    /// <param name="email">The email address.</param>
    /// <param name="firstName">The first name.</param>
    /// <param name="lastName">The last name.</param>
    /// <returns>The newly created customer.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="email"/> or <paramref name="keycloakSubjectId"/> is blank.</exception>
    public static Customer Create(string tenantId, string keycloakSubjectId, string email, string firstName, string lastName)
    {
        if (string.IsNullOrWhiteSpace(keycloakSubjectId))
        {
            throw new ArgumentException("KeycloakSubjectId is required.", nameof(keycloakSubjectId));
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email is required.", nameof(email));
        }

        var customer = new Customer
        {
            TenantId = tenantId,
            KeycloakSubjectId = keycloakSubjectId,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            IsActive = true,
        };

        customer.AddDomainEvent(new CustomerCreated(customer.Id, customer.TenantId, customer.KeycloakSubjectId, customer.Email));
        return customer;
    }

    /// <summary>Updates the customer's first and last name.</summary>
    /// <param name="firstName">The new first name.</param>
    /// <param name="lastName">The new last name.</param>
    public void UpdateProfile(string firstName, string lastName)
    {
        FirstName = firstName;
        LastName = lastName;
    }

    /// <summary>Adds a new address to the customer, marking it primary if it is the first one.</summary>
    /// <param name="line1">The first line of the address.</param>
    /// <param name="line2">The optional second line of the address.</param>
    /// <param name="city">The city.</param>
    /// <param name="postalCode">The postal code.</param>
    /// <param name="country">The country.</param>
    /// <returns>The id of the newly added address.</returns>
    public Guid AddAddress(string line1, string? line2, string city, string postalCode, string country)
    {
        var isPrimary = _addresses.Count == 0;
        var address = Address.Create(line1, line2, city, postalCode, country, isPrimary);
        _addresses.Add(address);
        return address.Id;
    }
}
