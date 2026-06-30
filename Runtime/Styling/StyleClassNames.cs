namespace Velvet
{
    /// <summary>
    /// Conditional class-name concatenation utility.
    /// Filters out null/empty entries and joins them with spaces.
    /// </summary>
    public static class StyleClassNames
    {
        /// <summary>Joins parts with spaces, skipping null/empty entries.</summary>
        public static string Class(params string?[] parts)
        {
            switch (parts.Length)
            {
                case 0:
                    return "";
                // Fast paths for the empty and single-entry shapes; 2+ entries fall through to the StringBuilder path.
                case 1:
                    return string.IsNullOrWhiteSpace(parts[0]) ? "" : parts[0];
            }

            var estimatedLength = 0;
            var validCount = 0;
            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part))
                {
                    continue;
                }

                estimatedLength += part.Length + 1;
                validCount++;
            }

            if (validCount == 0)
            {
                return "";
            }

            var result = new System.Text.StringBuilder(estimatedLength);
            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part))
                {
                    continue;
                }

                if (result.Length > 0)
                {
                    result.Append(' ');
                }

                result.Append(part);
            }

            return result.ToString();
        }

        /// <summary>Conditional class (returns null when condition is false).</summary>
        public static string? When(bool condition, string className)
            => condition ? className : null;
    }
}
