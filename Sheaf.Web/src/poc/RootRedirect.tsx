import { Navigate } from 'react-router-dom'
import { Box, CircularProgress } from '@mui/material'
import { useAuth } from '@/contexts/AuthContext'
import { isStaff } from './roles'

/** Sends people to the area that fits their role. */
export default function RootRedirect() {
  const { user, loading } = useAuth()

  if (loading) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', mt: 8 }}>
        <CircularProgress aria-label="Loading" />
      </Box>
    )
  }

  if (!user?.isAuthenticated) return <Navigate to="/login" replace />

  return <Navigate to={isStaff(user) ? '/staff' : '/app'} replace />
}
