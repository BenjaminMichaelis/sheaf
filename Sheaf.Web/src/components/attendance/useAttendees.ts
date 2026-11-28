import { useRef, useState } from 'react'
import type { components } from '@/api/schema'
import type { PossibleDuplicate } from '@/components/ConfirmMeetingDialog'
import { confirmationReasons } from '@/services/apiClient'
import { fullName } from '@/utils/names'

type PersonRequest = components['schemas']['PersonRequest']

/** An existing person named on a meeting. */
export interface Named {
  id: string
  name: string
}

/** Someone to create when the meeting is saved. The key tells them apart in lists and warnings. */
export interface NewPerson {
  key: number
  request: PersonRequest
}

/** What the API asked to confirm before saving. */
export interface Confirmation {
  sameDay: boolean
  duplicates: PossibleDuplicate[]
}

/**
 * Who is marked present on a meeting being recorded or corrected: people `listed` on screen (the roster, or everyone
 * already on a meeting) ticked or not, people found by search, and new people to create. Only people listed or found
 * count, so a change to what is listed (such as a new date's roster) never saves someone no longer shown.
 */
export function useAttendees(listed: readonly Named[], initiallyPresent: readonly string[] = []) {
  const [present, setPresent] = useState<ReadonlySet<string>>(() => new Set(initiallyPresent))
  const [found, setFound] = useState<Named[]>([])
  const [newPeople, setNewPeople] = useState<NewPerson[]>([])
  // Only tells new people apart; it never changes what is shown, so it is not state.
  const nextKey = useRef(0)
  const [creating, setCreating] = useState({ open: false, name: '' })
  const [confirmation, setConfirmation] = useState<Confirmation | null>(null)

  const listedIds = new Set(listed.map(person => person.id))
  // People found by search who are already listed are ticked there instead.
  const others = found.filter(person => !listedIds.has(person.id))
  const personIds = [...listed, ...others].flatMap(person => (present.has(person.id) ? [person.id] : []))

  const toggle = (id: string) =>
    setPresent(previous => {
      const next = new Set(previous)

      if (!next.delete(id)) next.add(id)

      return next
    })

  const markPresent = (person: Named) => {
    if (!listedIds.has(person.id) && !found.some(other => other.id === person.id)) {
      setFound(previous => [...previous, { id: person.id, name: person.name }])
    }

    setPresent(previous => new Set(previous).add(person.id))
  }

  const removeFound = (id: string) => {
    setFound(previous => previous.filter(person => person.id !== id))
    setPresent(previous => {
      const next = new Set(previous)
      next.delete(id)

      return next
    })
  }

  const addNewPerson = (request: PersonRequest) => {
    const key = nextKey.current++
    setNewPeople(previous => [...previous, { key, request }])
    setCreating(previous => ({ ...previous, open: false }))
  }

  const removeNewPerson = (key: number) => setNewPeople(previous => previous.filter(person => person.key !== key))

  /** Swaps a new person the API thinks may already exist for the existing match the user chose. */
  const chooseExisting = (duplicate: PossibleDuplicate, person: Named) => {
    removeNewPerson(duplicate.key)
    markPresent(person)
    setConfirmation(previous => {
      if (previous === null) return null
      const duplicates = previous.duplicates.filter(other => other.key !== duplicate.key)

      // Nothing left to confirm once every warning is resolved, so the dialog closes and Save is used as normal.
      return duplicates.length === 0 && !previous.sameDay ? null : { ...previous, duplicates }
    })
  }

  /**
   * Opens the confirmation when `error` is the API asking to confirm the save; `people` are the new people that save
   * sent, which its warnings point into. Returns whether it did.
   */
  const askToConfirm = (error: Error, people: readonly NewPerson[]) => {
    const reasons = confirmationReasons(error)

    if (reasons === null) return false

    setConfirmation({
      sameDay: reasons.existingSessionIds.length > 0,
      duplicates: reasons.similarPeople.flatMap(similar => {
        const person = people[similar.index]

        return person ? [{ key: person.key, name: fullName(person.request), matches: similar.matches }] : []
      }),
    })

    return true
  }

  return {
    present,
    others,
    newPeople,
    personIds,
    count: personIds.length + newPeople.length,
    creating,
    confirmation,
    toggle,
    markPresent,
    removeFound,
    startCreating: (name: string) => setCreating({ open: true, name }),
    stopCreating: () => setCreating(previous => ({ ...previous, open: false })),
    addNewPerson,
    removeNewPerson,
    chooseExisting,
    askToConfirm,
    closeConfirmation: () => setConfirmation(null),
  }
}

export type Attendees = ReturnType<typeof useAttendees>
