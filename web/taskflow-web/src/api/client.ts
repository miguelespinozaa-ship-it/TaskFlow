import type { AuthResponse } from './types'

interface ProblemDetails {
  title?: string
  errors?: Record<string, string[]>
}

export class ApiError extends Error {
  readonly status: number
  readonly fieldErrors: Record<string, string[]>
  constructor(message: string, status: number, fieldErrors: Record<string, string[]> = {}) {
    super(message)
    this.status = status
    this.fieldErrors = fieldErrors
  }
}

// El access token vive SOLO en memoria: no en localStorage (lo leería cualquier XSS).
// Al recargar la página se pierde y se recupera con /auth/refresh usando la cookie httpOnly.
let accessToken: string | null = null
const listeners = new Set<(session: AuthResponse | null) => void>()

export function onSessionChange(listener: (session: AuthResponse | null) => void) {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export function setSession(session: AuthResponse | null) {
  accessToken = session?.accessToken ?? null
  listeners.forEach((l) => l(session))
}

// Un solo refresh en vuelo. Sin esto, dos requests que reciben 401 a la vez (o el doble useEffect
// de StrictMode) mandan el MISMO refresh token dos veces, el servidor lo detecta como reuso
// y revoca la sesión entera.
let refreshInFlight: Promise<AuthResponse | null> | null = null

export function refreshSession(): Promise<AuthResponse | null> {
  refreshInFlight ??= fetch('/api/v1/auth/refresh', { method: 'POST' })
    .then(async (res) => (res.ok ? ((await res.json()) as AuthResponse) : null))
    .catch(() => null)
    .then((session) => {
      setSession(session)
      return session
    })
    .finally(() => {
      refreshInFlight = null
    })
  return refreshInFlight
}

async function toError(res: Response): Promise<ApiError> {
  // La API siempre responde errores como ProblemDetails (RFC 9457).
  const problem = (await res.json().catch(() => ({}))) as ProblemDetails
  const detail = problem.errors ? Object.values(problem.errors).flat().join(' ') : ''
  return new ApiError(detail || problem.title || `Error ${res.status}`, res.status, problem.errors)
}

export async function request<T>(path: string, init?: RequestInit & { json?: unknown }, retry = true): Promise<T> {
  const { json, ...rest } = init ?? {}
  const res = await fetch(`/api/v1${path}`, {
    ...rest,
    body: json !== undefined ? JSON.stringify(json) : rest.body,
    headers: {
      'Content-Type': 'application/json',
      ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
      ...rest.headers,
    },
  })

  // Access token vencido (dura 15 min): renovamos una vez y reintentamos.
  if (res.status === 401 && retry && accessToken) {
    if (await refreshSession()) return request<T>(path, init, false)
  }
  if (!res.ok) throw await toError(res)
  return res.status === 204 ? (undefined as T) : ((await res.json()) as T)
}

export function toQueryString(params: Record<string, string | number | undefined | null>) {
  const qs = new URLSearchParams()
  for (const [k, v] of Object.entries(params)) if (v !== undefined && v !== null && v !== '') qs.set(k, String(v))
  const s = qs.toString()
  return s ? `?${s}` : ''
}
