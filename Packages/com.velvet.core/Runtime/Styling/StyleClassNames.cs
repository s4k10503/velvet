namespace Velvet
{
    /// <summary>
    /// Central null/empty-safe join point for utility-first class composition, so call sites can pass raw
    /// conditional expressions (ternaries, <see cref="When"/>) as arguments without each one guarding against
    /// null or empty branches itself.
    /// </summary>
    public static class StyleClassNames
    {
        /// <summary>Single allocation-conscious join point, so callers never special-case the 0/1-argument shape.</summary>
        public static string? Class(params string?[] parts)
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

        /// <summary>Sugar for a ternary, so a <see cref="Class"/> call reads as a flat list of always/conditionally-applied classes.</summary>
        public static string? When(bool condition, string className)
            => condition ? className : null;
    }
}
