import type { components } from '@/api/schema'

type PersonName = Pick<components['schemas']['PersonRequest'], 'firstName' | 'lastName'>

type PersonMatch = components['schemas']['PersonLookupResponse']

/**
 * Splits one name box into the API's first and last name: the last word is the last name and the rest the first name,
 * so nobody has to enter their name in parts (#188 UI-PPL-02). A single word is a first name only.
 */
export function splitName(name: string): PersonName {
  const words = name.trim().split(/\s+/)
  const lastName = words.length > 1 ? words.pop() : undefined

  return { firstName: words.join(' '), lastName: lastName ?? null }
}

/** A person's name as the API shows it: first and last name joined by a space. */
export function fullName(person: PersonName): string {
  return [person.firstName, person.lastName].filter(Boolean).join(' ')
}

/** What tells a found person apart from others with a similar name: their household and, for Staff, their groups. */
export function personHint(person: PersonMatch): string | undefined {
  return [person.householdLabel, person.groups?.join(', ')].filter(Boolean).join(' · ') || undefined
}
