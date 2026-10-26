### Fixed

- Removing many rows of a list at once, such as clearing a list or replacing all of it, takes time proportional
  to the number of rows. It used to grow with the square of the row count: each removed row tested every
  registered component against its own subtree, and found its slot by walking the container's children from the
  first one.
