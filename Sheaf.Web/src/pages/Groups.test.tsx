import { Route, Routes } from 'react-router-dom'
import { screen, within } from '@testing-library/react'
import { onlineManager } from '@tanstack/react-query'
import { describe, expect, it } from 'vitest'
import type { components } from '@/api/schema'
import ChurchConflictDialog from '@/components/ChurchConflictDialog'
import Layout from '@/components/Layout'
import Login from './Login'
import { CHURCH_CONFLICT_PROBLEM_TYPE } from '@/services/apiClient'
import { FIRST_CHURCH, SECOND_CHURCH, json, mockApi, renderApp, signedInUser } from '@/test/renderApp'
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

  it('Groups_FirstPageFailsThenRetrySucceeds_ShowsErrorThenGroups', async () => {
    let groupCalls = 0
    mockApi({
      'GET /api/auth/user': () => json(signedInUser()),
      'GET /api/church/settings': () => json(villageSettings),
      'GET /api/church/groups': () =>
        ++groupCalls === 1
          ? problem('Groups are unavailable.', 503)
          : json({ items: [group('g1', 'Tuesday village', -30)], nextOffset: null }),
    })

    const { user } = renderApp(<Groups />, '/groups')
    expect(await screen.findByRole('alert')).toHaveTextContent('Groups are unavailable.')

    await user.click(screen.getByRole('button', { name: 'Retry' }))

    expect(await screen.findByText('Tuesday village')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('Groups_Offline_ShowsErrorWithRetry', async () => {
    onlineManager.setOnline(false)

    try {
      mockApi({
        'GET /api/auth/user': () => json(signedInUser()),
        'GET /api/church/settings': () => json(villageSettings),
        'GET /api/church/groups': () => Promise.reject(new TypeError('Failed to fetch')),
      })

      renderApp(<Groups />, '/groups')

      expect(await screen.findByRole('alert')).toHaveTextContent('Failed to fetch')
      expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
    } finally {
      onlineManager.setOnline(true)
    }
  })

  it('Groups_AfterSwitchingChurch_ShowsOnlyTheNewChurchsGroupsAndLabels', async () => {
    const account = signedInUser('Staff', [FIRST_CHURCH, SECOND_CHURCH])
    const circleSettings = { ...villageSettings, name: 'Church 2', groupLabel: 'Circle', groupLabelPlural: 'Circles' }
    let releaseSecondChurchGroups = () => {}

    const secondChurchGroupsReleased = new Promise<void>(resolve => {
      releaseSecondChurchGroups = resolve
    })

    mockApi({
      'GET /api/auth/user': () => json(account),
      'GET /api/church/settings': request =>
        json(request.headers.get('X-Sheaf-Church') === SECOND_CHURCH ? circleSettings : villageSettings),
      'GET /api/church/groups': request =>
        request.headers.get('X-Sheaf-Church') === SECOND_CHURCH
          ? secondChurchGroupsReleased.then(() => json({ items: [group('g2', 'Second church village', -30)], nextOffset: null }))
          : json({ items: [group('g1', 'First church village', -30)], nextOffset: null }),
      [`POST /api/auth/church/${SECOND_CHURCH}`]: () => json({ ...account, currentChurch: account.churches?.[1] ?? null }),
    })

    const { user } = renderGroupsInApp()
    expect(await screen.findByText('First church village')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Church 1, switch church' }))
    await user.click(screen.getByRole('menuitem', { name: 'Church 2' }))
    await user.click(await screen.findByRole('button', { name: 'Open navigation' }))
    await user.click(await screen.findByRole('link', { name: 'Circles' }))

    // While the new church's groups are still loading, nothing from the old church is shown.
    expect(await screen.findByRole('heading', { name: 'Circles' })).toBeInTheDocument()
    expect(screen.getByRole('progressbar', { name: 'Loading' })).toBeInTheDocument()
    expect(screen.queryByText('First church village')).not.toBeInTheDocument()

    releaseSecondChurchGroups()

    expect(await screen.findByText('Second church village')).toBeInTheDocument()
    expect(screen.queryByText('First church village')).not.toBeInTheDocument()
  })

  it('Groups_AfterLogoutAndLoginAgain_ShowsNothingFromTheEarlierSession', async () => {
    const account = signedInUser('Staff')
    let groupCalls = 0
    mockApi({
      'GET /api/auth/user': () => json(account),
      'GET /api/church/settings': () => json(villageSettings),
      'GET /api/church/groups': () =>
        ++groupCalls === 1
          ? json({ items: [group('g1', 'Earlier village', -30)], nextOffset: null })
          : new Promise<Response>(() => {}),
      'POST /api/auth/logout': () => new Response(null, { status: 204 }),
      'POST /api/auth/login': () => json(account),
    })

    const { user } = renderGroupsInApp()
    expect(await screen.findByText('Earlier village')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Logout' }))
    await user.type(await screen.findByLabelText(/Email/), 'leader@example.com')
    await user.type(screen.getByLabelText(/Password/), 'correct horse battery staple')
    await user.click(within(screen.getByRole('main')).getByRole('button', { name: 'Login' }))
    await user.click(await screen.findByRole('button', { name: 'Open navigation' }))
    await user.click(await screen.findByRole('link', { name: 'Villages' }))

    expect(await screen.findByRole('progressbar', { name: 'Loading' })).toBeInTheDocument()
    expect(screen.queryByText('Earlier village')).not.toBeInTheDocument()
  })

  // %s, not $role: Vitest would read "$role_Offers…" as one property name and print "undefined".
  it.each<['Leader' | 'Staff', boolean]>([
    ['Leader', false],
    ['Staff', true],
  ])('Groups_%s_OffersAttendanceForCurrentGroupsAndForEndedOnlyToStaff', async (role, endedOffered) => {
    mockApi({
      'GET /api/auth/user': () => json(signedInUser(role)),
      'GET /api/church/settings': () => json(villageSettings),
      'GET /api/church/groups': () =>
        json({
          items: [group('g1', 'Tuesday village', -30), group('g2', 'Spring village', -90, -1), group('g3', 'Autumn village', 14)],
          nextOffset: null,
        }),
    })

    renderApp(<Groups />, '/groups')

    expect(await screen.findByRole('link', { name: 'Take attendance for Tuesday village' })).toHaveAttribute('href', '/groups/g1/attendance')
    expect(screen.queryByRole('link', { name: 'Take attendance for Autumn village' })).not.toBeInTheDocument()
    const ended = screen.queryByRole('link', { name: 'Take attendance for Spring village' })

    if (endedOffered) expect(ended).toBeInTheDocument()
    else expect(ended).not.toBeInTheDocument()
  })
})

/** The groups page inside the app's layout, with login, so tests can switch church, log out and navigate back. */
function renderGroupsInApp() {
  return renderApp(
    <Routes>
      <Route path="/" element={<Layout />}>
        <Route index element={<p>Home page</p>} />
        <Route path="login" element={<Login />} />
        <Route path="groups" element={<Groups />} />
      </Route>
    </Routes>,
    '/groups',
  )
}
