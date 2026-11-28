import { screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { components } from '@/api/schema'
import ChurchConflictDialog from '@/components/ChurchConflictDialog'
import { CHURCH_CONFLICT_PROBLEM_TYPE } from '@/services/apiClient'
import { json, mockApi, renderApp, signedInUser } from '@/test/renderApp'
import Groups from './Groups'

type Group = components['schemas']['GroupResponse']

const day = 24 * 60 * 60 * 1000
const villageSettings = { name: 'Church 1', timeZoneId: 'America/Chicago', groupLabel: 'Village', groupLabelPlural: 'Villages' }

function group(id: string, name: string, startsInDays: number, endsInDays: number | null = null): Group {
  const now = Date.now()

  return {
    id,
    name,
    startsAt: new Date(now + startsInDays * day).toISOString(),
    endsAt: endsInDays === null ? null : new Date(now + endsInDays * day).toISOString(),
    groupTypeId: null,
    version: `v-${id}`,
  }
}

const problem = (title: string, status: number) =>
  new Response(JSON.stringify({ title, status }), { status, headers: { 'content-type': 'application/problem+json' } })

describe('Groups', () => {
  it('Groups_CurrentUpcomingAndEndedGroups_UsesChurchLabelAndListsEachSeparately', async () => {
    mockApi({
      'GET /api/auth/user': () => json(signedInUser('Staff')),
      'GET /api/church/settings': () => json(villageSettings),
      'GET /api/church/groups': () =>
        json({
          items: [
            group('g1', 'Tuesday village', -30),
            group('g2', 'Spring village', -90, -1),
            group('g3', 'Autumn village', 14),
          ],
          nextOffset: null,
        }),
    })

    renderApp(<Groups />, '/groups')

    expect(await screen.findByRole('heading', { name: 'Villages' })).toBeInTheDocument()
    const currentList = await screen.findByRole('list', { name: 'Current Villages' })
    expect(within(currentList).getByText('Tuesday village')).toBeInTheDocument()
    expect(within(currentList).queryByText('Autumn village')).not.toBeInTheDocument()
    const upcomingList = screen.getByRole('list', { name: 'Upcoming' })
    expect(within(upcomingList).getByText('Autumn village')).toBeInTheDocument()
    expect(within(upcomingList).getByText(/^Starts /)).toBeInTheDocument()
    const endedList = screen.getByRole('list', { name: 'Ended' })
    expect(within(endedList).getByText('Spring village')).toBeInTheDocument()
    expect(within(endedList).queryByText('Tuesday village')).not.toBeInTheDocument()
  })

  it('Groups_NoGroups_SaysThereAreNoneInChurchWords', async () => {
    mockApi({
      'GET /api/auth/user': () => json(signedInUser()),
      'GET /api/church/settings': () => json(villageSettings),
      'GET /api/church/groups': () => json({ items: [], nextOffset: null }),
    })

    renderApp(<Groups />, '/groups')

    expect(await screen.findByText('No Villages to show yet.')).toBeInTheDocument()
  })

  it('Groups_SettingsFailThenRetrySucceeds_HidesDatesUntilChurchZoneLoads', async () => {
    let settingsCalls = 0
    mockApi({
      'GET /api/auth/user': () => json(signedInUser()),
      'GET /api/church/settings': () => (++settingsCalls === 1 ? problem('Settings unavailable.', 503) : json(villageSettings)),
      'GET /api/church/groups': () => json({ items: [group('g1', 'Tuesday village', -30)], nextOffset: null }),
    })

    const { user } = renderApp(<Groups />, '/groups')

    expect(await screen.findByText(/Dates are hidden.*Settings unavailable\./)).toBeInTheDocument()
    expect(screen.getByText('Tuesday village')).toBeInTheDocument()
    expect(screen.queryByText(/^Since /)).not.toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Retry' }))

    expect(await screen.findByText(/^Since /)).toBeInTheDocument()
    expect(screen.queryByText(/Dates are hidden/)).not.toBeInTheDocument()
  })

  it('Groups_SettingsStillLoading_ShowsGroupsWithoutDates', async () => {
    mockApi({
      'GET /api/auth/user': () => json(signedInUser()),
      'GET /api/church/settings': () => new Promise<Response>(() => {}),
      'GET /api/church/groups': () => json({ items: [group('g1', 'Tuesday village', -30)], nextOffset: null }),
    })

    renderApp(<Groups />, '/groups')

    expect(await screen.findByText('Tuesday village')).toBeInTheDocument()
    expect(screen.queryByText(/^Since /)).not.toBeInTheDocument()
    expect(screen.queryByText(/Dates are hidden/)).not.toBeInTheDocument()
  })

  it('Groups_LoadMoreAfterChurchSwitchedElsewhere_OpensConflictDialogWithoutAddingRows', async () => {
    mockApi({
      'GET /api/auth/user': () => json(signedInUser('Staff')),
      'GET /api/church/settings': () => json(villageSettings),
      'GET /api/church/groups': request =>
        new URL(request.url).searchParams.get('offset') === '0'
          ? json({ items: [group('g1', 'First village', -30)], nextOffset: 1 })
          : new Response(JSON.stringify({ type: CHURCH_CONFLICT_PROBLEM_TYPE, title: 'Church selection does not match this request.', status: 409 }), {
              status: 409,
              headers: { 'content-type': 'application/problem+json' },
            }),
    })

    const { user } = renderApp(
      <>
        <Groups />
        <ChurchConflictDialog reload={() => {}} />
      </>,
      '/groups',
    )

    await user.click(await screen.findByRole('button', { name: 'Load more' }))

    expect(await screen.findByRole('dialog', { name: 'Your church changed' })).toBeInTheDocument()
    // The open modal hides the page from assistive tech, so include hidden elements when counting rows.
    expect(screen.getAllByRole('listitem', { hidden: true })).toHaveLength(1)
    expect(screen.getByText('First village')).toBeInTheDocument()
  })

  it('Groups_LoadMoreFailsThenRetrySucceeds_KeepsEarlierPagesAndClearsError', async () => {
    let laterPageCalls = 0
    mockApi({
      'GET /api/auth/user': () => json(signedInUser('Staff')),
      'GET /api/church/settings': () => json(villageSettings),
      'GET /api/church/groups': request => {
        if (new URL(request.url).searchParams.get('offset') === '0') {
          return json({ items: [group('g1', 'First village', -30)], nextOffset: 1 })
        }

        return ++laterPageCalls === 1
          ? problem('Try again shortly.', 503)
          : json({ items: [group('g2', 'Second village', -20)], nextOffset: null })
      },
    })

    const { user } = renderApp(<Groups />, '/groups')
    await user.click(await screen.findByRole('button', { name: 'Load more' }))
    expect(await screen.findByText('Try again shortly.')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Retry' }))

    expect(await screen.findByText('Second village')).toBeInTheDocument()
    expect(screen.getByText('First village')).toBeInTheDocument()
    expect(screen.queryByText('Try again shortly.')).not.toBeInTheDocument()
  })
})
