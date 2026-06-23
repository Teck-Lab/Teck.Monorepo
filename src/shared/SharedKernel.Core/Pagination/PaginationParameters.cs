namespace SharedKernel.Core.Pagination
{
    /// <summary>
    /// The pagination parameters.
    /// </summary>
    public abstract class PaginationParameters
    {
        /// <summary>
        /// Gets the max page size.
        /// </summary>
        internal virtual int MaxPageSize { get; } = 100;

        /// <summary>
        /// Gets or sets the default page size.
        /// </summary>
        internal virtual int DefaultPageSize { get; set; } = 10;

        /// <summary>
        /// Gets or sets the page.
        /// </summary>
        public virtual int Page { get; set; } = 1;

        /// <summary>
        /// Gets or sets the size.
        /// </summary>
        public int Size
        {
            get
            {
                return DefaultPageSize;
            }
            set
            {
                DefaultPageSize = value > MaxPageSize ? MaxPageSize : value;
            }
        }
    }
}
