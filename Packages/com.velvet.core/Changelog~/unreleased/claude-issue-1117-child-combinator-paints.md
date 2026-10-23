### Fixed

- A `[&>*]:` payload paints, shapes text and sets a font family on a child at mount whether or not the child declares
  a variant of its own, and on a `V.Text` child; it used to reach a child that declared none only from that
  child's next render, and a `V.Text` child not at all.
