### Changed

- A `bg-radial-[…]` bracket whose comma-separated body reads as a CSS stop list, such as
  `bg-radial-[at_top_left,red,blue]`, now draws those stops, followed by any `from-` / `via-` / `to-` stops on
  the same element. It drew the `from-` / `via-` / `to-` gradient alone and read the bracket only for a centre.
- A gradient is laid out over the box's real proportions, as CSS lays it out. A diagonal angle
  (`bg-linear-45`) and a conic were drawn as if the box were square; in a box that is not square the
  diagonal line is now as long as the box makes it and a conic sweeps true angles. Under `animate-gradient`
  they are laid out over the twice-as-large background the pan slides across. A radial gradient off
  the middle of the box is now the farthest-corner ellipse CSS draws: its rings were stretched from a
  circle before, which was the same only for a centred one.
- A stop fading to `transparent` no longer darkens on the way. Colours interpolate with their alpha, as CSS
  interpolates them, so `from-red-500 to-transparent` stays red while it fades, where it passed through
  dark red before.
- `from-` / `via-` / `to-` positions are fixed up as CSS fixes them: a position behind an earlier stop is
  raised to it, so `from-60%` with `via-` at its 50% default puts via at 60%. Where two stops share a
  position the later one starts there, so `from-0% via-0%` begins on the via colour rather than the from
  colour.
- A `bg-radial-[…]` bracket is read as CSS reads it. One whose body is not a shape, size and position (an
  unknown token such as `bg-radial-[at_top_bogus]`, an ellipse with one radius) used to ignore what it did
  not know and draw a centred or positioned radial; it now leaves the class inert, as an invalid CSS gradient
  paints nothing. A radial's rings also run on past its radius where the box does not end, instead of
  holding the last colour at the radius, and a position may lie outside the box.
- The `/oklch` modifier interpolates along the OKLCH hue arc, as CSS does. It interpolated in OKLab before,
  which has no hue arc, so a gradient between hues now takes the arc through the intermediate hues.
