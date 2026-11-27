import type { components } from '@/api/schema'

export type UserInfo = components['schemas']['UserInfo']

export type LoginRequest = components['schemas']['LoginRequest']

export type RegisterRequest = components['schemas']['RegisterRequest']

export type ConfirmEmailRequest = components['schemas']['ConfirmEmailRequest']

export type ConfirmEmailChangeRequest = components['schemas']['ConfirmEmailChangeRequest']

export type UpdateProfileRequest = components['schemas']['UpdateProfileRequest']

/**
 * An account waiting for its email code: the address and the password just typed, which confirming sets.
 * Kept only in memory, never in router state or storage.
 */
export interface PendingVerification {
  email: string
  password: string
}
