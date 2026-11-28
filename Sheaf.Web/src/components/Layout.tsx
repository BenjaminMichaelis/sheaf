import { useState, type ReactNode } from 'react'
import { NavLink, Outlet, useMatch, useNavigate } from 'react-router-dom'
import {
  AppBar,
  Box,
  Button,
  Container,
  Divider,
  Drawer,
  IconButton,
  Link,
  List,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  Toolbar,
  Typography,
  useMediaQuery,
  useTheme as useMuiTheme,
} from '@mui/material'
import { Groups as GroupsIcon, Home as HomeIcon, Menu as MenuIcon } from '@mui/icons-material'
import { useAuth } from '@/contexts/AuthContext'
import { useChurch } from '@/contexts/ChurchContext'
import ChurchConflictDialog from './ChurchConflictDialog'
import ChurchSwitcher from './ChurchSwitcher'
import ColorModeMenu from './ColorModeMenu'

const DRAWER_WIDTH = 240

interface NavItem {
  to: string
  label: string
  icon: ReactNode
}

export default function Layout() {
  const navigate = useNavigate()
  const { user, logout } = useAuth()
  const { church, labels } = useChurch()
  // Under createRoot the first render already reads the real width (mui.com/material-ui/react-use-media-query).
  const isDesktop = useMediaQuery(useMuiTheme().breakpoints.up('md'))
  const [menuOpen, setMenuOpen] = useState(false)

  // Navigation only makes sense once the user is acting in a church.
  const showNav = church !== null

  const navItems: NavItem[] = [
    { to: '/', label: 'Home', icon: <HomeIcon /> },
    { to: '/groups', label: labels.many, icon: <GroupsIcon /> },
  ]

  const handleLogout = async () => {
    await logout()
    navigate('/login')
  }

  const nav = (
    <Box component="nav" aria-label="Main">
      {/* On desktop this leaves room for the app bar above the drawer; on phones it titles the open drawer. */}
      <Toolbar>{isDesktop ? null : <Typography variant="h6">Sheaf</Typography>}</Toolbar>
      <Divider />
      <List>
        {navItems.map(item => (
          <NavItemButton key={item.to} item={item} onClick={() => setMenuOpen(false)} />
        ))}
      </List>
    </Box>
  )

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', minHeight: '100vh' }}>
      {/* Above the permanent drawer on desktop; on phones the modal drawer covers it, as MUI expects. */}
      <AppBar position="fixed" sx={isDesktop ? { zIndex: theme => theme.zIndex.drawer + 1 } : undefined}>
        <Toolbar sx={{ gap: 1 }}>
          {showNav && !isDesktop && (
            <IconButton color="inherit" edge="start" onClick={() => setMenuOpen(true)} aria-label="Open navigation">
              <MenuIcon />
            </IconButton>
          )}
          {/* On phones the church name takes this space once the user is acting in a church. */}
          <Link
            href="/"
            variant="h6"
            color="inherit"
            underline="none"
            sx={{ flexShrink: 0, display: { xs: showNav ? 'none' : 'block', sm: 'block' } }}
          >
            Sheaf
          </Link>
          <Box sx={{ flexGrow: 1, minWidth: 0, display: 'flex', alignItems: 'center', overflow: 'hidden' }}>
            <ChurchSwitcher />
          </Box>

          <ColorModeMenu />

          {user?.isAuthenticated ? (
            <Button color="inherit" onClick={handleLogout}>
              Logout
            </Button>
          ) : (
            <>
              <Button color="inherit" href="/login">
                Login
              </Button>
              <Button color="inherit" href="/register">
                Register
              </Button>
            </>
          )}
        </Toolbar>
      </AppBar>

      <Box sx={{ display: 'flex', flex: 1 }}>
        {showNav &&
          (isDesktop ? (
            <Drawer
              variant="permanent"
              sx={{ width: DRAWER_WIDTH, flexShrink: 0, '& .MuiDrawer-paper': { width: DRAWER_WIDTH } }}
            >
              {nav}
            </Drawer>
          ) : (
            <Drawer
              variant="temporary"
              open={menuOpen}
              onClose={() => setMenuOpen(false)}
              ModalProps={{ keepMounted: true }}
              sx={{ '& .MuiDrawer-paper': { width: DRAWER_WIDTH } }}
            >
              {nav}
            </Drawer>
          ))}

        <Box sx={{ display: 'flex', flexDirection: 'column', flex: 1, minWidth: 0 }}>
          <Toolbar />
          <Container component="main" sx={{ flex: 1, py: 3 }}>
            <Outlet />
          </Container>

          <Box component="footer" sx={{ py: 2, px: 2, mt: 'auto', backgroundColor: 'background.paper' }}>
            <Typography variant="body2" color="text.secondary" align="center">
              © {new Date().getFullYear()} Sheaf
            </Typography>
          </Box>
        </Box>
      </Box>

      <ChurchConflictDialog />
    </Box>
  )
}

/**
 * A nav link that MUI shows as selected while its page is open. NavLink marks it aria-current="page" for screen
 * readers; `end` keeps Home from matching every page, while a section stays selected on the pages under it.
 */
function NavItemButton({ item, onClick }: { item: NavItem; onClick: () => void }) {
  const end = item.to === '/'
  const selected = useMatch({ path: item.to, end }) !== null

  return (
    <ListItemButton component={NavLink} to={item.to} end={end} selected={selected} onClick={onClick}>
      <ListItemIcon>{item.icon}</ListItemIcon>
      <ListItemText primary={item.label} />
    </ListItemButton>
  )
}
