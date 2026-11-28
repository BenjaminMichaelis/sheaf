import createClient from 'openapi-fetch'
import type { components, paths } from '@/api/schema'

/** Problem type the API returns when a church request names a different church than the signed-in selection. */
export const CHURCH_CONFLICT_PROBLEM_TYPE = 'tag:sheaf,2026:problems/church-header-conflict'

const CHURCH_HEADER = 'X-Sheaf-Church'

/** Thrown when the page is acting for a church other than the one the account now has selected (for example, after switching in another tab). */
export class ChurchConflictError extends Error {
  constructor(message: string) {
    super(message)
    this.name = 'ChurchConflictError'
  }
}

let activeChurchId: string | null = null
const conflictListeners = new Set<() => void>()

/** Sets the church this page acts for. Church requests carry it so the API can refuse writes meant for another church. */
export function setActiveChurch(churchId: string | null) {
  activeChurchId = churchId
}

/** Calls `listener` whenever the API reports a church conflict. Returns a function that stops listening. */
export function onChurchConflict(listener: () => void): () => void {
  conflictListeners.add(listener)

  return () => conflictListeners.delete(listener)
}

export const apiClient = createClient<paths>({
  baseUrl: __API_BASE_URL__ || '',
  credentials: 'include',
  fetch: request => fetch(request),
})

apiClient.use({
  onRequest({ request }) {
    if (activeChurchId && new URL(request.url, window.location.origin).pathname.startsWith('/api/church/')) {
      request.headers.set(CHURCH_HEADER, activeChurchId)
    }

    return request
  },
  async onResponse({ request, response }) {
    if (!response.ok) {
      const body = request.method === 'GET' ? '' : await response.clone().text()

      let message = body || `HTTP error! status: ${response.status}`

      if (response.headers.get('content-type')?.includes('application/problem+json') && body) {
        try {
          // SAFETY: This response uses the generated Problem Details contract; malformed JSON falls through to the catch.
          const problem = JSON.parse(body) as components['schemas']['HttpValidationProblemDetails']
          const validationMessages = Object.values(problem.errors ?? {}).flat()

          message = validationMessages.join('\n') || String(problem.detail || problem.title || message)

          if (problem.type === CHURCH_CONFLICT_PROBLEM_TYPE) {
            conflictListeners.forEach(listener => listener())
            throw new ChurchConflictError(message)
          }
        } catch (error) {
          if (error instanceof ChurchConflictError) throw error
          // Keep the original response body when it is not valid Problem Details JSON.
        }
      }

      throw new Error(message)
    }
  },
})
