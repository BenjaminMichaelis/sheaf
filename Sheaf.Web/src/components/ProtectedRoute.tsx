import { Navigate } from 'react-router-dom'
import { useAuth } from '@/contexts/AuthContext'
import { useChurch } from '@/contexts/ChurchContext'
import { CircularProgress, Box } from '@mui/material'

interface ProtectedRouteProps {
  children: React.ReactNode
  /** Church pages also need a selected church; without one the user is sent home, which explains what to do. */
  requireChurch?: boolean
}

export default function ProtectedRoute({ children, requireChurch = false }: ProtectedRouteProps) {
  const { user, loading } = useAuth()
  const { church } = useChurch()

  if (loading) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', minHeight: '50vh' }}>
        <CircularProgress aria-label="Loading" />
      </Box>
    )
  }

  if (!user?.isAuthenticated) {
    return <Navigate to="/login" replace />
  }

  if (requireChurch && church === null) {
    return <Navigate to="/" replace />
  }

  return <>{children}</>
}
