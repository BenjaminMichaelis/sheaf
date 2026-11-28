import { QueryClient, type QueryKey } from '@tanstack/react-query'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { FIRST_CHURCH as CHURCH_A, SECOND_CHURCH as CHURCH_B, json } from '@/test/renderApp'
import { setActiveChurch } from './apiClient'
import { churchSettingsQuery, groupQuery, groupsQuery, peopleLookupQuery, rosterQuery } from './churchQueries'

/** Each church query, built for a given church, with how to load it into a cache. */
const queries: [string, (churchId: string) => { queryKey: QueryKey; load: (client: QueryClient) => Promise<void> }][] = [
  ['churchSettingsQuery', churchId => ({ queryKey: churchSettingsQuery(churchId).queryKey, load: async client => { await client.fetchQuery(churchSettingsQuery(churchId)) } })],
  ['groupsQuery', churchId => ({ queryKey: groupsQuery(churchId).queryKey, load: async client => { await client.fetchInfiniteQuery(groupsQuery(churchId)) } })],
  ['groupQuery', churchId => ({ queryKey: groupQuery(churchId, 'g1').queryKey, load: async client => { await client.fetchQuery(groupQuery(churchId, 'g1')) } })],
  ['rosterQuery', churchId => ({ queryKey: rosterQuery(churchId, 'g1', '2026-03-01').queryKey, load: async client => { await client.fetchQuery(rosterQuery(churchId, 'g1', '2026-03-01')) } })],
  ['peopleLookupQuery', churchId => ({ queryKey: peopleLookupQuery(churchId, 'Zo').queryKey, load: async client => { await client.fetchQuery(peopleLookupQuery(churchId, 'Zo')) } })],
]

/** Answers every church request with a body that suits its path, and returns the requests made. */
function mockChurchApi() {
  return vi.spyOn(globalThis, 'fetch').mockImplementation(async input => {
    const path = new URL(input instanceof Request ? input.url : String(input)).pathname

    if (path.endsWith('/settings')) return json({ name: 'Church', timeZoneId: 'America/Chicago', groupLabel: 'Group', groupLabelPlural: 'Groups' })

    if (path.endsWith('/groups')) return json({ items: [], nextOffset: null })

    if (path.endsWith('/groups/g1')) return json({ id: 'g1', name: 'Group', startsAt: '2025-01-01T00:00:00Z', endsAt: null, groupTypeId: null, version: 'v' })

    return json([])
  })
}

describe('church queries', () => {
  afterEach(() => setActiveChurch(null))

  it.each(queries)('%s_PageNowActsForAnotherChurch_StillAsksForItsOwnChurch', async (_, forChurch) => {
    const fetchMock = mockChurchApi()
    // A query left over from church A (say, a background refresh) must not ask as the church the page switched to.
    setActiveChurch(CHURCH_B)

    await forChurch(CHURCH_A).load(new QueryClient())

    const [input] = fetchMock.mock.calls[0] ?? []
    expect(input instanceof Request ? input.headers.get('X-Sheaf-Church') : null).toBe(CHURCH_A)
  })

  it.each(queries)('%s_LoadedForOneChurch_IsNotCachedForAnother', async (_, forChurch) => {
    mockChurchApi()
    const client = new QueryClient()

    await forChurch(CHURCH_A).load(client)

    expect(client.getQueryData(forChurch(CHURCH_A).queryKey)).toBeDefined()
    expect(client.getQueryData(forChurch(CHURCH_B).queryKey)).toBeUndefined()
  })
})
