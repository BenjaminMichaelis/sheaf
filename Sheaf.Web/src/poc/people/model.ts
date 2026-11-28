import type { components } from '@/api/schema'

export type MyPerson = components['schemas']['MyPersonResponse']

export type PersonDetail = components['schemas']['PersonDetailResponse']

export type Touchpoint = components['schemas']['TouchpointResponse']

// Mirrors the C# TouchpointKind enum, which the API sends as a number.
export const TouchpointKind = { Met: 0, Texted: 1, Called: 2, Coffee: 3, SawThem: 4, Other: 5 } as const

const touchpointLabels = ['Met', 'Texted', 'Called', 'Got coffee', 'Saw them', 'Other']

export function touchpointLabel(kind: Touchpoint['kind']) {
  return touchpointLabels[Number(kind)] ?? 'Touchpoint'
}

/** "today", "yesterday", "3 days ago", "2 weeks ago". */
export function timeAgo(iso: string) {
  const days = Math.floor((Date.now() - new Date(iso).getTime()) / 86_400_000)

  if (days <= 0) return 'today'

  if (days === 1) return 'yesterday'

  if (days < 14) return `${days} days ago`

  return `${Math.floor(days / 7)} weeks ago`
}

/** A vCard 3.0 contact for "Add to my phone". A minor has no phone or email to include. */
export function toVCard(detail: PersonDetail) {
  const { person } = detail
  const [first = '', ...rest] = person.name.split(' ')
  const escape = (value: string) => value.replace(/[\\;,]/g, match => `\\${match}`)
  const lines = ['BEGIN:VCARD', 'VERSION:3.0', `N:${escape(rest.join(' '))};${escape(first)};;;`, `FN:${escape(person.name)}`]

  if (person.phone) lines.push(`TEL;TYPE=CELL:${person.phone}`)

  if (detail.email) lines.push(`EMAIL:${detail.email}`)

  const note = [person.howWeMet && `Met: ${person.howWeMet}`, person.dorm, person.classYear, person.groupName].filter(Boolean).join(' · ')

  if (note) lines.push(`NOTE:${escape(note)}`)

  lines.push('END:VCARD')

  return lines.join('\r\n')
}

/** Alert colour for a nudge: overdue first contact is an error, missed meetings a warning, quiet spells info. */
export function nudgeColor(urgency: number | string | undefined): 'error' | 'warning' | 'info' {
  const level = Number(urgency ?? 0)

  return level >= 3 ? 'error' : level === 2 ? 'warning' : 'info'
}
