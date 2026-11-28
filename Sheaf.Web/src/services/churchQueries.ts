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
    // Ends in 'list', so refreshing the list never also refetches each group's details and meetings under it.
    queryKey: ['church', churchId, 'groups', 'list'],
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

const MEETINGS_PAGE_SIZE = 50

/** The prefix shared by everything cached about one group's meetings, so a save can refresh all of it at once. */
export function meetingsKey(churchId: string, groupId: string) {
  return ['church', churchId, 'groups', groupId, 'meetings'] as const
}

/** A group's meetings, newest first, with how many came; one page at a time. */
export function meetingsQuery(churchId: string, groupId: string) {
  return infiniteQueryOptions({
    queryKey: [...meetingsKey(churchId, groupId), 'list'],
    queryFn: async ({ pageParam, signal }) => {
      const { data } = await apiClient.GET('/api/church/groups/{groupId}/sessions', {
        params: { path: { groupId }, query: { offset: pageParam, limit: MEETINGS_PAGE_SIZE } },
        headers: churchHeaders(churchId),
        signal,
      })

      return { items: data?.items ?? [], nextOffset: data?.nextOffset == null ? null : Number(data.nextOffset) }
    },
    initialPageParam: 0,
    getNextPageParam: lastPage => lastPage.nextOffset,
  })
}

/** One meeting: who came, who on the roster did not, and whether the user may still correct it. */
export function meetingQuery(churchId: string, groupId: string, sessionId: string) {
  return queryOptions({
    queryKey: [...meetingsKey(churchId, groupId), sessionId],
    queryFn: async ({ signal }) => {
      const { data } = await apiClient.GET('/api/church/groups/{groupId}/sessions/{sessionId}', {
        params: { path: { groupId, sessionId } },
        headers: churchHeaders(churchId),
        signal,
      })

      if (!data) throw new Error('Meeting response is empty.')

      return data
    },
  })
}

type AttendeesRequest = components['schemas']['AttendeesRequest']

/**
 * Replaces who came to a meeting and its description. `version` is the meeting as the user last saw it: if someone
 * changed it since, the API refuses with 412 rather than overwrite their change.
 */
export async function correctMeeting(churchId: string, groupId: string, sessionId: string, version: string, request: AttendeesRequest) {
  const { data } = await apiClient.PUT('/api/church/groups/{groupId}/sessions/{sessionId}', {
    params: { path: { groupId, sessionId }, header: { 'If-Match': `"${version}"` } },
    headers: churchHeaders(churchId),
    body: request,
  })

  if (!data) throw new Error('Meeting response is empty.')

  return data
}

const PERSON_ATTENDANCE_PAGE_SIZE = 50

/** The prefix shared by everything cached about one person, so an update can refresh all of it at once. */
export function personKey(churchId: string, personId: string) {
  return ['church', churchId, 'people', personId] as const
}

/** A person's profile: name, contacts, household, and whether the user may edit them. */
export function personQuery(churchId: string, personId: string) {
  return queryOptions({
    queryKey: [...personKey(churchId, personId), 'profile'],
    queryFn: async ({ signal }) => {
      const { data } = await apiClient.GET('/api/church/people/{id}', {
        params: { path: { id: personId } },
        headers: churchHeaders(churchId),
        signal,
      })

      if (!data) throw new Error('Person response is empty.')

      return data
    },
  })
}

/** The groups a person is or was in or led, current first; only for those who may edit them. */
export function personGroupsQuery(churchId: string, personId: string) {
  return queryOptions({
    queryKey: [...personKey(churchId, personId), 'groups'],
    queryFn: async ({ signal }) => {
      const { data } = await apiClient.GET('/api/church/people/{id}/groups', {
        params: { path: { id: personId } },
        headers: churchHeaders(churchId),
        signal,
      })

      return data ?? []
    },
  })
}

/** The meetings a person came to, newest first, one page at a time; only for those who may edit them. */
export function personAttendanceQuery(churchId: string, personId: string) {
  return infiniteQueryOptions({
    queryKey: [...personKey(churchId, personId), 'attendance'],
    queryFn: async ({ pageParam, signal }) => {
      const { data } = await apiClient.GET('/api/church/people/{id}/attendance', {
        params: { path: { id: personId }, query: { offset: pageParam, limit: PERSON_ATTENDANCE_PAGE_SIZE } },
        headers: churchHeaders(churchId),
        signal,
      })

      return { items: data?.items ?? [], nextOffset: data?.nextOffset == null ? null : Number(data.nextOffset) }
    },
    initialPageParam: 0,
    getNextPageParam: lastPage => lastPage.nextOffset,
  })
}

type PersonRequest = components['schemas']['PersonRequest']

/**
 * Updates a person's name and contacts. `version` is the profile as the user last saw it: if someone changed it since,
 * the API refuses with 412 rather than overwrite their change.
 */
export async function updatePerson(churchId: string, personId: string, version: string, request: PersonRequest) {
  const { data } = await apiClient.PUT('/api/church/people/{id}', {
    params: { path: { id: personId }, header: { 'If-Match': `"${version}"` } },
    headers: churchHeaders(churchId),
    body: request,
  })

  if (!data) throw new Error('Person response is empty.')

  return data
}

/** A household's label and members (first names only), with the version an update is checked against. */
export function householdQuery(churchId: string, householdId: string) {
  return queryOptions({
    queryKey: ['church', churchId, 'households', householdId],
    queryFn: async ({ signal }) => {
      const { data } = await apiClient.GET('/api/church/households/{id}', {
        params: { path: { id: householdId } },
        headers: churchHeaders(churchId),
        signal,
      })

      if (!data) throw new Error('Household response is empty.')

      return data
    },
  })
}

type HouseholdRequest = components['schemas']['HouseholdRequest']

/** Starts a household. Without a label, the API names it after the first member's last name. */
export async function createHousehold(churchId: string, request: HouseholdRequest) {
  const { data } = await apiClient.POST('/api/church/households', { headers: churchHeaders(churchId), body: request })

  if (!data) throw new Error('Household response is empty.')

  return data
}

/**
 * Replaces a household's members, and its label when one is given (null keeps the current label). `version` is the
 * household as the user last saw it: if someone changed it since, the API refuses with 412.
 */
export async function updateHousehold(churchId: string, householdId: string, version: string, request: HouseholdRequest) {
  const { data } = await apiClient.PUT('/api/church/households/{id}', {
    params: { path: { id: householdId }, header: { 'If-Match': `"${version}"` } },
    headers: churchHeaders(churchId),
    body: request,
  })

  if (!data) throw new Error('Household response is empty.')

  return data
}
