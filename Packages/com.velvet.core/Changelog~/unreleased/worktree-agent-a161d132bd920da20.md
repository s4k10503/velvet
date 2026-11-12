### Fixed

- A rounded layoutId `V.Motion` that does not clip its content and whose background image is set inline keeps
  painting that image while its move is drawn scaled differently on each axis, and has it back once the move
  lands. A texture or sprite image used to be painted for one frame of the move, show none for the rest of it, and
  be lost for good when the move landed. An inline `RenderTexture` image, on a Motion that clips its content or
  not, is now painted for the whole move and handed back too, where it used to be painted on no frame of the move
  and lost when it landed.
