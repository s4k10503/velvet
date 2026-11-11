### Fixed

- The skew pass no longer cuts the sign off a class that opens with `-` and is not a skew utility, such as `-mt-2`
  or `-translate-x-1/2`. It read each class of an element on every patch and allocated a string for each such class
  before checking for `skew-`.
