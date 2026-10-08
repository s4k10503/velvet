### Changed

- A `bg-radial-[…]` bracket whose comma-separated body reads as a CSS stop list, such as
  `bg-radial-[at_top_left,red,blue]`, now draws those stops, and `from-` / `via-` / `to-` on the same element
  are no longer read. It drew the `from-` / `via-` / `to-` gradient and read the bracket only for a centre.
