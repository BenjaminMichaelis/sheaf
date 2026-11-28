/** The parts of a TanStack infinite query result that paging reads. */
interface PagedResult<T> {
  data: { pages: { items: T[] }[] } | undefined
  error: Error | null
  isError: boolean
  isFetching: boolean
  isFetchingNextPage: boolean
  isFetchNextPageError: boolean
  hasNextPage: boolean
}

/**
 * What a paged list shows. Errors are hidden while their retry is in flight, and a failed later page, or a failed
 * background refresh, keeps the pages already shown.
 */
export function pagedState<T>(result: PagedResult<T>) {
  const moreError = result.isFetchNextPageError && !result.isFetching ? (result.error?.message ?? null) : null

  return {
    items: result.data?.pages.flatMap(page => page.items) ?? [],
    loaded: result.data !== undefined,
    firstPageError: result.data === undefined && result.isError && !result.isFetching ? (result.error?.message ?? null) : null,
    moreError,
    loading: result.isFetching && (result.data === undefined || result.isFetchingNextPage),
    // TanStack Query recommends calling fetchNextPage only when nothing else is fetching, so a refresh is not overwritten.
    canLoadMore: result.hasNextPage && !result.isFetching && moreError === null,
  }
}

export type PagedState = ReturnType<typeof pagedState>
