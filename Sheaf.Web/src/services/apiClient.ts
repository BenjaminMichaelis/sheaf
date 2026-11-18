import createClient from 'openapi-fetch'
import type { components, paths } from '@/api/schema'

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

      if (response.headers.get('content-type')?.includes('application/problem+json') && body) {
        try {
          // SAFETY: This response uses the generated Problem Details contract; malformed JSON falls through to the catch.
          const problem = JSON.parse(body) as components['schemas']['HttpValidationProblemDetails']
          const validationMessages = Object.values(problem.errors ?? {}).flat()

          message = validationMessages.join('\n') || String(problem.detail || problem.title || message)
        } catch {
          // Keep the original response body when it is not valid Problem Details JSON.
        }
      }

      throw new Error(message)
    }
  },
})
