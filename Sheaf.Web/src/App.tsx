import { Routes, Route, Navigate } from 'react-router-dom'
import { SnackbarProvider } from 'notistack'
import { AuthProvider } from './contexts/AuthContext'
import { ChurchProvider } from './contexts/ChurchContext'
import AppThemeProvider from './components/AppThemeProvider'
import Layout from './components/Layout'
import ProtectedRoute from './components/ProtectedRoute'

// Pages
import Groups from './pages/Groups'
import Home from './pages/Home'
import Login from './pages/Login'
import Register from './pages/Register'
import GroupMeetings from './pages/GroupMeetings'
import Meeting from './pages/Meeting'
import Person from './pages/Person'
import TakeAttendance from './pages/TakeAttendance'

function App() {
  return (
    <AppThemeProvider>
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
                  path="groups/:groupId"
                  element={
                    <ProtectedRoute requireChurch>
                      <GroupMeetings />
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
                <Route
                  path="groups/:groupId/meetings/:sessionId"
                  element={
                    <ProtectedRoute requireChurch>
                      <Meeting />
                    </ProtectedRoute>
                  }
                />
                <Route
                  path="people/:personId"
                  element={
                    <ProtectedRoute requireChurch>
                      <Person />
                    </ProtectedRoute>
                  }
                />
                <Route path="*" element={<Navigate to="/" replace />} />
              </Route>
            </Routes>
          </ChurchProvider>
        </AuthProvider>
      </SnackbarProvider>
    </AppThemeProvider>
  )
}

export default App
