### Changed

- A `Spring` or `Bezier` variant swap whose classes on one side write the same slot more than once
  animates each slot toward the class the cascade lets hold it at rest. A shorthand beside one of its own
  longhands (`p-8 pt-2`, `size-8 w-20`, `rounded-3xl rounded-tl-lg`) animates each slot both sides name
  toward its own holder; both classes used to land instantly with the swap. Two stylesheet utilities on one slot
  (`opacity-50 opacity-20`) animate toward the one the stylesheet declares later rather than the later
  one in the class string. Important stylesheet utilities suppress fully covered plain tokens;
  important inline tokens outrank plain tokens of the same property. Surviving inline tokens take
  their slots over stylesheet utilities, with later writes deciding across a shorthand
  and its longhand. These conflicts used to animate
  toward the later class in the string and jump to the cascade's value when the play ended. A slot held
  by a class no magnitude is read from (`rounded-tl-full`, `scale-x-[.5]`) lands with the swap instead of
  being driven toward another class's value. A slot whose inline holder depends on an existing class
  projection also lands with the swap.
