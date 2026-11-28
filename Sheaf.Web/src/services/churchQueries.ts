import { infiniteQueryOptions, queryOptions, skipToken, type QueryKey } from '@tanstack/react-query'
import type { components } from '@/api/schema'
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

export function groupQuery(churchId: string, groupId: string) {
  return queryOptions({
    queryKey: ['church', churchId, 'groups', groupId],
    queryFn: async ({ signal }) => {
      const { data } = await apiClient.GET('/api/church/groups/{id}', {
        params: { path: { id: groupId } },
        headers: churchHeaders(churchId),
        signal,
      })

      if (!data) throw new Error('Group response is empty.')

      return data
    },
  })
}

/** The active people on the group's roster on `occurredOn` (YYYY-MM-DD in the church's zone), by name. */
export function rosterQuery(churchId: string, groupId: string, occurredOn: string) {
  return queryOptions({
    queryKey: ['church', churchId, 'groups', groupId, 'roster', occurredOn],
    queryFn: async ({ signal }) => {
      const { data } = await apiClient.GET('/api/church/groups/{groupId}/roster', {
        params: { path: { groupId }, query: { occurredOn } },
        headers: churchHeaders(churchId),
        signal,
      })

      return data ?? []
    },
  })
}

/** The minimum name length the lookup API searches for. */
export const LOOKUP_MIN_LENGTH = 2

/** Active people whose name matches `name`; nothing is requested for a shorter name than the API searches for. */
export function peopleLookupQuery(churchId: string, name: string) {
  return queryOptions({
    queryKey: ['church', churchId, 'people', 'lookup', name],
    queryFn:
      name.length < LOOKUP_MIN_LENGTH
        ? skipToken
        : async ({ signal }) => {
            const { data } = await apiClient.GET('/api/church/people/lookup', {
              params: { query: { name } },
              headers: churchHeaders(churchId),
              signal,
            })

            return data ?? []
          },
  })
}

type SessionRequest = components['schemas']['SessionRequest']

/** Records a meeting and who came, creating new people in the same save. */
export async function recordMeeting(churchId: string, groupId: string, request: SessionRequest) {
  const { data } = await apiClient.POST('/api/church/groups/{groupId}/sessions', {
    params: { path: { groupId } },
    headers: churchHeaders(churchId),
    body: request,
  })

  if (!data) throw new Error('Meeting response is empty.')

  return data
}
