import { Routes, Route, Navigate } from 'react-router-dom'
import { SnackbarProvider } from 'notistack'
import { AuthProvider } from './contexts/AuthContext'
import { ChurchProvider } from './contexts/ChurchContext'
import AppThemeProvider from './components/AppThemeProvider'
import Layout from './components/Layout'
import ProtectedRoute from './components/ProtectedRoute'

// Pages
import Groups from './pages/Groups'
import Login from './pages/Login'
import Register from './pages/Register'
import TakeAttendance from './pages/TakeAttendance'

// POC
import AppShell from './poc/AppShell'
import RootRedirect from './poc/RootRedirect'
import LeaderHome from './poc/pages/LeaderHome'
import GroupsList from './poc/groups/GroupsList'
import GroupPage from './poc/groups/GroupPage'
import AttendanceSheet from './poc/groups/AttendanceSheet'
import CapturePage from './poc/capture/CapturePage'
import MyPeoplePage from './poc/people/MyPeoplePage'
import PersonPage from './poc/people/PersonPage'
import ReviewPage from './poc/capture/ReviewPage'
import { AuditLog, PastorInbox } from './poc/capture/StaffPages'
import StaffOverview from './poc/staff/StaffOverview'
import ResponsibleAi from './poc/staff/ResponsibleAi'
import Integrations from './poc/staff/Integrations'

function App() {
  return (
    <AppThemeProvider>
      <SnackbarProvider maxSnack={3} anchorOrigin={{ vertical: 'top', horizontal: 'center' }}>
        <AuthProvider>
          <ChurchProvider>
            <Routes>
              <Route index element={<RootRedirect />} />
              <Route path="login" element={<Login />} />
              <Route path="register" element={<Register />} />

              <Route
                path="app"
                element={
                  <ProtectedRoute>
                    <AppShell area="leader" />
                  </ProtectedRoute>
                }
              >
                <Route index element={<LeaderHome />} />
                <Route path="capture" element={<CapturePage />} />
                <Route path="people" element={<MyPeoplePage />} />
                <Route path="people/:personId" element={<PersonPage />} />
                <Route path="groups" element={<GroupsList />} />
                <Route path="groups/:groupId" element={<GroupPage />} />
                <Route path="groups/:groupId/record" element={<AttendanceSheet key="record" />} />
                <Route path="groups/:groupId/sessions/:sessionId" element={<AttendanceSheet key="edit" />} />
                <Route path="review" element={<ReviewPage />} />
                <Route path="ai" element={<ResponsibleAi />} />
              </Route>

              <Route
                path="staff"
                element={
                  <ProtectedRoute>
                    <AppShell area="staff" />
                  </ProtectedRoute>
                }
              >
                <Route index element={<StaffOverview />} />
                <Route path="ai" element={<ResponsibleAi />} />
                <Route path="inbox" element={<PastorInbox />} />
                <Route path="review" element={<ReviewPage all />} />
                <Route path="audit" element={<AuditLog />} />
                <Route path="integrations" element={<Integrations />} />
              </Route>

              {/* The product screens from main, kept reachable beside the POC. */}
              <Route element={<Layout />}>
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
              </Route>

              <Route path="*" element={<Navigate to="/" replace />} />
            </Routes>
          </ChurchProvider>
        </AuthProvider>
      </SnackbarProvider>
    </AppThemeProvider>
  )
}

export default App
