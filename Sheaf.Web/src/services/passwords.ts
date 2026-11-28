/** The message to show when the two passwords differ, or null when they match. */
export function passwordMismatch(password: string, confirmPassword: string): string | null {
  return password === confirmPassword ? null : 'Passwords do not match'
}
