import { Routes, Route, Navigate } from 'react-router-dom'
import { CssBaseline } from '@mui/material'
import { SnackbarProvider } from 'notistack'
import { AuthProvider } from './contexts/AuthContext'
import { ChurchProvider } from './contexts/ChurchContext'
import { ThemeProvider } from './contexts/ThemeContext'
import Layout from './components/Layout'
import ProtectedRoute from './components/ProtectedRoute'

// Pages
import Groups from './pages/Groups'
import Home from './pages/Home'
import Login from './pages/Login'
import Register from './pages/Register'
import TakeAttendance from './pages/TakeAttendance'

function App() {
  return (
    <ThemeProvider>
      <CssBaseline />
      <SnackbarProvider maxSnack={3}>
        <AuthProvider>
          <ChurchProvider>
            <Routes>
              <Route path="/" element={<Layout />}>
                <Route index element={<Home />} />
                <Route path="login" element={<Login />} />
                <Route path="register" element={<Register />} />
                <Route
                  path="groups"
                  element={
                    <ProtectedRoute requireChurch>
                      <Groups />
                    </ProtectedRoute>
                  }
                />
                <Route
                  path="groups/:groupId/attendance"
                  element={
                    <ProtectedRoute requireChurch>
                      <TakeAttendance />
                    </ProtectedRoute>
                  }
                />
                <Route path="*" element={<Navigate to="/" replace />} />
              </Route>
            </Routes>
          </ChurchProvider>
        </AuthProvider>
      </SnackbarProvider>
    </ThemeProvider>
  )
}

export default App
