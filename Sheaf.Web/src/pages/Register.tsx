import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Box, TextField, Button, Typography, Paper, Link } from '@mui/material'
import { useSnackbar } from 'notistack'
import { useAuth } from '@/contexts/AuthContext'
import { passwordMismatch } from '@/services/passwords'
import { NameFields, NewPasswordFields } from '@/components/AccountFields'

export default function Register() {
  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [loading, setLoading] = useState(false)
  const navigate = useNavigate()
  const { register } = useAuth()
  const { enqueueSnackbar } = useSnackbar()

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()

    const mismatch = passwordMismatch(password, confirmPassword)

    if (mismatch) {
      enqueueSnackbar(mismatch, { variant: 'error' })

      return
    }

    setLoading(true)

    try {
      await register({ email, password, confirmPassword, firstName, lastName })
      navigate('/verify-email')
    } catch (error) {
      enqueueSnackbar(error instanceof Error ? error.message : 'Registration failed', { variant: 'error' })
    } finally {
      setLoading(false)
    }
  }

  return (
    <Box sx={{ maxWidth: 400, mx: 'auto', mt: 4 }}>
      <Paper elevation={3} sx={{ p: 4 }}>
        <Typography variant="h4" component="h1" gutterBottom>
          Register
        </Typography>
        <Box component="form" onSubmit={handleSubmit} sx={{ mt: 3 }}>
          <NameFields
            firstName={firstName}
            lastName={lastName}
            onFirstNameChange={setFirstName}
            onLastNameChange={setLastName}
            autoFocus
          />
          <TextField
            fullWidth
            label="Email"
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            margin="normal"
            required
            autoComplete="email"
            data-testid="email-input"
          />
          <NewPasswordFields
            label="Password"
            password={password}
            confirmPassword={confirmPassword}
            onPasswordChange={setPassword}
            onConfirmPasswordChange={setConfirmPassword}
          />
          <Button
            type="submit"
            variant="contained"
            fullWidth
            size="large"
            sx={{ mt: 3 }}
            disabled={loading}
            data-testid="register-button"
          >
            {loading ? 'Registering...' : 'Register'}
          </Button>
          <Box sx={{ mt: 2, textAlign: 'center' }}>
            <Typography variant="body2">
              Already have an account?{' '}
              <Link href="/login">
                Login
              </Link>
            </Typography>
          </Box>
        </Box>
      </Paper>
    </Box>
  )
}
