import type { components } from '@/api/schema'

export type ChurchRole = components['schemas']['ChurchMembershipRole']

/**
 * What the UI may offer. This mirrors the API's role ladder only to decide which actions to show;
 * the API enforces every permission on its own.
 */
/** recordEndedGroups: Leaders lose a group once it ends, so only Staff record or correct its meetings. */
export type Capability = 'manageGroups' | 'managePeople' | 'manageChurch' | 'recordEndedGroups'

const ladder: readonly ChurchRole[] = ['Leader', 'Coordinator', 'Staff', 'Admin']

const minimumRole: Record<Capability, ChurchRole> = {
  manageGroups: 'Staff',
  managePeople: 'Staff',
  manageChurch: 'Admin',
  recordEndedGroups: 'Staff',
}

/** Whether someone with `role` in the current church may be offered `capability`. */
export function can(role: ChurchRole | null | undefined, capability: Capability): boolean {
  return role != null && ladder.indexOf(role) >= ladder.indexOf(minimumRole[capability])
}
