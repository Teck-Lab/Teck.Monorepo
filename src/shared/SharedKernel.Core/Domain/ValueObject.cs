namespace SharedKernel.Core.Domain
{
    /// <summary>
    /// Represents a base class for value objects, which are immutable and compared based on their properties.
    /// </summary>
    public abstract class ValueObject
    {
        /// <summary>
        /// Provides the components that are used to determine equality for the value object.
        /// </summary>
        /// <returns>An enumerable of objects representing the equality components.</returns>
        protected abstract IEnumerable<object?> GetEqualityComponents();

        /// <summary>
        /// Determines whether the specified object is equal to the current value object.
        /// </summary>
        /// <param name="obj">The object to compare with the current value object.</param>
        /// <returns><c>true</c> if the specified object is equal to the current value object; otherwise, <c>false</c>.</returns>
        public override bool Equals(object? obj)
        {
            if (obj is null || obj.GetType() != GetType())
                return false;

            var other = (ValueObject)obj;
            using var thisValues = GetEqualityComponents().GetEnumerator();
            using var otherValues = other.GetEqualityComponents().GetEnumerator();

            while (thisValues.MoveNext() && otherValues.MoveNext())
            {
                if (thisValues.Current is null ^ otherValues.Current is null)
                    return false;

                if (thisValues.Current is not null &&
                    !thisValues.Current.Equals(otherValues.Current))
                    return false;
            }

            return !thisValues.MoveNext() && !otherValues.MoveNext();
        }

        /// <summary>
        /// Serves as the default hash function for the value object.
        /// </summary>
        /// <returns>A hash code for the current value object.</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                foreach (var obj in GetEqualityComponents())
                    hash = (hash * 23) + (obj?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }
}
