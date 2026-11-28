import { useState } from 'react'
import { Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useColorScheme } from '@mui/material/styles'
import {
  AppBar,
  Avatar,
  BottomNavigation,
  BottomNavigationAction,
  Box,
  Chip,
  Divider,
  IconButton,
  ListItemIcon,
  Menu,
  MenuItem,
  Paper,
  Toolbar,
  Typography,
} from '@mui/material'
import {
  AddCircleOutlined,
  AutoAwesome,
  Brightness4,
  Brightness7,
  Dashboard,
  FactCheck,
  Groups,
  Home,
  Inbox,
  Logout,
  People,
  RestartAlt,
  SwapHoriz,
  SyncAlt,
} from '@mui/icons-material'
import { useSnackbar } from 'notistack'
import { useAuth } from '@/contexts/AuthContext'
import { apiClient } from '@/services/apiClient'
import { currentRole, displayName, isStaff } from './roles'
import { usePocStatus } from './usePocStatus'

const leaderNav = [
  { label: 'Home', path: '/app', icon: <Home /> },
  { label: 'Add', path: '/app/capture', icon: <AddCircleOutlined /> },
  { label: 'People', path: '/app/people', icon: <People /> },
  { label: 'Groups', path: '/app/groups', icon: <Groups /> },
  { label: 'Review', path: '/app/review', icon: <FactCheck /> },
]

const staffNav = [
  { label: 'Overview', path: '/staff', icon: <Dashboard /> },
  { label: 'Inbox', path: '/staff/inbox', icon: <Inbox /> },
  { label: 'Review', path: '/staff/review', icon: <FactCheck /> },
  { label: 'Audit', path: '/staff/audit', icon: <AutoAwesome /> },
]

/** Phone-first frame: a slim top bar and bottom navigation, centered on wide screens. */
export default function AppShell({ area }: { area: 'leader' | 'staff' }) {
  const navigate = useNavigate()
  const location = useLocation()
  const { user, logout } = useAuth()
  const { mode, systemMode, setMode } = useColorScheme()
  const dark = (mode === 'system' ? systemMode : mode) === 'dark'
  const { enqueueSnackbar } = useSnackbar()
  const status = usePocStatus()
  const [menuAnchor, setMenuAnchor] = useState<HTMLElement | null>(null)

  const nav = area === 'staff' ? staffNav : leaderNav

  const active = [...nav].sort((a, b) => b.path.length - a.path.length).find(item => location.pathname.startsWith(item.path))

  const handleLogout = async () => {
    setMenuAnchor(null)
    await logout()
    navigate('/login')
  }

  const handleReset = async () => {
    setMenuAnchor(null)

    try {
      await apiClient.POST('/api/poc/demo/reset')
      await logout().catch(() => undefined)
      enqueueSnackbar('Demo data reset. Sign in again.', { variant: 'success' })
      navigate('/login')
    } catch (error) {
      enqueueSnackbar(error instanceof Error ? error.message : 'Reset failed', { variant: 'error' })
    }
  }

  return (
    <Box sx={{ minHeight: '100dvh', bgcolor: 'background.default', display: 'flex', justifyContent: 'center' }}>
      <Box sx={{ width: '100%', maxWidth: 560, display: 'flex', flexDirection: 'column', minHeight: '100dvh', position: 'relative' }}>
        <AppBar position="sticky" color="inherit" elevation={0} sx={{ borderBottom: 1, borderColor: 'divider' }}>
          <Toolbar sx={{ gap: 1 }}>
            <Box sx={{ flexGrow: 1, minWidth: 0 }}>
              <Typography variant="subtitle1" sx={{ fontWeight: 700, lineHeight: 1.2 }}>
                Sheaf{area === 'staff' ? ' · Staff' : ''}
              </Typography>
              <Typography variant="caption" color="text.secondary" noWrap component="div">
                {user?.currentChurch?.churchName}
              </Typography>
            </Box>
            {status && (
              <Chip
                size="small"
                icon={<AutoAwesome />}
                label={status.aiLive ? 'Claude' : 'AI: canned'}
                color={status.aiLive ? 'primary' : 'default'}
                variant="outlined"
              />
            )}
            <IconButton onClick={event => setMenuAnchor(event.currentTarget)} aria-label="Account menu">
              <Avatar sx={{ width: 32, height: 32, fontSize: 14 }}>{displayName(user).charAt(0)}</Avatar>
            </IconButton>
          </Toolbar>
        </AppBar>

        <Menu anchorEl={menuAnchor} open={menuAnchor !== null} onClose={() => setMenuAnchor(null)}>
          <Box sx={{ px: 2, py: 1 }}>
            <Typography variant="subtitle2">{displayName(user)}</Typography>
            <Typography variant="caption" color="text.secondary">
              {currentRole(user)} · {user?.email}
            </Typography>
          </Box>
          <Divider />
          {isStaff(user) && (
            <MenuItem
              onClick={() => {
                setMenuAnchor(null)
                navigate(area === 'staff' ? '/app' : '/staff')
              }}
            >
              <ListItemIcon>
                <SwapHoriz fontSize="small" />
              </ListItemIcon>
              {area === 'staff' ? 'Leader view' : 'Staff view'}
            </MenuItem>
          )}
          <MenuItem
            onClick={() => {
              setMenuAnchor(null)
              setMode(dark ? 'light' : 'dark')
            }}
          >
            <ListItemIcon>{dark ? <Brightness7 fontSize="small" /> : <Brightness4 fontSize="small" />}</ListItemIcon>
            {dark ? 'Light mode' : 'Dark mode'}
          </MenuItem>
          <MenuItem
            onClick={() => {
              setMenuAnchor(null)
              navigate(area === 'staff' ? '/staff/ai' : '/app/ai')
            }}
          >
            <ListItemIcon>
              <AutoAwesome fontSize="small" />
            </ListItemIcon>
            How Sheaf uses AI
          </MenuItem>
          {area === 'staff' && (
            <MenuItem
              onClick={() => {
                setMenuAnchor(null)
                navigate('/staff/integrations')
              }}
            >
              <ListItemIcon>
                <SyncAlt fontSize="small" />
              </ListItemIcon>
              Integrations
            </MenuItem>
          )}
          {status?.demoTools && (
            <MenuItem onClick={handleReset}>
              <ListItemIcon>
                <RestartAlt fontSize="small" />
              </ListItemIcon>
              Reset demo data
            </MenuItem>
          )}
          <MenuItem onClick={handleLogout}>
            <ListItemIcon>
              <Logout fontSize="small" />
            </ListItemIcon>
            Sign out
          </MenuItem>
        </Menu>

        <Box component="main" sx={{ flex: 1, px: 2, pt: 2, pb: 10 }}>
          <Outlet />
        </Box>

        <Paper
          elevation={8}
          sx={{ position: 'fixed', bottom: 0, left: '50%', transform: 'translateX(-50%)', width: '100%', maxWidth: 560 }}
        >
          <BottomNavigation showLabels value={active?.path ?? false} onChange={(_, path: string) => navigate(path)}>
            {nav.map(item => (
              <BottomNavigationAction key={item.path} label={item.label} value={item.path} icon={item.icon} />
            ))}
          </BottomNavigation>
        </Paper>
      </Box>
    </Box>
  )
}
