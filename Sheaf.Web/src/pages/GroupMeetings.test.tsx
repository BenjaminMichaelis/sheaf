import { Route, Routes } from 'react-router-dom'
import { screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { components } from '@/api/schema'
import { json, mockApi, renderApp, signedInUser } from '@/test/renderApp'
import { formatDate } from '@/utils/dates'
import GroupMeetings from './GroupMeetings'
import TakeAttendance from './TakeAttendance'

type Group = components['schemas']['GroupResponse']

type SessionSummary = components['schemas']['SessionSummaryResponse']

const settings = { name: 'Church 1', timeZoneId: 'America/Chicago', groupLabel: 'Village', groupLabelPlural: 'Villages' }
const village: Group = { id: 'g1', name: 'Tuesday village', startsAt: '2025-01-01T12:00:00Z', endsAt: null, groupTypeId: null, version: 'v1' }

function renderGroup(group: Partial<Group>, meetings: SessionSummary[], role: 'Leader' | 'Staff' = 'Leader') {
  mockApi({
    'GET /api/auth/user': () => json(signedInUser(role)),
    'GET /api/church/settings': () => json(settings),
    'GET /api/church/groups/g1': () => json({ ...village, ...group }),
    'GET /api/church/groups/g1/sessions': () => json({ items: meetings, nextOffset: null }),
  })

  return renderApp(
    <Routes>
      <Route path="/groups/:groupId" element={<GroupMeetings />} />
    </Routes>,
    '/groups/g1',
  )
}

describe('GroupMeetings', () => {
  it('GroupMeetings_GroupWithMeetings_ListsEachWithItsDateAndCountLinkingToIt', async () => {
    renderGroup({}, [
      { id: 's2', occurredOn: '2026-11-24', attendeeCount: 7, version: 'v' },
      { id: 's1', occurredOn: '2026-11-17', attendeeCount: 5, version: 'v' },
    ])

    expect(await screen.findByRole('heading', { name: 'Tuesday village' })).toBeInTheDocument()
    const meetings = await screen.findByRole('list', { name: 'Meetings' })
    const newest = within(meetings).getByRole('link', { name: new RegExp(formatDate('2026-11-24')) })
    expect(newest).toHaveTextContent('7 present')
    expect(newest).toHaveAttribute('href', '/groups/g1/meetings/s2')
    expect(within(meetings).getByRole('link', { name: new RegExp(formatDate('2026-11-17')) })).toHaveTextContent('5 present')
  })

  it('GroupMeetings_NoMeetingsYet_SaysSoAndOffersAttendance', async () => {
    renderGroup({}, [])

    expect(await screen.findByText('No meetings recorded yet.')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Take attendance' })).toHaveAttribute('href', '/groups/g1/attendance')
  })

  it('GroupMeetings_GroupNotStartedYet_DoesNotOfferAttendance', async () => {
    renderGroup({ startsAt: '2099-01-01T12:00:00Z' }, [])

    expect(await screen.findByText('No meetings recorded yet.')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Take attendance' })).not.toBeInTheDocument()
  })

  it('GroupMeetings_StaffOnEndedGroup_StillOffersAttendance', async () => {
    renderGroup({ endsAt: '2025-06-01T05:00:00Z' }, [], 'Staff')

    expect(await screen.findByText('Ended')).toBeInTheDocument()
    expect(await screen.findByRole('link', { name: 'Take attendance' })).toBeInTheDocument()
  })

  it('GroupMeetings_LeaderOnEndedGroup_ShowsItsHistoryInTheChurchsWordsWithoutRecording', async () => {
    renderGroup({ endsAt: '2025-06-01T05:00:00Z' }, [{ id: 's1', occurredOn: '2025-05-27', attendeeCount: 4, version: 'v' }])

    expect(await screen.findByText('Village')).toBeInTheDocument()
    expect(screen.getByText('Ended')).toBeInTheDocument()
    expect(await screen.findByRole('link', { name: /4 present/ })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Take attendance' })).not.toBeInTheDocument()
  })

  it('GroupMeetings_AfterRecordingAMeeting_ListsIt', async () => {
    const saved: SessionSummary[] = []
    mockApi({
      'GET /api/auth/user': () => json(signedInUser()),
      'GET /api/church/settings': () => json(settings),
      'GET /api/church/groups/g1': () => json(village),
      'GET /api/church/groups/g1/sessions': () => json({ items: saved, nextOffset: null }),
      'GET /api/church/groups/g1/roster': () => json([{ id: 'p-lena', name: 'Lena Leader' }]),
      'POST /api/church/groups/g1/sessions': async request => {
        const body: components['schemas']['SessionRequest'] = await request.json()
        saved.push({ id: 's1', occurredOn: body.occurredOn, attendeeCount: 1, version: 'v' })

        return json({ id: 's1', groupId: 'g1', occurredOn: body.occurredOn, description: null, attendees: [], absent: [], canEdit: true, version: 'v' }, 201)
      },
    })

    const { user } = renderApp(
      <Routes>
        <Route path="/groups/:groupId" element={<GroupMeetings />} />
        <Route path="/groups/:groupId/attendance" element={<TakeAttendance />} />
      </Routes>,
      '/groups/g1',
    )

    expect(await screen.findByText('No meetings recorded yet.')).toBeInTheDocument()
    await user.click(screen.getByRole('link', { name: 'Take attendance' }))
    await user.click(await screen.findByRole('checkbox', { name: 'Lena Leader' }))
    await user.click(screen.getByRole('button', { name: 'Save attendance' }))

    expect(await screen.findByRole('link', { name: /1 present/ })).toBeInTheDocument()
  })
})
