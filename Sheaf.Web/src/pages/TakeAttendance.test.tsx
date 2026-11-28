import { Route, Routes } from 'react-router-dom'
import { screen, waitFor, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { components } from '@/api/schema'
import { CONFIRMATION_REQUIRED_PROBLEM_TYPE, type ConfirmationReasons } from '@/services/apiClient'
import { json, mockApi, renderApp, signedInUser } from '@/test/renderApp'
import { churchDateOf } from '@/utils/dates'
import TakeAttendance from './TakeAttendance'

type SessionRequest = components['schemas']['SessionRequest']

type PersonMatch = components['schemas']['PersonLookupResponse']

type SessionPerson = components['schemas']['SessionPersonResponse']

type ProblemBody = components['schemas']['HttpValidationProblemDetails'] & Partial<ConfirmationReasons>

const TIME_ZONE = 'America/Chicago'
const settings = { name: 'Church 1', timeZoneId: TIME_ZONE, groupLabel: 'Village', groupLabelPlural: 'Villages' }
const village = { id: 'g1', name: 'Tuesday village', startsAt: '2025-01-01T12:00:00Z', endsAt: null, groupTypeId: null, version: 'v1' }
const lena = { id: 'p-lena', name: 'Lena Leader' }
const max = { id: 'p-max', name: 'Max Member' }
const zoe: PersonMatch = { id: 'p-zoe', name: 'Zoe Visitor', householdLabel: 'Visitor household', groups: null }
const savedMeeting = { id: 's1', groupId: 'g1', occurredOn: '2026-11-27', description: null, attendees: [], absent: [], version: 'v' }

const problem = (status: number, body: ProblemBody) =>
  new Response(JSON.stringify({ status, ...body }), { status, headers: { 'content-type': 'application/problem+json' } })

const confirmation = (extensions: Partial<ConfirmationReasons>) =>
  problem(409, { type: CONFIRMATION_REQUIRED_PROBLEM_TYPE, title: 'This looks like a duplicate.', ...extensions })

interface ApiOptions {
  role?: 'Leader' | 'Staff'
  group?: { endsAt: string }
  /** The roster for each date. Defaults to Lena and Max on every date. */
  roster?: (occurredOn: string) => SessionPerson[]
  lookup?: (name: string) => Response | Promise<Response>
  save?: (request: SessionRequest, attempt: number) => Response | Promise<Response>
}

/** Stubs the API for one group and records the meetings saved and the names searched. */
function mockAttendanceApi({
  role = 'Leader',
  group,
  roster = () => [lena, max],
  lookup = () => json([zoe]),
  save = () => json(savedMeeting, 201),
}: ApiOptions = {}) {
  const saved: SessionRequest[] = []
  const searched: string[] = []
  mockApi({
    'GET /api/auth/user': () => json(signedInUser(role)),
    'GET /api/church/settings': () => json(settings),
    'GET /api/church/groups/g1': () => json({ ...village, ...group }),
    'GET /api/church/groups/g1/roster': request => json(roster(new URL(request.url).searchParams.get('occurredOn') ?? '')),
    'GET /api/church/people/lookup': request => {
      const name = new URL(request.url).searchParams.get('name') ?? ''
      searched.push(name)

      return lookup(name)
    },
    'POST /api/church/groups/g1/sessions': async request => {
      const body: SessionRequest = await request.json()
      saved.push(body)

      return save(body, saved.length)
    },
  })

  return { saved, searched }
}

function renderAttendance() {
  return renderApp(
    <Routes>
      <Route path="/groups/:groupId/attendance" element={<TakeAttendance />} />
      <Route path="/groups" element={<p>Groups page</p>} />
    </Routes>,
    '/groups/g1/attendance',
  )
}

// Found asynchronously: after a dialog closes, the page stays hidden from assistive tech until the close finishes.
const saveButton = () => screen.findByRole('button', { name: 'Save attendance' })
const nameBox = () => screen.findByRole('combobox', { name: 'Find or add a person' })

/** "Create new person", once the search for the typed name has finished (it can't be chosen while searching). */
async function createOption() {
  await waitFor(() => expect(screen.getByRole('option', { name: /Create new person/ })).toHaveAttribute('aria-disabled', 'false'))

  return screen.getByRole('option', { name: /Create new person/ })
}

describe('TakeAttendance', () => {
  it('TakeAttendance_TickRosterAndSave_RecordsChurchDateAndReturnsToGroups', async () => {
    // 03:00 UTC on March 1 is still February 28 in Chicago, so a meeting recorded then belongs to February 28.
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date('2026-03-01T03:00:00Z'))

    try {
      const { saved } = mockAttendanceApi({ roster: occurredOn => (occurredOn === '2026-02-28' ? [lena, max] : []) })
      const { user } = renderAttendance()

      const roster = await screen.findByRole('list', { name: 'Roster' })
      expect(await saveButton()).toBeDisabled()
      await user.click(within(roster).getByRole('checkbox', { name: 'Lena Leader' }))
      expect(screen.getByRole('status')).toHaveTextContent('1 present')
      await user.type(screen.getByLabelText('Description (optional)'), 'Potluck')
      await user.click(await saveButton())

      expect(await screen.findByText('Groups page')).toBeInTheDocument()
      expect(screen.getByText('Attendance saved.')).toBeInTheDocument()
      expect(saved).toEqual([{ occurredOn: '2026-02-28', personIds: [lena.id], newPeople: [], confirm: false, description: 'Potluck' }])
    } finally {
      vi.useRealTimers()
    }
  })

  it('TakeAttendance_SearchSomeoneOffTheRoster_SearchesTypedNameAndMarksThemPresent', async () => {
    const { saved, searched } = mockAttendanceApi()
    const { user } = renderAttendance()

    // The API matches names its own way (here a nickname), so every result it returns is offered, then Create last.
    await user.type(await nameBox(), 'Zoey')
    const zoeOption = await screen.findByRole('option', { name: /^Zoe Visitor/ })
    const options = screen.getAllByRole('option')
    expect(options).toHaveLength(2)
    expect(options[1]).toHaveAccessibleName(/Create new person “Zoey”/)
    expect(searched).toContain('Zoey')
    await user.click(zoeOption)
    expect(within(screen.getByRole('list', { name: 'Also came' })).getByText('Zoe Visitor')).toBeInTheDocument()
    await user.click(await saveButton())

    expect(await screen.findByText('Groups page')).toBeInTheDocument()
    expect(saved[0]?.personIds).toEqual([zoe.id])
  })

  it('TakeAttendance_SearchFindsSomeoneOnTheRoster_TicksThemOnTheRoster', async () => {
    mockAttendanceApi({ lookup: () => json([{ ...max, householdLabel: null, groups: null }]) })
    const { user } = renderAttendance()

    await user.type(await nameBox(), 'Max')
    await user.click(await screen.findByRole('option', { name: /^Max Member/ }))

    expect(screen.getByRole('checkbox', { name: 'Max Member' })).toBeChecked()
    expect(screen.queryByRole('list', { name: 'Also came' })).not.toBeInTheDocument()
  })

  it('TakeAttendance_EnterBeforeSearchFinishes_DoesNotCreateSomeoneWhoMayBeFound', async () => {
    mockAttendanceApi({ lookup: () => new Promise<Response>(() => {}) })
    const { user } = renderAttendance()

    await user.type(await nameBox(), 'Zoe Visitor{Enter}')

    expect(screen.queryByRole('dialog', { name: 'New person' })).not.toBeInTheDocument()
  })

  it('TakeAttendance_EnterInEmptyNameBox_DoesNotSave', async () => {
    const { saved } = mockAttendanceApi()
    const { user } = renderAttendance()

    await user.click(await screen.findByRole('checkbox', { name: 'Lena Leader' }))
    await user.click(await nameBox())
    await user.keyboard('{Enter}')

    expect(saved).toEqual([])
    expect(screen.queryByText('Groups page')).not.toBeInTheDocument()
  })

  it('TakeAttendance_SearchFails_SaysSoInsteadOfOnlyOfferingToCreate', async () => {
    mockAttendanceApi({ lookup: () => problem(429, { title: 'Too many requests.' }) })
    const { user } = renderAttendance()

    await user.type(await nameBox(), 'Zo')

    expect(await screen.findByText('Search failed: Too many requests.')).toBeInTheDocument()
  })

  it('TakeAttendance_CreateNewPersonFromSearch_SavesThemWithNameSplitAndOptionalContacts', async () => {
    const { saved } = mockAttendanceApi()
    const { user } = renderAttendance()

    await user.type(await nameBox(), 'Mary Ann Smith')
    await user.click(await createOption())
    const dialog = screen.getByRole('dialog', { name: 'New person' })
    expect(within(dialog).getByLabelText(/Name/)).toHaveValue('Mary Ann Smith')
    await user.type(within(dialog).getByLabelText('Phone (optional)'), '555-0100')
    await user.click(within(dialog).getByRole('button', { name: 'Add' }))
    await user.click(await saveButton())

    expect(await screen.findByText('Groups page')).toBeInTheDocument()
    expect(saved[0]?.newPeople).toEqual([{ firstName: 'Mary Ann', lastName: 'Smith', email: null, phone: '555-0100' }])
  })

  it('TakeAttendance_NewPersonMayAlreadyExist_UsesChosenExistingPersonInstead', async () => {
    const { saved } = mockAttendanceApi({
      save: (_, attempt) => (attempt === 1 ? confirmation({ similarPeople: [{ index: 0, matches: [zoe] }] }) : json(savedMeeting, 201)),
    })

    const { user } = renderAttendance()

    await user.type(await nameBox(), 'Zoe Visiter')
    await user.click(await createOption())
    await user.click(screen.getByRole('button', { name: 'Add' }))
    await user.click(await saveButton())

    const dialog = await screen.findByRole('dialog', { name: 'Check before saving' })
    expect(within(dialog).getByText('“Zoe Visiter” may already be here')).toBeInTheDocument()
    expect(within(dialog).queryByText(/already recorded for this date/)).not.toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Use this person: Zoe Visitor' }))
    await user.click(await saveButton())

    expect(await screen.findByText('Groups page')).toBeInTheDocument()
    expect(saved[1]).toMatchObject({ personIds: [zoe.id], newPeople: [], confirm: false })
  })

  it('TakeAttendance_MeetingAlreadyRecordedThatDay_SavesAnotherOnlyWhenConfirmed', async () => {
    const { saved } = mockAttendanceApi({
      save: (_, attempt) => (attempt === 1 ? confirmation({ existingSessionIds: ['s0'] }) : json(savedMeeting, 201)),
    })

    const { user } = renderAttendance()

    await user.click(await screen.findByRole('checkbox', { name: 'Max Member' }))
    await user.click(await saveButton())
    const dialog = await screen.findByRole('dialog', { name: 'Check before saving' })
    expect(within(dialog).getByText(/only if the village met twice/)).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Save anyway' }))

    expect(await screen.findByText('Groups page')).toBeInTheDocument()
    expect(saved.map(request => request.confirm)).toEqual([false, true])
  })

  it('TakeAttendance_DateChangedToOneWithoutThem_DoesNotSaveRosterTicksNoLongerShown', async () => {
    const today = churchDateOf(new Date(), TIME_ZONE)
    const { saved } = mockAttendanceApi({ roster: occurredOn => (occurredOn === today ? [lena, max] : []) })
    const { user } = renderAttendance()

    await user.click(await screen.findByRole('checkbox', { name: 'Max Member' }))
    const date = screen.getByLabelText(/Meeting date/)
    await user.clear(date)
    await user.type(date, '2025-06-01')
    expect(await screen.findByText('Nobody was on the roster that day.')).toBeInTheDocument()
    await user.type(await nameBox(), 'Zo')
    await user.click(await screen.findByRole('option', { name: /^Zoe Visitor/ }))
    await user.click(await saveButton())

    expect(await screen.findByText('Groups page')).toBeInTheDocument()
    expect(saved[0]).toMatchObject({ occurredOn: '2025-06-01', personIds: [zoe.id] })
  })

  it('TakeAttendance_SaveInFlight_SendsOnlyOneRequest', async () => {
    const { saved } = mockAttendanceApi({ save: () => new Promise<Response>(() => {}) })
    const { user } = renderAttendance()

    await user.click(await screen.findByRole('checkbox', { name: 'Max Member' }))
    await user.click(await saveButton())

    // A disabled button can't be pressed again, so the meeting is sent once.
    expect(await saveButton()).toBeDisabled()
    expect(saved).toHaveLength(1)
  })

  it('TakeAttendance_SaveFails_ShowsServerMessageAndKeepsTheForm', async () => {
    mockAttendanceApi({ save: () => problem(400, { title: 'Invalid tracker input.' }) })
    const { user } = renderAttendance()

    await user.click(await screen.findByRole('checkbox', { name: 'Max Member' }))
    await user.click(await saveButton())

    expect(await screen.findByRole('alert')).toHaveTextContent('Invalid tracker input.')
    expect(screen.getByRole('checkbox', { name: 'Max Member' })).toBeChecked()
  })

  it('TakeAttendance_LeaderOpensEndedGroup_SaysOnlyStaffCanRecordIt', async () => {
    mockAttendanceApi({ group: { endsAt: '2025-06-01T05:00:00Z' } })
    renderAttendance()

    expect(await screen.findByText('This village has ended, so only Staff can record its meetings.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Save attendance' })).not.toBeInTheDocument()
  })

  it('TakeAttendance_StaffOpensGroupEndedAtMidnight_DefaultsToItsLastDay', async () => {
    // Midnight in Chicago on June 1: the group's last day was May 31.
    mockAttendanceApi({ role: 'Staff', group: { endsAt: '2025-06-01T05:00:00Z' } })
    renderAttendance()

    expect(await screen.findByLabelText(/Meeting date/)).toHaveValue('2025-05-31')
  })
})
