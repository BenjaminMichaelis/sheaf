import { infiniteQueryOptions, queryOptions, skipToken, type QueryKey } from '@tanstack/react-query'
import { apiClient, churchHeaders } from './apiClient'

const GROUPS_PAGE_SIZE = 50

// Every church query's key is ['church', churchId, ...], and each request sends that church explicitly, so data cached
// for one church can never be shown, or asked for, under another.

/** True for a cached query that belongs to a church other than `churchId` (or to any church, when `churchId` is null). */
export function isOtherChurchQuery(queryKey: QueryKey, churchId: string | null) {
  return queryKey[0] === 'church' && queryKey[1] !== churchId
}

/** The church's settings; nothing is requested while no church is selected. */
export function churchSettingsQuery(churchId: string | null) {
  return queryOptions({
    queryKey: ['church', churchId, 'settings'],
    queryFn:
      churchId === null
        ? skipToken
        : async ({ signal }) => {
            const { data } = await apiClient.GET('/api/church/settings', { headers: churchHeaders(churchId), signal })

            if (!data) throw new Error('Church settings response is empty.')

            return data
          },
  })
}

/** The groups the user can reach, one page at a time. */
export function groupsQuery(churchId: string) {
  return infiniteQueryOptions({
    queryKey: ['church', churchId, 'groups'],
    queryFn: async ({ pageParam, signal }) => {
      const { data } = await apiClient.GET('/api/church/groups', {
        params: { query: { offset: pageParam, limit: GROUPS_PAGE_SIZE } },
        headers: churchHeaders(churchId),
        signal,
      })

      return { items: data?.items ?? [], nextOffset: data?.nextOffset == null ? null : Number(data.nextOffset) }
    },
    initialPageParam: 0,
    getNextPageParam: lastPage => lastPage.nextOffset,
  })
}
