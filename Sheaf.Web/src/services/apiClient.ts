import createClient from 'openapi-fetch'
import type { components, paths } from '@/api/schema'

/** The API asks the caller to verify their email before it signs them in. */
export const EMAIL_NOT_CONFIRMED = 'tag:sheaf,2026:problems/email-not-confirmed'

/** Problem type the API returns when a church request names a different church than the signed-in selection. */
export const CHURCH_CONFLICT_PROBLEM_TYPE = 'tag:sheaf,2026:problems/church-header-conflict'

/** Problem type the API returns when a save looks like a duplicate and must be resent with confirm to go ahead. */
export const CONFIRMATION_REQUIRED_PROBLEM_TYPE = 'urn:sheaf:problem:confirmation-required'

const CHURCH_HEADER = 'X-Sheaf-Church'

type ProblemDetails = components['schemas']['HttpValidationProblemDetails']

/** Thrown for every failed API response. `problem` is set when the body was Problem Details, so callers can read its extensions. */
export class ApiError extends Error {
  readonly status: number
  readonly problem: ProblemDetails | null

  constructor(message: string, status: number, problem: ProblemDetails | null) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
  }
}

/** Thrown when the page is acting for a church other than the one the account now has selected (for example, after switching in another tab). */
export class ChurchConflictError extends ApiError {
  constructor(message: string, status: number, problem: ProblemDetails) {
    super(message, status, problem)
    this.name = 'ChurchConflictError'
  }
}

let activeChurchId: string | null = null
const conflictListeners = new Set<() => void>()

/** Sets the church this page acts for. Church requests carry it so the API can refuse writes meant for another church. */
export function setActiveChurch(churchId: string | null) {
  activeChurchId = churchId
}

/** Headers that make a church request act for `churchId` rather than the page's current church. */
export function churchHeaders(churchId: string) {
  return { [CHURCH_HEADER]: churchId }
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
    // A request that already names its church (see churchHeaders) keeps it, so cached data is always asked for by the
    // church it is cached under.
    if (activeChurchId && !request.headers.has(CHURCH_HEADER) && new URL(request.url, window.location.origin).pathname.startsWith('/api/church/')) {
      request.headers.set(CHURCH_HEADER, activeChurchId)
    }

    return request
  },
  async onResponse({ request, response }) {
    if (response.ok) return

    // Every failure's body is read so Problem Details (recovery guidance, church conflicts) reach the user. A plain
    // body is only shown for writes; for reads it is often an HTML error page from a proxy.
    const body = await response.clone().text()
    const problem = parseProblem(response, body)
    const fallback = (request.method !== 'GET' && body) || `HTTP error! status: ${response.status}`
    const validationMessages = Object.values(problem?.errors ?? {}).flat()
    const message = validationMessages.join('\n') || String(problem?.detail || problem?.title || fallback)

    if (problem?.type === CHURCH_CONFLICT_PROBLEM_TYPE) {
      conflictListeners.forEach(listener => listener())
      throw new ChurchConflictError(message, response.status, problem)
    }

    throw new ApiError(message, response.status, problem)
  },
})

/** The response's Problem Details, or null when the body is not Problem Details JSON. */
function parseProblem(response: Response, body: string): ProblemDetails | null {
  if (!body || !response.headers.get('content-type')?.includes('application/problem+json')) return null

  try {
    // SAFETY: This response uses the generated Problem Details contract; malformed JSON falls through to the catch.
    return JSON.parse(body) as ProblemDetails
  } catch {
    return null
  }
}

type PersonMatch = components['schemas']['PersonLookupResponse']

/**
 * People who may already exist for the new person at `index` in the request. Mirrors the API's SimilarPeopleResponse,
 * which reaches the client as a Problem Details extension that the OpenAPI document does not describe.
 */
export interface SimilarPeople {
  index: number
  matches: PersonMatch[]
}

/** What a "confirm to save anyway" response warns about. */
export interface ConfirmationReasons {
  /** Meetings already recorded for the group on the same date. */
  existingSessionIds: string[]
  similarPeople: SimilarPeople[]
}

/** The warnings in `error` when the API asked for confirmation, or null for any other failure. */
export function confirmationReasons(error: Error): ConfirmationReasons | null {
  if (!(error instanceof ApiError) || error.problem?.type !== CONFIRMATION_REQUIRED_PROBLEM_TYPE) return null

  // SAFETY: The problem type is the API's confirmation problem, whose extensions are these optional members (they are
  // top-level members the generated type does not list). Each is still checked to be an array before use.
  const { existingSessionIds, similarPeople } = error.problem as ProblemDetails & Partial<ConfirmationReasons>

  return {
    existingSessionIds: Array.isArray(existingSessionIds) ? existingSessionIds : [],
    similarPeople: Array.isArray(similarPeople) ? similarPeople : [],
  }
}
