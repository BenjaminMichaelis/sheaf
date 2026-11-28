import { apiClient } from '@/services/apiClient'
import type { components } from '@/api/schema'

export type Group = components['schemas']['GroupResponse']

export type Session = components['schemas']['SessionResponse']

export type SessionSummary = components['schemas']['SessionSummaryResponse']

/** Meetings the group page looks back over for the trend, streaks and visitors. */
export const TREND_WEEKS = 8

export interface RosterPerson {
  id: string
  name: string
  isLeader: boolean
  /** Meetings attended out of the recent ones held while they were on the roster. */
  attended: number
  /** Recent meetings held while they were on the roster. */
  held: number
  lastAttended: string | null
  /** How many of the most recent meetings in a row they missed. */
  missedInARow: number
}

export interface Visitor {
  id: string
  name: string
  visits: number
  lastVisit: string
}

export interface GroupOverview {
  group: Group
  /** Recent meetings, oldest first. */
  sessions: Session[]
  roster: RosterPerson[]
  visitors: Visitor[]
}

/** Loads a group with its recent meetings and works out the roster stats the page shows. */
export async function loadGroupOverview(groupId: string): Promise<GroupOverview> {
  const path = { params: { path: { groupId } } }

  const [groupResult, membersResult, leadersResult, sessionsResult] = await Promise.all([
    apiClient.GET('/api/church/groups/{id}', { params: { path: { id: groupId } } }),
    apiClient.GET('/api/church/groups/{groupId}/members', { params: { path: { groupId }, query: { limit: 200 } } }),
    apiClient.GET('/api/church/groups/{groupId}/leaders', path),
    apiClient.GET('/api/church/groups/{groupId}/sessions', { params: { path: { groupId }, query: { limit: TREND_WEEKS } } }),
  ])

  if (!groupResult.data) throw new Error('Group not found.')

  const summaries = [...(sessionsResult.data?.items ?? [])].sort((a, b) => a.occurredOn.localeCompare(b.occurredOn))

  const sessions = await Promise.all(
    summaries.map(async summary => {
      const { data } = await apiClient.GET('/api/church/groups/{groupId}/sessions/{sessionId}', {
        params: { path: { groupId, sessionId: summary.id } },
      })

      if (!data) throw new Error('Could not load a meeting.')

      return data
    }),
  )

  const names = new Map<string, string>()

  for (const session of sessions) {
    for (const person of [...session.attendees, ...session.absent]) names.set(person.id, person.name)
  }

  const current = (items: components['schemas']['AssignmentResponse'][] | undefined) => (items ?? []).filter(a => a.endsAt === null)
  const leaderIds = new Set(current(leadersResult.data?.items).map(a => a.personId))
  const memberIds = current(membersResult.data?.items).map(a => a.personId)
  const rosterIds = [...new Set([...leaderIds, ...memberIds])]

  // Anyone who joined after the last meeting is not in any meeting's lists yet.
  await Promise.all(
    rosterIds.flatMap(id =>
      names.has(id)
        ? []
        : [
            apiClient.GET('/api/church/people/{id}', { params: { path: { id } } }).then(({ data }) => {
              if (data) names.set(id, data.name)
            }),
          ],
    ),
  )

  const roster = rosterIds.map(id => rosterStats(id, names.get(id) ?? 'Unknown', leaderIds.has(id), sessions))

  roster.sort((a, b) => Number(b.isLeader) - Number(a.isLeader) || a.name.localeCompare(b.name))

  const onRoster = new Set(rosterIds)
  const visitorMap = new Map<string, Visitor>()

  for (const session of sessions) {
    for (const person of session.attendees) {
      if (onRoster.has(person.id)) continue

      const visitor = visitorMap.get(person.id) ?? { id: person.id, name: person.name, visits: 0, lastVisit: session.occurredOn }

      visitor.visits += 1
      visitor.lastVisit = session.occurredOn
      visitorMap.set(person.id, visitor)
    }
  }

  const visitors = [...visitorMap.values()].sort((a, b) => b.lastVisit.localeCompare(a.lastVisit))

  return { group: groupResult.data, sessions, roster, visitors }
}

function rosterStats(id: string, name: string, isLeader: boolean, sessions: Session[]): RosterPerson {
  let attended = 0
  let held = 0
  let lastAttended: string | null = null
  let missedInARow = 0

  for (const session of sessions) {
    const came = session.attendees.some(p => p.id === id)
    const missed = session.absent.some(p => p.id === id)

    if (!came && !missed) continue

    held += 1

    if (came) {
      attended += 1
      lastAttended = session.occurredOn
      missedInARow = 0
    } else {
      missedInARow += 1
    }
  }

  return { id, name, isLeader, attended, held, lastAttended, missedInARow }
}

/** Formats a yyyy-mm-dd date as a short local label like "Tue, Nov 25", without shifting it across time zones. */
export function formatDay(isoDate: string, options: Intl.DateTimeFormatOptions = { weekday: 'short', month: 'short', day: 'numeric' }) {
  const [year = 1970, month = 1, day = 1] = isoDate.split('-').map(Number)

  return new Date(year, month - 1, day).toLocaleDateString(undefined, options)
}

/** Today's date as yyyy-mm-dd in the browser's time zone. */
export function todayIso() {
  const now = new Date()

  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`
}

/** Someone typed in as new while recording attendance, not yet saved. */
export interface NewPerson {
  key: string
  firstName: string
  lastName: string
}

export interface Confirmation {
  /** Meetings already recorded on the same date. */
  existingSessionIds: string[]
  /** For each new person (by index), people already in the church with a similar name. */
  similarPeople: { index: number; matches: components['schemas']['PersonLookupResponse'][] }[]
  newPeople: NewPerson[]
}

export type ConfirmationChoice =
  | { kind: 'cancel' }
  | { kind: 'open-existing'; sessionId: string }
  | { kind: 'save'; matches: Map<number, { id: string; name: string }> }

/** "Dana", "Dana and Elmer", "Dana, Elmer and Lyle". */
export function listNames(names: string[]) {
  if (names.length <= 2) return names.join(' and ')

  return `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]}`
}
