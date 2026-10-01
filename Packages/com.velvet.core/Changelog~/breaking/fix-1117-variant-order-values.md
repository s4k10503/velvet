### Changed

- Two variant rules of one rank order as Tailwind emits them rather than by where the className writes them:
  by their variants' values, then by the first property they differ on in Tailwind's property order, then by
  the candidate itself with digits read as numbers. On every property an arbitrary value shares with another
  variant rule of its rank, the one emitted later takes it.
  `data-[state=open]:w-[10px] data-[side=left]:w-[20px]` with both attributes set was 20 px wide and is now 10
  px; `hover:w-[20px] hover:w-[10px]` on a hovered element was 10 px and is now 20 px;
  `hover:bg-red-500 hover:bg-[#f00]` paints `bg-red-500` in either written order; and
  `hover:bg-red-500 hover:bg-blue-500` paints red, where the stylesheet's own order used to paint blue.
