import { useEffect, useState } from 'react'
import { useNavigate, Link } from 'react-router-dom'
import {
  Avatar,
  Box,
  Button,
  Chip,
  List,
  ListItemAvatar,
  ListItemButton,
  ListItemText,
  Paper,
  TextField,
  Typography,
} from '@mui/material'
import { useSnackbar } from 'notistack'
import { useAuth } from '@/contexts/AuthContext'
import { apiClient } from '@/services/apiClient'
import type { components } from '@/api/schema'

type DemoAccount = components['schemas']['DemoAccountResponse']

export default function Login() {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [loading, setLoading] = useState(false)
  const [demoAccounts, setDemoAccounts] = useState<DemoAccount[]>([])
  const navigate = useNavigate()
  const { login } = useAuth()
  const { enqueueSnackbar } = useSnackbar()

  useEffect(() => {
    let cancelled = false
    // Development only: the API returns 404 elsewhere, which leaves the picker hidden.
    apiClient
      .GET('/api/poc/demo/accounts')
      .then(({ data }) => {
        if (!cancelled && data) setDemoAccounts(data)
      })
      .catch(() => undefined)

    return () => {
      cancelled = true
    }
  }, [])

  const signIn = async (credentials: { email: string; password: string }) => {
    setLoading(true)

    try {
      await login(credentials)
      navigate('/')
    } catch (error) {
      enqueueSnackbar(error instanceof Error ? error.message : 'Login failed', { variant: 'error' })
    } finally {
      setLoading(false)
    }
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    await signIn({ email, password })
  }

  return (
    <Box sx={{ maxWidth: 440, mx: 'auto', mt: 4, px: 2 }}>
      {demoAccounts.length > 0 && (
        <Paper variant="outlined" sx={{ mb: 3 }}>
          <Box sx={{ px: 2, pt: 2 }}>
            <Typography variant="h6">Demo accounts</Typography>
            <Typography variant="body2" color="text.secondary">
              Northgate Church campus ministry. Tap someone to sign in as them.
            </Typography>
          </Box>
          <List>
            {demoAccounts.map(account => (
              <ListItemButton
                key={account.email}
                disabled={loading}
                onClick={() => signIn({ email: account.email, password: account.password })}
              >
                <ListItemAvatar>
                  <Avatar>{account.name.charAt(0)}</Avatar>
                </ListItemAvatar>
                <ListItemText primary={account.name} secondary={account.blurb} />
                <Chip size="small" label={account.role} variant="outlined" />
              </ListItemButton>
            ))}
          </List>
        </Paper>
      )}

      <Paper elevation={demoAccounts.length > 0 ? 0 : 3} variant={demoAccounts.length > 0 ? 'outlined' : 'elevation'} sx={{ p: 3 }}>
        <Typography variant="h5" component="h1" gutterBottom>
          Sign in
        </Typography>
        <Box component="form" onSubmit={handleSubmit}>
          <TextField
            fullWidth
            label="Email"
            type="email"
            value={email}
            onChange={e => setEmail(e.target.value)}
            margin="normal"
            required
            data-testid="email-input"
          />
          <TextField
            fullWidth
            label="Password"
            type="password"
            value={password}
            onChange={e => setPassword(e.target.value)}
            margin="normal"
            required
            data-testid="password-input"
          />
          <Button
            type="submit"
            variant="contained"
            fullWidth
            size="large"
            sx={{ mt: 2 }}
            disabled={loading}
            data-testid="login-button"
          >
            {loading ? 'Signing in…' : 'Sign in'}
          </Button>
          <Box sx={{ mt: 2, textAlign: 'center' }}>
            <Typography variant="body2">
              Don't have an account? <Link to="/register">Register</Link>
            </Typography>
          </Box>
        </Box>
      </Paper>
    </Box>
  )
}
