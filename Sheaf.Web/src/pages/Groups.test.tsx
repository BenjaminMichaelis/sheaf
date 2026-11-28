import { screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { json, mockApi, renderApp, signedInUser } from '@/test/renderApp'
import Groups from './Groups'

const day = 24 * 60 * 60 * 1000

describe('Groups', () => {
  it('Groups_CurrentAndEndedGroups_UsesChurchLabelAndListsEndedSeparately', async () => {
    const now = Date.now()
    mockApi({
      'GET /api/auth/user': () => json(signedInUser('Staff')),
      'GET /api/church/settings': () =>
        json({ name: 'Church 1', timeZoneId: 'America/Chicago', groupLabel: 'Village', groupLabelPlural: 'Villages' }),
      'GET /api/church/groups': () =>
        json({
          items: [
            { id: 'g1', name: 'Tuesday village', startsAt: new Date(now - 30 * day).toISOString(), endsAt: null, groupTypeId: null, version: 'v1' },
            { id: 'g2', name: 'Spring village', startsAt: new Date(now - 90 * day).toISOString(), endsAt: new Date(now - day).toISOString(), groupTypeId: null, version: 'v2' },
          ],
          nextOffset: null,
        }),
    })

    renderApp(<Groups />, '/groups')

    expect(await screen.findByRole('heading', { name: 'Villages' })).toBeInTheDocument()
    expect(await screen.findByText('Tuesday village')).toBeInTheDocument()
    const endedList = screen.getByRole('list', { name: 'Ended' })
    const currentList = screen.getByRole('list', { name: 'Current Villages' })
    expect(within(endedList).getByText('Spring village')).toBeInTheDocument()
    expect(within(endedList).queryByText('Tuesday village')).not.toBeInTheDocument()
    expect(within(currentList).queryByText('Spring village')).not.toBeInTheDocument()
  })

  it('Groups_NoGroups_SaysThereAreNoneInChurchWords', async () => {
    mockApi({
      'GET /api/auth/user': () => json(signedInUser()),
      'GET /api/church/settings': () =>
        json({ name: 'Church 1', timeZoneId: 'America/Chicago', groupLabel: 'Village', groupLabelPlural: 'Villages' }),
      'GET /api/church/groups': () => json({ items: [], nextOffset: null }),
    })

    renderApp(<Groups />, '/groups')

    expect(await screen.findByText('No Villages to show yet.')).toBeInTheDocument()
  })
})
