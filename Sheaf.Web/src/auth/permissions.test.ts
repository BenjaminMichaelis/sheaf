import { describe, expect, it } from 'vitest'
import { can, type ChurchRole } from './permissions'

describe('can', () => {
  it.each<[ChurchRole, boolean, boolean]>([
    ['Leader', false, false],
    ['Coordinator', false, false],
    ['Staff', true, false],
    ['Admin', true, true],
  ])('can_%s_OffersGroupManagement%sAndChurchSettings%s', (role, manages, managesChurch) => {
    expect(can(role, 'manageGroups')).toBe(manages)
    expect(can(role, 'managePeople')).toBe(manages)
    expect(can(role, 'manageChurch')).toBe(managesChurch)
  })

  it('can_NoChurchSelected_OffersNothing', () => {
    expect(can(null, 'manageGroups')).toBe(false)
    expect(can(undefined, 'manageChurch')).toBe(false)
  })
})
