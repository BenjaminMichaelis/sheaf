import type { UserInfo } from '@/types'

export function currentRole(user: UserInfo | null) {
  return user?.currentChurch?.role
}

/** Staff and Admin see the church-wide staff area. */
export function isStaff(user: UserInfo | null) {
  const role = currentRole(user)

  return role === 'Staff' || role === 'Admin'
}

export function displayName(user: UserInfo | null) {
  const email = user?.email ?? user?.userName ?? ''
  const local = email.split('@')[0] ?? ''

  return local ? local.charAt(0).toUpperCase() + local.slice(1) : 'Signed in'
}
