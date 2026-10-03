### Fixed

- Two variant rules of one rank that name the same class keep it while either holds:
  `nth-1:opacity-50 nth-2:opacity-50` dims the first row, and `data-[selected=true]:bg-blue-500
  data-[highlighted=true]:bg-blue-500` stays blue on a selected row that is not highlighted.
- An element no longer gathers another variant manipulator each time its className changes a `group-*:` or
  `peer-*:` rule stacked over another variant (`group-hover:hover:bg-red-500`), each time a className drops a
  stacked rule and brings it back (`group-hover:hover:bg-red-500`, `hover:focus:bg-red-500`), nor each time
  the outer variant of a three-deep stack whose middle is `dark:`, a breakpoint or `disabled:` turns off
  (`hover:dark:focus:bg-red-500`).
- Above `md`, `p-[12px] md:pt-6` takes its top padding from `pt-6` and the other three sides from `p-[12px]`,
  where the shorthand used to keep all four.
