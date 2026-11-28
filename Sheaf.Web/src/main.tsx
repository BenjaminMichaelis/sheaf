import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import { QueryClientProvider } from '@tanstack/react-query'
import { ReactQueryDevtools } from '@tanstack/react-query-devtools'
import App from './App.tsx'
import { createQueryClient } from './services/queryClient'
import { initTelemetry } from './services/telemetry'

initTelemetry()

const queryClient = createQueryClient()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <App />
      </BrowserRouter>
      {/* Left out of production builds by the package itself. */}
      <ReactQueryDevtools />
    </QueryClientProvider>
  </StrictMode>,
)
