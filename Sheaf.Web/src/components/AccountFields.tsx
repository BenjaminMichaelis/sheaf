import { TextField } from '@mui/material'

interface CodeFieldProps {
  value: string
  onChange: (value: string) => void
  autoFocus?: boolean
}

/** The six-digit code from an account email. Allows a little slack for pasted spaces, which the server ignores. */
export function CodeField({ value, onChange, autoFocus = false }: CodeFieldProps) {
  return (
    <TextField
      fullWidth
      label="Code"
      value={value}
      onChange={(e) => onChange(e.target.value)}
      margin="normal"
      required
      autoFocus={autoFocus}
      autoComplete="one-time-code"
      slotProps={{ htmlInput: { inputMode: 'numeric', maxLength: 12 } }}
      data-testid="code-input"
    />
  )
}

interface NameFieldsProps {
  firstName: string
  lastName: string
  onFirstNameChange: (value: string) => void
  onLastNameChange: (value: string) => void
  autoFocus?: boolean
}

/** First and last name, up to the server's 100 characters each. */
export function NameFields({ firstName, lastName, onFirstNameChange, onLastNameChange, autoFocus = false }: NameFieldsProps) {
  return (
    <>
      <TextField
        fullWidth
        label="First name"
        value={firstName}
        onChange={(e) => onFirstNameChange(e.target.value)}
        margin="normal"
        required
        autoFocus={autoFocus}
        autoComplete="given-name"
        slotProps={{ htmlInput: { maxLength: 100 } }}
        data-testid="first-name-input"
      />
      <TextField
        fullWidth
        label="Last name"
        value={lastName}
        onChange={(e) => onLastNameChange(e.target.value)}
        margin="normal"
        required
        autoComplete="family-name"
        slotProps={{ htmlInput: { maxLength: 100 } }}
        data-testid="last-name-input"
      />
    </>
  )
}

interface NewPasswordFieldsProps {
  password: string
  confirmPassword: string
  onPasswordChange: (value: string) => void
  onConfirmPasswordChange: (value: string) => void
  label?: string
}

/** A new password typed twice. Check it with `passwordMismatch` from `@/services/passwords` before submitting. */
export function NewPasswordFields({
  password,
  confirmPassword,
  onPasswordChange,
  onConfirmPasswordChange,
  label = 'New password',
}: NewPasswordFieldsProps) {
  return (
    <>
      <TextField
        fullWidth
        label={label}
        type="password"
        value={password}
        onChange={(e) => onPasswordChange(e.target.value)}
        margin="normal"
        required
        autoComplete="new-password"
        data-testid="password-input"
      />
      <TextField
        fullWidth
        label={`Confirm ${label.toLowerCase()}`}
        type="password"
        value={confirmPassword}
        onChange={(e) => onConfirmPasswordChange(e.target.value)}
        margin="normal"
        required
        autoComplete="new-password"
        data-testid="confirm-password-input"
      />
    </>
  )
}
