import { onlineManager, QueryClient } from '@tanstack/react-query'
import { ApiError } from './apiClient'

const MAX_RETRIES = 3

/**
 * The app's query cache.
 * - The app is online-only, so requests are always attempted ('always' network mode): with no connection they fail
 *   and show their error and Retry, instead of pausing with nothing on screen. That mode turns off refetching on
 *   reconnect, so it is turned back on to clear those errors once the connection returns.
 * - Failed reads are retried like TanStack Query's default (three times), except when the API answered with a 4xx
 *   (asking again cannot fix a bad request, a missing permission or a church conflict) or the device is offline.
 * - Data stays fresh for 30 seconds, so returning to the tab does not reload every page at once.
 */
export function createQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        networkMode: 'always',
        refetchOnReconnect: true,
        staleTime: 30_000,
        retry: (failureCount, error) =>
          onlineManager.isOnline() && !(error instanceof ApiError && error.status < 500) && failureCount < MAX_RETRIES,
      },
    },
  })
}
