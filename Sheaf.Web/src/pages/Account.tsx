import { useState } from 'react'
import { Box, TextField, Button, Typography, Paper } from '@mui/material'
import { useSnackbar } from 'notistack'
import { useAuth } from '@/contexts/AuthContext'
import { apiClient } from '@/services/apiClient'
import { CodeField, NameFields } from '@/components/AccountFields'

/**
 * The signed-in account: its name, and its sign-in email. Changing the email needs the current password and a code
 * sent to the new address.
 */
export default function Account() {
  const { user, confirmEmailChange, updateProfile } = useAuth()
  const [firstName, setFirstName] = useState(user?.firstName ?? '')
  const [lastName, setLastName] = useState(user?.lastName ?? '')
  const [savingName, setSavingName] = useState(false)
  const [newEmail, setNewEmail] = useState('')
  const [currentPassword, setCurrentPassword] = useState('')
  const [codeSent, setCodeSent] = useState(false)
  const [code, setCode] = useState('')
  const [loading, setLoading] = useState(false)
  const { enqueueSnackbar } = useSnackbar()

  const handleSaveName = async (e: React.FormEvent) => {
    e.preventDefault()
    setSavingName(true)

    try {
      await updateProfile({ firstName, lastName })
      enqueueSnackbar('Name saved', { variant: 'success' })
    } catch (error) {
      enqueueSnackbar(error instanceof Error ? error.message : 'Could not save the name', { variant: 'error' })
    } finally {
      setSavingName(false)
    }
  }

  const handleRequest = async (e: React.FormEvent) => {
    e.preventDefault()
    setLoading(true)

    try {
      await apiClient.POST('/api/auth/email', { body: { newEmail, currentPassword } })
      setCurrentPassword('')
      setCodeSent(true)
    } catch (error) {
      enqueueSnackbar(error instanceof Error ? error.message : 'Could not send a code', { variant: 'error' })
    } finally {
      setLoading(false)
    }
  }

  const handleConfirm = async (e: React.FormEvent) => {
    e.preventDefault()
    setLoading(true)

    try {
      await confirmEmailChange({ newEmail, code })
      setCodeSent(false)
      setNewEmail('')
      setCode('')
      enqueueSnackbar('Sign-in email changed', { variant: 'success' })
    } catch (error) {
      enqueueSnackbar(error instanceof Error ? error.message : 'Could not change the email', { variant: 'error' })
    } finally {
      setLoading(false)
    }
  }

  return (
    <Box sx={{ maxWidth: 480, mx: 'auto', mt: 4 }}>
      <Paper elevation={3} sx={{ p: 4 }}>
        <Typography variant="h4" component="h1" gutterBottom>
          Account
        </Typography>
        <Typography variant="body2" color="text.secondary" gutterBottom>
          Signs in as {user?.email}
        </Typography>

        <Typography variant="h6" component="h2" sx={{ mt: 3 }}>
          Name
        </Typography>
        <Box component="form" onSubmit={handleSaveName}>
          <NameFields
            firstName={firstName}
            lastName={lastName}
            onFirstNameChange={setFirstName}
            onLastNameChange={setLastName}
          />
          <Button type="submit" variant="contained" fullWidth sx={{ mt: 2 }} disabled={savingName} data-testid="save-name-button">
            {savingName ? 'Saving...' : 'Save name'}
          </Button>
        </Box>

        <Typography variant="h6" component="h2" sx={{ mt: 3 }}>
          Change sign-in email
        </Typography>
        {codeSent ? (
          <Box component="form" onSubmit={handleConfirm}>
            <Typography variant="body2" color="text.secondary">
              We sent a six-digit code to {newEmail}. It expires in about 10 minutes.
            </Typography>
            <CodeField value={code} onChange={setCode} autoFocus />
            <Button type="submit" variant="contained" fullWidth sx={{ mt: 2 }} disabled={loading} data-testid="confirm-email-button">
              {loading ? 'Saving...' : 'Change email'}
            </Button>
            <Button
              fullWidth
              sx={{ mt: 1 }}
              onClick={() => {
                setCode('')
                setCodeSent(false)
              }}
            >
              Cancel
            </Button>
          </Box>
        ) : (
          <Box component="form" onSubmit={handleRequest}>
            <TextField
              fullWidth
              label="New email"
              type="email"
              value={newEmail}
              onChange={(e) => setNewEmail(e.target.value)}
              margin="normal"
              required
              autoComplete="email"
              data-testid="new-email-input"
            />
            <TextField
              fullWidth
              label="Current password"
              type="password"
              value={currentPassword}
              onChange={(e) => setCurrentPassword(e.target.value)}
              margin="normal"
              required
              autoComplete="current-password"
              data-testid="current-password-input"
            />
            <Button type="submit" variant="contained" fullWidth sx={{ mt: 2 }} disabled={loading} data-testid="send-code-button">
              {loading ? 'Sending...' : 'Send code'}
            </Button>
          </Box>
        )}
      </Paper>
    </Box>
  )
}
