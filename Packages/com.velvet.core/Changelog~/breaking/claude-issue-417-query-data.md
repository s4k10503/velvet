### Changed

- `QueryOptions<T>` derives from the new `QueryOptions<TQueryFnData, TData>`, which declares every option, and
  `QueryProperties.All` includes the new `QueryProperties.IsPlaceholderData`.
