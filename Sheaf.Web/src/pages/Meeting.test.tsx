import { Route, Routes } from 'react-router-dom'
import { act, screen, waitFor, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { components } from '@/api/schema'
import { json, mockApi, renderApp, signedInUser } from '@/test/renderApp'
import { formatDate } from '@/utils/dates'
import { CONFIRMATION_REQUIRED_PROBLEM_TYPE } from '@/services/apiClient'
import GroupMeetings from './GroupMeetings'
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

    expect(await screen.findByRole('heading', { name: `Tuesday village, ${formatDate('2026-11-24')}` })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Back to Tuesday village' })).toHaveAttribute('href', '/groups/g1')
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

  it('Meeting_CorrectByAddingSomeoneNew_SendsThemToBeCreated', async () => {
    const corrections = mockMeetingApi([meeting()])
    const { user } = renderMeeting()

    await user.click(await screen.findByRole('button', { name: 'Correct attendance' }))
    await user.type(screen.getByRole('combobox', { name: 'Find or add a person' }), 'Nia New')
    await user.click(await createOption())
    await user.click(screen.getByRole('button', { name: 'Add' }))
    await user.click(await screen.findByRole('button', { name: 'Save correction' }))

    expect(await screen.findByText('Attendance corrected.')).toBeInTheDocument()
    expect(corrections[0]?.request).toMatchObject({ personIds: [lena.id], newPeople: [{ firstName: 'Nia', lastName: 'New' }] })
  })

  it('Meeting_CorrectionAddsSomeoneWhoMayExist_UsesTheChosenMatchWithoutShowingAnError', async () => {
    const zoeMatch = { ...zoe, householdLabel: null, groups: null }

    const corrections = mockMeetingApi([meeting()], request =>
      request.newPeople?.length
        ? new Response(
            JSON.stringify({ type: CONFIRMATION_REQUIRED_PROBLEM_TYPE, title: 'This looks like a duplicate.', status: 409, similarPeople: [{ index: 0, matches: [zoeMatch] }] }),
            { status: 409, headers: { 'content-type': 'application/problem+json' } },
          )
        : json(meeting({ attendees: [lena, zoe], version: 'v2' })),
    )

    const { user } = renderMeeting()

    await user.click(await screen.findByRole('button', { name: 'Correct attendance' }))
    await user.type(screen.getByRole('combobox', { name: 'Find or add a person' }), 'Zoe Visiter')
    await user.click(await createOption())
    await user.click(screen.getByRole('button', { name: 'Add' }))
    await user.click(await screen.findByRole('button', { name: 'Save correction' }))
    const dialog = await screen.findByRole('dialog', { name: 'Check before saving' })
    await user.click(within(dialog).getByRole('button', { name: 'Use this person: Zoe Visitor' }))

    // Choosing the match answers the warning, so it is not shown again as an error.
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(screen.queryByText('This looks like a duplicate.')).not.toBeInTheDocument()
    await user.click(await screen.findByRole('button', { name: 'Save correction' }))

    expect(await screen.findByText('Attendance corrected.')).toBeInTheDocument()
    expect(corrections[1]?.request).toMatchObject({ personIds: [lena.id, zoe.id], newPeople: [], confirm: false })
  })

  it('Meeting_SearchFindsSomeoneAlreadyChosen_ListsThemOnce', async () => {
    const corrections = mockMeetingApi([meeting()])
    const { user } = renderMeeting()

    await user.click(await screen.findByRole('button', { name: 'Correct attendance' }))

    for (let time = 0; time < 2; time++) {
      await user.type(screen.getByRole('combobox', { name: 'Find or add a person' }), 'Zo')
      await user.click(await screen.findByRole('option', { name: /^Zoe Visitor/ }))
    }

    expect(within(screen.getByRole('list', { name: 'Also came' })).getAllByText('Zoe Visitor')).toHaveLength(1)
    await user.click(screen.getByRole('button', { name: 'Save correction' }))
    expect(await screen.findByText('Attendance corrected.')).toBeInTheDocument()
    expect(corrections[0]?.request.personIds).toEqual([lena.id, zoe.id])
  })

  it('Meeting_CancelCorrection_KeepsTheMeetingAsItWasAndReturnsFocus', async () => {
    const corrections = mockMeetingApi([meeting()])
    const { user } = renderMeeting()

    await user.click(await screen.findByRole('button', { name: 'Correct attendance' }))
    expect(screen.getByRole('heading', { name: 'Correct attendance' })).toHaveFocus()
    await user.click(screen.getByRole('checkbox', { name: 'Lena Leader' }))
    await user.click(screen.getByRole('button', { name: 'Cancel' }))

    expect(within(screen.getByRole('list', { name: 'Came (1)' })).getByText('Lena Leader')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Correct attendance' })).toHaveFocus()
    expect(corrections).toEqual([])
  })

  it('Meeting_CorrectionWindowEndsWhileEditing_ClosesTheEditorAndSaysSo', async () => {
    mockMeetingApi([meeting(), meeting({ canEdit: false })], () =>
      new Response(JSON.stringify({ title: 'Access denied.', status: 403 }), { status: 403, headers: { 'content-type': 'application/problem+json' } }),
    )
    const { user } = renderMeeting()

    await user.click(await screen.findByRole('button', { name: 'Correct attendance' }))
    await user.click(screen.getByRole('button', { name: 'Save correction' }))

    expect(await screen.findByText('This meeting can no longer be corrected.')).toBeInTheDocument()
    expect(await screen.findByRole('list', { name: 'Came (1)' })).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Correct attendance' })).not.toBeInTheDocument())
    // With no Correct button to return to, focus goes to the page heading rather than nowhere.
    expect(screen.getByRole('heading', { level: 1 })).toHaveFocus()
  })

  it('Meeting_RefreshedWhileCorrecting_KeepsTheEditsAndCorrectsTheVersionStartedFrom', async () => {
    const corrections = mockMeetingApi([meeting(), meeting({ attendees: [lena, max], absent: [], version: 'v2' })], () =>
      json(meeting({ attendees: [], absent: [lena, max], version: 'v3' })),
    )

    const { user, queryClient } = renderMeeting()

    await user.click(await screen.findByRole('button', { name: 'Correct attendance' }))
    await user.click(screen.getByRole('checkbox', { name: 'Lena Leader' }))
    // A background refresh (such as returning to the tab) brings someone else's newer version.
    await act(() => queryClient.invalidateQueries())
    await waitFor(() => expect(screen.getByRole('heading', { level: 1 })).toBeInTheDocument())

    expect(screen.getByRole('checkbox', { name: 'Lena Leader' })).not.toBeChecked()
    expect(screen.getByRole('checkbox', { name: 'Max Member' })).not.toBeChecked()
    await user.click(screen.getByRole('button', { name: 'Save correction' }))
    expect(await screen.findByText('Attendance corrected.')).toBeInTheDocument()
    expect(corrections[0]?.ifMatch).toBe('"v1"')
  })

  it('Meeting_ReloadedVersionCanNoLongerBeCorrected_ClosesTheEditorAndSaysSo', async () => {
    mockMeetingApi([meeting(), meeting({ canEdit: false, version: 'v2' })], () =>
      new Response(JSON.stringify({ title: 'The resource changed since it was read.', status: 412 }), {
        status: 412,
        headers: { 'content-type': 'application/problem+json' },
      }),
    )
    const { user } = renderMeeting()

    await user.click(await screen.findByRole('button', { name: 'Correct attendance' }))
    await user.click(screen.getByRole('button', { name: 'Save correction' }))
    await user.click(await screen.findByRole('button', { name: 'Reload' }))

    expect(await screen.findByText('This meeting can no longer be corrected.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Save correction' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Correct attendance' })).not.toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 1 })).toHaveFocus()
  })

  it('Meeting_ReloadFails_SaysSo', async () => {
    let reads = 0
    mockApi({
      'GET /api/auth/user': () => json(signedInUser()),
      'GET /api/church/settings': () => json(settings),
      'GET /api/church/groups/g1': () => json(village),
      'GET /api/church/groups/g1/sessions/s1': () =>
        ++reads === 1 ? json(meeting()) : new Response(JSON.stringify({ title: 'Resource not found.', status: 404 }), { status: 404, headers: { 'content-type': 'application/problem+json' } }),
      'PUT /api/church/groups/g1/sessions/s1': () =>
        new Response(JSON.stringify({ title: 'The resource changed since it was read.', status: 412 }), { status: 412, headers: { 'content-type': 'application/problem+json' } }),
    })
    const { user } = renderMeeting()

    await user.click(await screen.findByRole('button', { name: 'Correct attendance' }))
    await user.click(screen.getByRole('button', { name: 'Save correction' }))
    await user.click(await screen.findByRole('button', { name: 'Reload' }))

    expect(await screen.findByText('Could not reload the meeting: Resource not found.')).toBeInTheDocument()
  })

  it('Meeting_AfterCorrecting_TheGroupsMeetingListShowsTheNewCount', async () => {
    let attendeeCount = 1
    mockApi({
      'GET /api/auth/user': () => json(signedInUser()),
      'GET /api/church/settings': () => json(settings),
      'GET /api/church/groups/g1': () => json(village),
      'GET /api/church/groups/g1/sessions': () => json({ items: [{ id: 's1', occurredOn: '2026-11-24', attendeeCount, version: 'v1' }], nextOffset: null }),
      'GET /api/church/groups/g1/sessions/s1': () => json(meeting()),
      'PUT /api/church/groups/g1/sessions/s1': () => {
        attendeeCount = 2

        return json(meeting({ attendees: [lena, max], absent: [], version: 'v2' }))
      },
    })

    const { user } = renderApp(
      <Routes>
        <Route path="/groups/:groupId" element={<GroupMeetings />} />
        <Route path="/groups/:groupId/meetings/:sessionId" element={<Meeting />} />
      </Routes>,
      '/groups/g1',
    )

    await user.click(await screen.findByRole('link', { name: /1 present/ }))
    await user.click(await screen.findByRole('button', { name: 'Correct attendance' }))
    await user.click(screen.getByRole('checkbox', { name: 'Max Member' }))
    await user.click(screen.getByRole('button', { name: 'Save correction' }))
    expect(await screen.findByText('Attendance corrected.')).toBeInTheDocument()
    await user.click(screen.getByRole('link', { name: 'Back to Tuesday village' }))

    expect(await screen.findByRole('link', { name: /2 present/ })).toBeInTheDocument()
  })
})

/** "Create new person", once the search for the typed name has finished (it can't be chosen while searching). */
async function createOption() {
  await waitFor(() => expect(screen.getByRole('option', { name: /Create new person/ })).toHaveAttribute('aria-disabled', 'false'))

  return screen.getByRole('option', { name: /Create new person/ })
}
