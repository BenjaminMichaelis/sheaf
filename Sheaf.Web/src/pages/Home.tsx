import { useState } from 'react'
import { Link as RouterLink } from 'react-router-dom'
import { Box, Button, List, ListItemButton, ListItemText, Paper, Typography } from '@mui/material'
import { useSnackbar } from 'notistack'
import { useAuth } from '@/contexts/AuthContext'
import { useChurch } from '@/contexts/ChurchContext'

export default function Home() {
  const { user } = useAuth()
  const { church, churches, labels } = useChurch()

  if (user?.isAuthenticated && church === null) {
    return churches.length > 0 ? <ChooseChurch /> : <NoChurch />
  }

  if (church !== null) {
    return (
      <Box sx={{ maxWidth: 600, mx: 'auto', mt: 4 }}>
        <Typography variant="h4" component="h1" gutterBottom>
          {church.churchName}
        </Typography>
        <Typography color="text.secondary" gutterBottom>
          Signed in as {church.role}.
        </Typography>
        <Button component={RouterLink} to="/groups" variant="contained" sx={{ mt: 2 }}>
          View {labels.many}
        </Button>
      </Box>
    )
  }

  return (
    <Box sx={{ maxWidth: 600, mx: 'auto', mt: 4 }}>
      <Typography variant="h4" component="h1" gutterBottom>
        Sheaf
      </Typography>
      <Typography color="text.secondary">Leader-first relational capture for churches.</Typography>
    </Box>
  )
}

function NoChurch() {
  return (
    <Box sx={{ maxWidth: 600, mx: 'auto', mt: 4 }}>
      <Typography variant="h4" component="h1" gutterBottom>
        You're not part of a church yet
      </Typography>
      <Typography color="text.secondary">
        Ask an administrator at your church to give your account access, then sign in again.
      </Typography>
    </Box>
  )
}

/** The account belongs to churches but none is selected, for example after losing access to the one it had. */
function ChooseChurch() {
  const { churches, switchChurch } = useChurch()
  const { enqueueSnackbar } = useSnackbar()
  const [switching, setSwitching] = useState(false)

  const choose = async (churchId: string) => {
    setSwitching(true)

    try {
      await switchChurch(churchId)
    } catch (error) {
      enqueueSnackbar(error instanceof Error ? error.message : 'Could not select church.', { variant: 'error' })
    } finally {
      setSwitching(false)
    }
  }

  return (
    <Box sx={{ maxWidth: 600, mx: 'auto', mt: 4 }}>
      <Typography id="choose-church-heading" variant="h4" component="h1" gutterBottom>
        Choose a church
      </Typography>
      <Paper variant="outlined">
        <List disablePadding aria-labelledby="choose-church-heading">
          {churches.map(option => (
            <ListItemButton key={option.churchId} disabled={switching} onClick={() => void choose(option.churchId)}>
              <ListItemText primary={option.churchName} secondary={option.role} />
            </ListItemButton>
          ))}
        </List>
      </Paper>
    </Box>
  )
}
