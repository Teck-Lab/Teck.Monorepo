using SharedKernel.Core.Domain;

namespace Customers.Domain.Entities;

/// <summary>A postal address owned by a <see cref="Customer"/>.</summary>
public sealed class Address : BaseEntity
{
    private Address()
    {
    }

    /// <summary>Gets the first line of the address.</summary>
    public string Line1 { get; private set; } = string.Empty;

    /// <summary>Gets the optional second line of the address.</summary>
    public string? Line2 { get; private set; }

    /// <summary>Gets the city.</summary>
    public string City { get; private set; } = string.Empty;

    /// <summary>Gets the postal code.</summary>
    public string PostalCode { get; private set; } = string.Empty;

    /// <summary>Gets the country.</summary>
    public string Country { get; private set; } = string.Empty;

    /// <summary>Gets a value indicating whether this is the customer's primary address.</summary>
    public bool IsPrimary { get; private set; }

    /// <summary>Creates a new address.</summary>
    /// <param name="line1">The first line of the address.</param>
    /// <param name="line2">The optional second line of the address.</param>
    /// <param name="city">The city.</param>
    /// <param name="postalCode">The postal code.</param>
    /// <param name="country">The country.</param>
    /// <param name="isPrimary">Whether this is the customer's primary address.</param>
    /// <returns>The newly created address.</returns>
    /// <exception cref="ArgumentException">Thrown when a required field is blank.</exception>
    internal static Address Create(string line1, string? line2, string city, string postalCode, string country, bool isPrimary)
    {
        if (string.IsNullOrWhiteSpace(line1))
        {
            throw new ArgumentException("Line1 is required.", nameof(line1));
        }

        if (string.IsNullOrWhiteSpace(city))
        {
            throw new ArgumentException("City is required.", nameof(city));
        }

        if (string.IsNullOrWhiteSpace(postalCode))
        {
            throw new ArgumentException("PostalCode is required.", nameof(postalCode));
        }

        if (string.IsNullOrWhiteSpace(country))
        {
            throw new ArgumentException("Country is required.", nameof(country));
        }

        return new Address
        {
            Line1 = line1,
            Line2 = line2,
            City = city,
            PostalCode = postalCode,
            Country = country,
            IsPrimary = isPrimary,
        };
    }
}
