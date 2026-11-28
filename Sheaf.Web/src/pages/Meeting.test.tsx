import { Route, Routes } from 'react-router-dom'
import { screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { components } from '@/api/schema'
import { json, mockApi, renderApp, signedInUser } from '@/test/renderApp'
import { formatDate } from '@/utils/dates'
import Meeting from './Meeting'

type SessionResponse = components['schemas']['SessionResponse']

type AttendeesRequest = components['schemas']['AttendeesRequest']

const settings = { name: 'Church 1', timeZoneId: 'America/Chicago', groupLabel: 'Village', groupLabelPlural: 'Villages' }
const village = { id: 'g1', name: 'Tuesday village', startsAt: '2025-01-01T12:00:00Z', endsAt: null, groupTypeId: null, version: 'v1' }
const lena = { id: 'p-lena', name: 'Lena Leader' }
const max = { id: 'p-max', name: 'Max Member' }
const zoe = { id: 'p-zoe', name: 'Zoe Visitor' }

const meeting = (changes: Partial<SessionResponse> = {}): SessionResponse => ({
  id: 's1',
  groupId: 'g1',
  occurredOn: '2026-11-24',
  description: 'Potluck',
  attendees: [lena],
  absent: [max],
  canEdit: true,
  version: 'v1',
  ...changes,
})

interface Correction {
  request: AttendeesRequest
  ifMatch: string | null
}

/** Stubs one meeting; `reads` are returned in turn by each GET, and every PUT is recorded. */
function mockMeetingApi(reads: SessionResponse[], correct: (request: AttendeesRequest) => Response = () => json(meeting())) {
  const corrections: Correction[] = []
  let read = 0
  mockApi({
    'GET /api/auth/user': () => json(signedInUser()),
    'GET /api/church/settings': () => json(settings),
    'GET /api/church/groups/g1': () => json(village),
    'GET /api/church/groups/g1/sessions/s1': () => json(reads[Math.min(read++, reads.length - 1)]),
    'GET /api/church/people/lookup': () => json([{ ...zoe, householdLabel: null, groups: null }]),
    'PUT /api/church/groups/g1/sessions/s1': async request => {
      const body: AttendeesRequest = await request.json()
      corrections.push({ request: body, ifMatch: request.headers.get('If-Match') })

      return correct(body)
    },
  })

  return corrections
}

function renderMeeting() {
  return renderApp(
    <Routes>
      <Route path="/groups/:groupId/meetings/:sessionId" element={<Meeting />} />
    </Routes>,
    '/groups/g1/meetings/s1',
  )
}

describe('Meeting', () => {
  it('Meeting_RecordedMeeting_ShowsWhoCameWhoWasAbsentAndTheDescription', async () => {
    mockMeetingApi([meeting()])
    renderMeeting()

    expect(await screen.findByRole('heading', { name: formatDate('2026-11-24') })).toBeInTheDocument()
    expect(screen.getByText('Potluck')).toBeInTheDocument()
    expect(within(screen.getByRole('list', { name: 'Came (1)' })).getByText('Lena Leader')).toBeInTheDocument()
    expect(within(screen.getByRole('list', { name: 'Absent (1)' })).getByText('Max Member')).toBeInTheDocument()
  })

  it('Meeting_CannotBeCorrectedAnyMore_OffersNoCorrection', async () => {
    mockMeetingApi([meeting({ canEdit: false })])
    renderMeeting()

    expect(await screen.findByRole('list', { name: 'Came (1)' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Correct attendance' })).not.toBeInTheDocument()
  })

  it('Meeting_Correct_SendsChangesForTheVersionShownAndShowsTheResult', async () => {
    const corrections = mockMeetingApi([meeting()], () => json(meeting({ attendees: [max, zoe], absent: [lena], description: 'Potluck and games', version: 'v2' })))
    const { user } = renderMeeting()

    await user.click(await screen.findByRole('button', { name: 'Correct attendance' }))
    const listed = screen.getByRole('list', { name: 'Marked on this meeting' })
    expect(within(listed).getByRole('checkbox', { name: 'Lena Leader' })).toBeChecked()
    await user.click(within(listed).getByRole('checkbox', { name: 'Lena Leader' }))
    await user.click(within(listed).getByRole('checkbox', { name: 'Max Member' }))
    await user.type(screen.getByRole('combobox', { name: 'Find or add a person' }), 'Zo')
    await user.click(await screen.findByRole('option', { name: /^Zoe Visitor/ }))
    await user.type(screen.getByLabelText('Description (optional)'), ' and games')
    await user.click(screen.getByRole('button', { name: 'Save correction' }))

    expect(await screen.findByText('Attendance corrected.')).toBeInTheDocument()
    expect(corrections).toEqual([
      { request: expect.objectContaining({ personIds: [max.id, zoe.id], confirm: false, description: 'Potluck and games' }), ifMatch: '"v1"' },
    ])
    expect(within(await screen.findByRole('list', { name: 'Came (2)' })).getByText('Zoe Visitor')).toBeInTheDocument()
  })

  it('Meeting_ChangedBySomeoneElseFirst_SaysSoAndReloadsTheirVersion', async () => {
    const theirs = meeting({ attendees: [lena, max], absent: [], version: 'v2' })

    const corrections = mockMeetingApi([meeting(), theirs], () =>
      new Response(JSON.stringify({ title: 'The resource changed since it was read.', status: 412 }), {
        status: 412,
        headers: { 'content-type': 'application/problem+json' },
      }),
    )

    const { user } = renderMeeting()

    await user.click(await screen.findByRole('button', { name: 'Correct attendance' }))
    await user.click(screen.getByRole('checkbox', { name: 'Max Member' }))
    await user.click(screen.getByRole('button', { name: 'Save correction' }))
    expect(await screen.findByText(/Someone changed this meeting since you opened it/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Save correction' })).toBeDisabled()
    await user.click(screen.getByRole('button', { name: 'Reload' }))

    // Their version is loaded and the correction starts again from it.
    expect(await screen.findByRole('checkbox', { name: 'Max Member', checked: true })).toBeInTheDocument()
    expect(screen.queryByText(/Someone changed this meeting/)).not.toBeInTheDocument()
    expect(corrections).toHaveLength(1)
  })

  it('Meeting_EveryoneTakenOff_CanStillBeSaved', async () => {
    const corrections = mockMeetingApi([meeting()], () => json(meeting({ attendees: [], absent: [lena, max], version: 'v2' })))
    const { user } = renderMeeting()

    await user.click(await screen.findByRole('button', { name: 'Correct attendance' }))
    await user.click(screen.getByRole('checkbox', { name: 'Lena Leader' }))
    expect(screen.getByRole('status')).toHaveTextContent('0 present')
    await user.click(screen.getByRole('button', { name: 'Save correction' }))

    expect(await screen.findByText('Attendance corrected.')).toBeInTheDocument()
    expect(corrections[0]?.request.personIds).toEqual([])
  })
})
