import { useState } from 'react'
import { useNavigate, Navigate } from 'react-router-dom'
import { Box, TextField, Button, Typography, Paper, Link } from '@mui/material'
import { useSnackbar } from 'notistack'
import { useAuth } from '@/contexts/AuthContext'
import { apiClient } from '@/services/apiClient'
import { passwordMismatch } from '@/services/passwords'
import { CodeField, NewPasswordFields } from '@/components/AccountFields'

/**
 * Finishes sign-up, or a sign-in the server held back, with the six-digit code from the email. After a reload, or
 * when the held address or password is wrong, the page asks for them again.
 */
export default function VerifyEmail() {
  const { user, pendingVerification, confirmEmail, clearPendingVerification, renewPendingVerification } = useAuth()
  const [email, setEmail] = useState(pendingVerification?.email ?? '')
  const [password, setPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [code, setCode] = useState('')
  const [loading, setLoading] = useState(false)
  const [resending, setResending] = useState(false)
  const navigate = useNavigate()
  const { enqueueSnackbar } = useSnackbar()
  const knowsCredentials = pendingVerification !== null

  if (user?.isAuthenticated) {
    return <Navigate to="/" replace />
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()

    const mismatch = knowsCredentials ? null : passwordMismatch(password, confirmPassword)

    if (mismatch) {
      enqueueSnackbar(mismatch, { variant: 'error' })

      return
    }

    setLoading(true)

    try {
      await confirmEmail({
        email: pendingVerification?.email ?? email,
        password: pendingVerification?.password ?? password,
        code,
      })
      enqueueSnackbar('Email verified', { variant: 'success' })
      navigate('/', { replace: true })
    } catch (error) {
      enqueueSnackbar(error instanceof Error ? error.message : 'Verification failed', { variant: 'error' })
    } finally {
      setLoading(false)
    }
  }

  const handleResend = async () => {
    const address = pendingVerification?.email ?? email

    if (!address) {
      enqueueSnackbar('Enter your email address first', { variant: 'error' })

      return
    }

    setResending(true)

    try {
      await apiClient.POST('/api/auth/resend-confirmation', { body: { email: address } })
      renewPendingVerification()
      enqueueSnackbar('If that address needs verifying, a new code is on its way. Codes can be resent once a minute.', {
        variant: 'info',
      })
    } catch (error) {
      enqueueSnackbar(error instanceof Error ? error.message : 'Could not send a new code', { variant: 'error' })
    } finally {
      setResending(false)
    }
  }

  const handleUseDifferentAccount = () => {
    setEmail(pendingVerification?.email ?? '')
    setPassword('')
    setConfirmPassword('')
    clearPendingVerification()
  }

  return (
    <Box sx={{ maxWidth: 400, mx: 'auto', mt: 4 }}>
      <Paper elevation={3} sx={{ p: 4 }}>
        <Typography variant="h4" component="h1" gutterBottom>
          Verify your email
        </Typography>
        <Typography variant="body2" color="text.secondary">
          {knowsCredentials
            ? `Check ${pendingVerification.email} for a six-digit code. It expires in about 10 minutes.`
            : 'Enter your email, the six-digit code we emailed you, and the password you want to sign in with.'}
        </Typography>
        <Box component="form" onSubmit={handleSubmit} sx={{ mt: 2 }}>
          {!knowsCredentials && (
            <>
              <TextField
                fullWidth
                label="Email"
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                margin="normal"
                required
                autoFocus
                autoComplete="email"
                data-testid="email-input"
              />
              <NewPasswordFields
                password={password}
                confirmPassword={confirmPassword}
                onPasswordChange={setPassword}
                onConfirmPasswordChange={setConfirmPassword}
              />
            </>
          )}
          <CodeField value={code} onChange={setCode} autoFocus={knowsCredentials} />
          <Button
            type="submit"
            variant="contained"
            fullWidth
            size="large"
            sx={{ mt: 3 }}
            disabled={loading}
            data-testid="verify-button"
          >
            {loading ? 'Verifying...' : 'Verify'}
          </Button>
          <Button fullWidth sx={{ mt: 1 }} onClick={handleResend} disabled={resending} data-testid="resend-button">
            Send a new code
          </Button>
          {knowsCredentials && (
            <Button fullWidth sx={{ mt: 1 }} onClick={handleUseDifferentAccount} data-testid="different-account-button">
              Use a different email or password
            </Button>
          )}
          <Box sx={{ mt: 2, textAlign: 'center' }}>
            <Typography variant="body2">
              <Link href="/login" onClick={clearPendingVerification}>
                Back to login
              </Link>
            </Typography>
          </Box>
        </Box>
      </Paper>
    </Box>
  )
}
