import createClient from 'openapi-fetch'
import type { components, paths } from '@/api/schema'

/** The API asks the caller to verify their email before it signs them in. */
export const EMAIL_NOT_CONFIRMED = 'tag:sheaf,2026:problems/email-not-confirmed'

/** A failed API call, with the HTTP status and the Problem Details type when the server sent one. */
export class ApiError extends Error {
  readonly status: number
  readonly problemType: string | undefined

  constructor(message: string, status: number, problemType: string | undefined) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.problemType = problemType
  }
}

export const apiClient = createClient<paths>({
  baseUrl: __API_BASE_URL__ || '',
  credentials: 'include',
  fetch: request => fetch(request),
})

apiClient.use({
  async onResponse({ request, response }) {
    if (!response.ok) {
      const body = request.method === 'POST' ? await response.clone().text() : ''

      let message = body || `HTTP error! status: ${response.status}`
      let problemType: string | undefined

      if (response.headers.get('content-type')?.includes('application/problem+json') && body) {
        try {
          // SAFETY: This response uses the generated Problem Details contract; malformed JSON falls through to the catch.
          const problem = JSON.parse(body) as components['schemas']['HttpValidationProblemDetails']
          const validationMessages = Object.values(problem.errors ?? {}).flat()

          problemType = problem.type ?? undefined
          message = validationMessages.join('\n') || String(problem.detail || problem.title || message)
        } catch {
          // Keep the original response body when it is not valid Problem Details JSON.
        }
      }

      throw new ApiError(message, response.status, problemType)
    }
  },
})
