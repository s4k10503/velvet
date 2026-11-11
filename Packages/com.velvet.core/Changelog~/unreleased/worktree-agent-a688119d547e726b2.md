### Fixed

- Patching an element no longer allocates a string for each of its classes that opens with `-` and is not a skew
  utility, such as `-mt-2` or `-translate-x-1/2`. The skew pass reads each class on every patch, and it cut the sign
  off before checking for `skew-`.
