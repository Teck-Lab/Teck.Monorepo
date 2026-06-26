namespace SharedKernel.Core.Pagination;

/// <summary>
/// Represents a paginated list of items with metadata about paging.
/// </summary>
/// <typeparam name="T">The type of the items.</typeparam>
public class PagedList<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PagedList{T}"/> class for JSON deserialization.
    /// </summary>
    public PagedList()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PagedList{T}"/> class.
    /// </summary>
    /// <param name="items">The items on the current page.</param>
    /// <param name="totalItems">The total number of items in the full dataset.</param>
    /// <param name="page">The current page number (1-based).</param>
    /// <param name="size">The number of items per page.</param>
    public PagedList(IEnumerable<T> items, int totalItems, int page, int size)
    {
        Page = page;
        Size = size;
        TotalItems = totalItems;
        TotalPages = totalItems > 0 ? (int)Math.Ceiling(totalItems / (double)size) : 0;
        Items = new List<T>(items);
    }

    /// <summary>
    /// Gets the items on the current page.
    /// </summary>
    public IList<T> Items { get; init; } = new List<T>();

    /// <summary>
    /// Gets the current page number (1-based).
    /// </summary>
    public int Page { get; init; }

    /// <summary>
    /// Gets the size of the page (number of items per page).
    /// </summary>
    public int Size { get; init; }

    /// <summary>
    /// Gets the total number of pages.
    /// </summary>
    public int TotalPages { get; init; }

    /// <summary>
    /// Gets the total number of items across all pages.
    /// </summary>
    public int TotalItems { get; init; }

    /// <summary>
    /// Gets a value indicating whether this is the first page.
    /// </summary>
    public bool IsFirstPage => Page == 1;

    /// <summary>
    /// Gets a value indicating whether this is the last page.
    /// </summary>
    public bool IsLastPage => Page == TotalPages && TotalPages > 0;
}
