### Fixed

- A re-render of a container whose children are expanded in place, such as components, fragments or context
  providers, finds each child's existing element by walking on from the one found before it wherever the
  children kept their order. It used to walk the container's children from the first one for every child it
  reused, a cost that grew with the square of the child count.
