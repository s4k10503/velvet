### Fixed

- Two variant rules of one rank that name the same class keep it while either holds:
  `nth-1:opacity-50 nth-2:opacity-50` dims the first row, and `data-[selected=true]:bg-blue-500
  data-[highlighted=true]:bg-blue-500` stays blue on a selected row that is not highlighted.
