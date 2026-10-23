### Changed

- A rotated `V.Motion(layoutId:)` inside another layoutId Motion that stretches by different factors on its two
  axes is drawn undistorted, as Framer Motion draws it: the counter-scale sits in a wrapper element outside the
  Motion's own rotate. That Motion's parent in the visual tree is the wrapper; its `name`, ref and styles stay on
  the Motion.
