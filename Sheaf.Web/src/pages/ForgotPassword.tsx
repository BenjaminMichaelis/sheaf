import { useState } from 'react'
import { useNavigate, Link } from 'react-router-dom'
import { Box, TextField, Button, Typography, Paper } from '@mui/material'
import { useSnackbar } from 'notistack'
import { apiClient } from '@/services/apiClient'
import { passwordMismatch } from '@/services/passwords'
import { CodeField, NewPasswordFields } from '@/components/AccountFields'

/** Asks for a reset code, then sets a new password with it. Resetting also verifies the email address. */
export default function ForgotPassword() {
  const [email, setEmail] = useState('')
  const [codeSent, setCodeSent] = useState(false)
  const [code, setCode] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [loading, setLoading] = useState(false)
  const navigate = useNavigate()
  const { enqueueSnackbar } = useSnackbar()

  const handleRequest = async (e: React.FormEvent) => {
    e.preventDefault()
    setLoading(true)

    try {
      await apiClient.POST('/api/auth/forgot-password', { body: { email } })
      setCodeSent(true)
    } catch (error) {
      enqueueSnackbar(error instanceof Error ? error.message : 'Could not send a code', { variant: 'error' })
    } finally {
      setLoading(false)
    }
  }

  const handleReset = async (e: React.FormEvent) => {
    e.preventDefault()

    const mismatch = passwordMismatch(newPassword, confirmPassword)

    if (mismatch) {
      enqueueSnackbar(mismatch, { variant: 'error' })

      return
    }

    setLoading(true)

    try {
      await apiClient.POST('/api/auth/reset-password', { body: { email, code, newPassword } })
      enqueueSnackbar('Password changed. Log in with your new password.', { variant: 'success' })
      navigate('/login')
    } catch (error) {
      enqueueSnackbar(error instanceof Error ? error.message : 'Could not reset the password', { variant: 'error' })
    } finally {
      setLoading(false)
    }
  }

  return (
    <Box sx={{ maxWidth: 400, mx: 'auto', mt: 4 }}>
      <Paper elevation={3} sx={{ p: 4 }}>
        <Typography variant="h4" component="h1" gutterBottom>
          Reset password
        </Typography>
        {codeSent ? (
          <Box component="form" onSubmit={handleReset} sx={{ mt: 2 }}>
            <Typography variant="body2" color="text.secondary">
              If {email} has an account, we sent it a six-digit code. It expires in about 10 minutes.
            </Typography>
            <CodeField value={code} onChange={setCode} autoFocus />
            <NewPasswordFields
              password={newPassword}
              confirmPassword={confirmPassword}
              onPasswordChange={setNewPassword}
              onConfirmPasswordChange={setConfirmPassword}
            />
            <Button
              type="submit"
              variant="contained"
              fullWidth
              size="large"
              sx={{ mt: 3 }}
              disabled={loading}
              data-testid="reset-button"
            >
              {loading ? 'Saving...' : 'Set new password'}
            </Button>
            <Button
              fullWidth
              sx={{ mt: 1 }}
              onClick={() => {
                setCode('')
                setNewPassword('')
                setConfirmPassword('')
                setCodeSent(false)
              }}
            >
              Use a different email
            </Button>
          </Box>
        ) : (
          <Box component="form" onSubmit={handleRequest} sx={{ mt: 2 }}>
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
            <Button
              type="submit"
              variant="contained"
              fullWidth
              size="large"
              sx={{ mt: 3 }}
              disabled={loading}
              data-testid="send-code-button"
            >
              {loading ? 'Sending...' : 'Send code'}
            </Button>
          </Box>
        )}
        <Box sx={{ mt: 2, textAlign: 'center' }}>
          <Typography variant="body2">
            <Link to="/login">
              Back to login
            </Link>
          </Typography>
        </Box>
      </Paper>
    </Box>
  )
}
