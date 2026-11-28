import { Route, Routes } from 'react-router-dom'
import { act, screen, waitFor, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import type { components } from '@/api/schema'
import { json, mockApi, renderApp, signedInUser } from '@/test/renderApp'
import { formatChurchDate, formatDate } from '@/utils/dates'
import Person from './Person'

type PersonResponse = components['schemas']['PersonResponse']

type PersonRequest = components['schemas']['PersonRequest']

type PersonGroup = components['schemas']['PersonGroupResponse']

type PersonAttendance = components['schemas']['PersonAttendanceResponse']

const TIME_ZONE = 'America/Chicago'
const settings = { name: 'Church 1', timeZoneId: TIME_ZONE, groupLabel: 'Village', groupLabelPlural: 'Villages' }

const max = (changes: Partial<PersonResponse> = {}): PersonResponse => ({
  id: 'p-max',
  name: 'Max Member',
  firstName: 'Max',
  lastName: 'Member',
  email: 'max@example.com',
  phone: '555-0100',
  householdId: 'h1',
  householdLabel: 'The Member Family',
  householdMembers: [{ id: 'p-mia', firstName: 'Mia' }],
  isActive: true,
  canEdit: true,
  version: 'v1',
  mergedIntoId: null,
  ...changes,
})

const groups: PersonGroup[] = [
  { groupId: 'g1', groupName: 'Tuesday village', role: 'Member', startsAt: '2025-01-01T12:00:00Z', endsAt: null, isCurrent: true },
  { groupId: 'g2', groupName: 'Spring village', role: 'Leader', startsAt: '2024-03-01T12:00:00Z', endsAt: '2024-06-01T12:00:00Z', isCurrent: false },
]

const attendance: PersonAttendance[] = [{ sessionId: 's1', groupId: 'g1', groupName: 'Tuesday village', occurredOn: '2026-11-24' }]

interface Update {
  request: PersonRequest
  ifMatch: string | null
}

interface ApiOptions {
  role?: 'Leader' | 'Staff'
  /** Returned in turn by each profile read: a profile, or a function answering with an error. */
  reads?: (PersonResponse | (() => Response))[]
  update?: (request: PersonRequest) => Response
}

/** Stubs one person; records every update and which history endpoints were asked for. */
function mockPersonApi({ role = 'Leader', reads = [max()], update = () => json(max({ version: 'v2' })) }: ApiOptions = {}) {
  const updates: Update[] = []
  const historyRequests: string[] = []
  let read = 0
  mockApi({
    'GET /api/auth/user': () => json(signedInUser(role)),
    'GET /api/church/settings': () => json(settings),
    'GET /api/church/people/p-max': () => {
      const next = reads[Math.min(read++, reads.length - 1)] ?? max()

      return next instanceof Function ? next() : json(next)
    },
    'GET /api/church/people/p-max/groups': () => {
      historyRequests.push('groups')

      return json(groups)
    },
    'GET /api/church/people/p-max/attendance': () => {
      historyRequests.push('attendance')

      return json({ items: attendance, nextOffset: null })
    },
    'PUT /api/church/people/p-max': async request => {
      const body: PersonRequest = await request.json()
      updates.push({ request: body, ifMatch: request.headers.get('If-Match') })

      return update(body)
    },
  })

  return { updates, historyRequests }
}

function renderPerson() {
  return renderApp(
    <Routes>
      <Route path="/people/:personId" element={<Person />} />
    </Routes>,
    '/people/p-max',
  )
}

const problem = (status: number, title: string) =>
  new Response(JSON.stringify({ title, status }), { status, headers: { 'content-type': 'application/problem+json' } })

describe('Person', () => {
  it('Person_CanEdit_ShowsDetailsHouseholdGroupsAndAttendance', async () => {
    mockPersonApi()
    renderPerson()

    expect(await screen.findByRole('heading', { level: 1, name: 'Max Member' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'max@example.com' })).toHaveAttribute('href', 'mailto:max@example.com')
    expect(screen.getByRole('link', { name: '555-0100' })).toHaveAttribute('href', 'tel:555-0100')
    expect(screen.getByText('The Member Family: Mia')).toBeInTheDocument()
    // The church's own word, and current roles apart from past ones (UI-PPL-04).
    expect(screen.getByRole('heading', { name: 'Villages' })).toBeInTheDocument()
    const current = await screen.findByRole('list', { name: 'Current' })
    expect(within(current).getByRole('link', { name: /Tuesday village/ })).toHaveAttribute('href', '/groups/g1')
    expect(within(current).getByText(`Member, since ${formatChurchDate('2025-01-01T12:00:00Z', TIME_ZONE)}`)).toBeInTheDocument()
    const past = screen.getByRole('list', { name: 'Past and upcoming' })
    expect(
      within(past).getByText(`Leader, ${formatChurchDate('2024-03-01T12:00:00Z', TIME_ZONE)} – ${formatChurchDate('2024-06-01T12:00:00Z', TIME_ZONE)}`),
    ).toBeInTheDocument()
    expect(within(past).queryByText('Tuesday village')).not.toBeInTheDocument()
    expect(screen.getAllByRole('term').map(term => term.textContent)).toEqual(['Email', 'Phone', 'Household'])
    const meetings = await screen.findByRole('list', { name: 'Attendance' })
    expect(within(meetings).getByRole('link', { name: `${formatDate('2026-11-24')}, Tuesday village` })).toHaveAttribute('href', '/groups/g1/meetings/s1')
  })

  it('Person_RecentVisitorForALeader_ShowsBasicDetailsWithoutEditingOrHistory', async () => {
    const { historyRequests } = mockPersonApi({ reads: [max({ canEdit: false })] })
    renderPerson()

    expect(await screen.findByRole('link', { name: 'max@example.com' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Edit details' })).not.toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Attendance' })).not.toBeInTheDocument()
    expect(historyRequests).toEqual([])
  })

  it('Person_NotVisibleToTheUser_SaysSoWithoutSayingWhetherTheyExist', async () => {
    mockPersonApi({ reads: [() => problem(404, 'Resource not found.')] })
    renderPerson()

    expect(await screen.findByText("You can't see this person's details.")).toBeInTheDocument()
  })

  it('Person_MergedRecord_PointsToThePersonItWasMergedIntoWithoutEditing', async () => {
    mockPersonApi({ role: 'Staff', reads: [max({ mergedIntoId: 'p-kept', isActive: false })] })
    renderPerson()

    expect(await screen.findByRole('link', { name: 'another person' })).toHaveAttribute('href', '/people/p-kept')
    expect(screen.getByText('Inactive')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Edit details' })).not.toBeInTheDocument()
  })

  it('Person_LeaderEditsDetails_SendsTheNameSplitForTheVersionShownWithoutChangingActive', async () => {
    const { updates } = mockPersonApi({ update: () => json(max({ name: 'Maxine Member', firstName: 'Maxine', email: null, version: 'v2' })) })
    const { user } = renderPerson()

    await user.click(await screen.findByRole('button', { name: 'Edit details' }))
    expect(screen.getByRole('heading', { name: 'Edit details' })).toHaveFocus()
    expect(screen.queryByRole('switch', { name: 'Active' })).not.toBeInTheDocument()
    await user.clear(screen.getByLabelText(/^Name/))
    await user.type(screen.getByLabelText(/^Name/), 'Maxine Member')
    await user.clear(screen.getByLabelText('Email (optional)'))
    await user.click(screen.getByRole('button', { name: 'Save details' }))

    expect(await screen.findByText('Details saved.')).toBeInTheDocument()
    expect(updates).toEqual([
      { request: { firstName: 'Maxine', lastName: 'Member', email: null, phone: '555-0100', isActive: null }, ifMatch: '"v1"' },
    ])
    expect(await screen.findByRole('heading', { level: 1, name: 'Maxine Member' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Edit details' })).toHaveFocus()
  })

  it('Person_StaffMarksInactive_SendsIsActiveFalse', async () => {
    const { updates } = mockPersonApi({ role: 'Staff' })
    const { user } = renderPerson()

    await user.click(await screen.findByRole('button', { name: 'Edit details' }))
    await user.click(screen.getByRole('switch', { name: 'Active' }))
    await user.click(screen.getByRole('button', { name: 'Save details' }))

    expect(await screen.findByText('Details saved.')).toBeInTheDocument()
    expect(updates[0]?.request.isActive).toBe(false)
  })

  it('Person_ChangedBySomeoneElseFirst_SaysSoAndReloadsTheirVersion', async () => {
    const { updates } = mockPersonApi({
      reads: [max(), max({ phone: '555-0199', version: 'v2' })],
      update: () => problem(412, 'The resource changed since it was read.'),
    })

    const { user } = renderPerson()

    await user.click(await screen.findByRole('button', { name: 'Edit details' }))
    await user.click(screen.getByRole('button', { name: 'Save details' }))
    expect(await screen.findByText(/Someone changed these details since you opened them/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Save details' })).toBeDisabled()
    await user.click(screen.getByRole('button', { name: 'Reload' }))

    expect(await screen.findByDisplayValue('555-0199')).toBeInTheDocument()
    expect(updates).toHaveLength(1)
  })

  it('Person_RefreshedWhileEditing_KeepsTheEditsAndSavesAgainstTheVersionStartedFrom', async () => {
    const { updates } = mockPersonApi({ reads: [max(), max({ phone: '555-0199', version: 'v2' })] })
    const { user, queryClient } = renderPerson()

    await user.click(await screen.findByRole('button', { name: 'Edit details' }))
    await user.clear(screen.getByLabelText('Phone (optional)'))
    await user.type(screen.getByLabelText('Phone (optional)'), '555-0123')
    await act(() => queryClient.invalidateQueries())
    await waitFor(() => expect(screen.getByLabelText('Phone (optional)')).toHaveValue('555-0123'))
    await user.click(screen.getByRole('button', { name: 'Save details' }))

    expect(await screen.findByText('Details saved.')).toBeInTheDocument()
    expect(updates[0]).toMatchObject({ request: { phone: '555-0123' }, ifMatch: '"v1"' })
  })

  it('Person_SaveRefused_ShowsTheServerMessageAndKeepsTheForm', async () => {
    mockPersonApi({ update: () => problem(400, 'Invalid tracker input.') })
    const { user } = renderPerson()

    await user.click(await screen.findByRole('button', { name: 'Edit details' }))
    await user.click(screen.getByRole('button', { name: 'Save details' }))

    expect(await screen.findByText('Invalid tracker input.')).toBeInTheDocument()
    expect(screen.getByLabelText(/^Name/)).toHaveValue('Max Member')
  })

  it('Person_MergedRecord_ShowsNoHistory', async () => {
    const { historyRequests } = mockPersonApi({ role: 'Staff', reads: [max({ mergedIntoId: 'p-kept', isActive: false })] })
    renderPerson()

    expect(await screen.findByRole('link', { name: 'another person' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Attendance' })).not.toBeInTheDocument()
    expect(historyRequests).toEqual([])
  })

  it('Person_EditOnlyContactsOfMultiWordName_KeepsTheNamePartsAsStored', async () => {
    const anna = { name: 'Anna van der Berg', firstName: 'Anna', lastName: 'van der Berg' }
    const { updates } = mockPersonApi({ reads: [max(anna)], update: () => json(max({ ...anna, version: 'v2' })) })
    const { user } = renderPerson()

    await user.click(await screen.findByRole('button', { name: 'Edit details' }))
    await user.clear(screen.getByLabelText('Phone (optional)'))
    await user.type(screen.getByLabelText('Phone (optional)'), '555-0123')
    await user.click(screen.getByRole('button', { name: 'Save details' }))

    expect(await screen.findByText('Details saved.')).toBeInTheDocument()
    expect(updates[0]?.request).toMatchObject({ firstName: 'Anna', lastName: 'van der Berg', phone: '555-0123' })
  })

  it('Person_NameChangedToOneWord_SendsItAsFirstNameOnly', async () => {
    const { updates } = mockPersonApi()
    const { user } = renderPerson()

    await user.click(await screen.findByRole('button', { name: 'Edit details' }))
    await user.clear(screen.getByLabelText(/^Name/))
    await user.type(screen.getByLabelText(/^Name/), 'Maxie')
    await user.click(screen.getByRole('button', { name: 'Save details' }))

    expect(await screen.findByText('Details saved.')).toBeInTheDocument()
    expect(updates[0]?.request).toMatchObject({ firstName: 'Maxie', lastName: null })
  })

  it('Person_NameCleared_SaysANameIsNeededAndCannotSave', async () => {
    mockPersonApi()
    const { user } = renderPerson()

    await user.click(await screen.findByRole('button', { name: 'Edit details' }))
    await user.clear(screen.getByLabelText(/^Name/))

    expect(screen.getByText('Enter a name.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Save details' })).toBeDisabled()
  })

  it('Person_ReloadedAfterSomeoneElsesChange_SavesAgainstTheirVersion', async () => {
    let attempts = 0

    const { updates } = mockPersonApi({
      reads: [max(), max({ phone: '555-0199', version: 'v2' })],
      update: () => (++attempts === 1 ? problem(412, 'The resource changed since it was read.') : json(max({ version: 'v3' }))),
    })

    const { user } = renderPerson()

    await user.click(await screen.findByRole('button', { name: 'Edit details' }))
    await user.click(screen.getByRole('button', { name: 'Save details' }))
    await user.click(await screen.findByRole('button', { name: 'Reload' }))
    await screen.findByDisplayValue('555-0199')
    await user.click(screen.getByRole('button', { name: 'Save details' }))

    expect(await screen.findByText('Details saved.')).toBeInTheDocument()
    expect(updates.map(update => update.ifMatch)).toEqual(['"v1"', '"v2"'])
  })

  it('Person_ReloadedVersionCanNoLongerBeEdited_ClosesTheFormAndSaysSo', async () => {
    mockPersonApi({ reads: [max(), max({ canEdit: false, version: 'v2' })], update: () => problem(412, 'The resource changed since it was read.') })
    const { user } = renderPerson()

    await user.click(await screen.findByRole('button', { name: 'Edit details' }))
    await user.click(screen.getByRole('button', { name: 'Save details' }))
    await user.click(await screen.findByRole('button', { name: 'Reload' }))

    expect(await screen.findByText('You can no longer edit these details.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Save details' })).not.toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole('heading', { level: 1 })).toHaveFocus())
  })

  it('Person_AccessLostWhileEditing_ClosesTheFormAndSaysSo', async () => {
    mockPersonApi({ reads: [max(), () => problem(404, 'Resource not found.')], update: () => problem(404, 'Resource not found.') })
    const { user } = renderPerson()

    await user.click(await screen.findByRole('button', { name: 'Edit details' }))
    await user.click(screen.getByRole('button', { name: 'Save details' }))

    expect(await screen.findByText('You can no longer edit these details.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Save details' })).not.toBeInTheDocument()
    // The person can't be seen any more, so the page says so rather than offering Edit from the old details.
    expect(await screen.findByText("You can't see this person's details.")).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Edit details' })).not.toBeInTheDocument()
  })

  it('Person_EditRefusedAsForbidden_ClosesTheFormAndSaysSo', async () => {
    mockPersonApi({ reads: [max(), max({ canEdit: false, version: 'v2' })], update: () => problem(403, 'Access denied.') })
    const { user } = renderPerson()

    await user.click(await screen.findByRole('button', { name: 'Edit details' }))
    await user.click(screen.getByRole('button', { name: 'Save details' }))

    expect(await screen.findByText('You can no longer edit these details.')).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Edit details' })).not.toBeInTheDocument())
  })

  it('Person_PhoneWithExtension_LinksOnlyTheNumberToDial', async () => {
    mockPersonApi({ reads: [max({ phone: '555-0100 ext 2' })] })
    renderPerson()

    expect(await screen.findByRole('link', { name: '555-0100 ext 2' })).toHaveAttribute('href', 'tel:555-0100')
  })

  it('Person_Cancel_KeepsTheDetailsAsTheyWereAndReturnsFocus', async () => {
    const { updates } = mockPersonApi()
    const { user } = renderPerson()

    await user.click(await screen.findByRole('button', { name: 'Edit details' }))
    await user.clear(screen.getByLabelText('Phone (optional)'))
    await user.click(screen.getByRole('button', { name: 'Cancel' }))

    expect(screen.getByRole('link', { name: '555-0100' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Edit details' })).toHaveFocus()
    expect(updates).toEqual([])
  })
})
