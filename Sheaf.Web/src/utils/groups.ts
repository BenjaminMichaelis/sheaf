import type { components } from '@/api/schema'
import type { Capability } from '@/auth/permissions'

type Group = components['schemas']['GroupResponse']

export type Phase = 'upcoming' | 'current' | 'ended'

/** Where the group is in its life at `now`. Ended groups stay listed as history; Staff can create groups that start later. */
export function phaseOf(group: Group, now: Date): Phase {
  if (group.endsAt !== null && new Date(group.endsAt) <= now) return 'ended'

  return new Date(group.startsAt) > now ? 'upcoming' : 'current'
}

/**
 * Whether to offer recording a meeting for a group in `phase`: not before it starts (no date would be allowed), and
 * once it has ended only to those who can record for ended groups (Leaders lose a group when it ends).
 */
export function offersAttendance(phase: Phase, can: (capability: Capability) => boolean): boolean {
  return phase === 'current' || (phase === 'ended' && can('recordEndedGroups'))
}
