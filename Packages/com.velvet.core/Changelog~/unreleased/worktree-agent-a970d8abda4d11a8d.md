### Fixed

- Patching every row of an unkeyed list checks that each row's slot is still occupied by walking on from the slot
  it checked before, where nothing moved that one, rather than from the container's first child. That walk asked
  every child it passed whether it was one of the children Velvet
  hosts that hold no slot, so the cost grew with the square of the row count and with each kind of those children
  Velvet added. In the editor, reordering a keyed list counted all of the container's children twice for each row
  it kept from the first one out of place, and now checks that row's slot from the one checked before it.
- A class change whose families of classes are already in the order Tailwind emits them no longer sorts the
  element's classes, or copies its class list once for each family, to find that out.
