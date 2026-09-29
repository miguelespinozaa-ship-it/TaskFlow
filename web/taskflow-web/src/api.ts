export type TaskStatus = 'Todo' | 'InProgress' | 'InReview' | 'Done'
export type TaskPriority = 'Low' | 'Medium' | 'High' | 'Urgent'
export type WorkspaceRole = 'Owner' | 'Admin' | 'Member' | 'Viewer'

export interface Project {
  id: string
  workspaceId: string
  name: string
  description: string | null
  keyPrefix: string
  createdAt: string
}

export interface Task {
  id: string
  projectId: string
  title: string
  description: string | null
  status: TaskStatus
  priority: TaskPriority
  position: number
  createdAt: string
}

export interface User {
  id: string
  email: string
  displayName: string
}

export interface CurrentWorkspace {
  id: string
  name: string
  slug: string
  role: WorkspaceRole
}

export interface WorkspaceSummary extends CurrentWorkspace {}

export interface AuthResponse {
  accessToken: string
  expiresAt: string
  user: User
  workspace: CurrentWorkspace
}

export interface CreateTaskInput {
  title: string
  description?: string
  priority: TaskPriority
}

interface ProblemDetails {
  title?: string
  errors?: Record<string, string[]>
}

export class ApiError extends Error {
  readonly status: number
  constructor(message: string, status: number) {
    super(message)
    this.status = status
  }
}

// El access token vive SOLO en memoria: no en localStorage (lo leería cualquier XSS).
// Al recargar la página se pierde y se recupera con /auth/refresh usando la cookie httpOnly.
let accessToken: string | null = null
let onSessionChange: (session: AuthResponse | null) => void = () => {}

export function subscribeSession(listener: (session: AuthResponse | null) => void) {
  onSessionChange = listener
}

function setSession(session: AuthResponse | null) {
  accessToken = session?.accessToken ?? null
  onSessionChange(session)
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
  return new ApiError(detail || problem.title || `Error ${res.status}`, res.status)
}

async function request<T>(path: string, init?: RequestInit, retry = true): Promise<T> {
  const res = await fetch(`/api/v1${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
      ...init?.headers,
    },
  })

  // Access token vencido (dura 15 min): renovamos una vez y reintentamos.
  if (res.status === 401 && retry && accessToken) {
    if (await refreshSession()) return request<T>(path, init, false)
  }
  if (!res.ok) throw await toError(res)
  return res.status === 204 ? (undefined as T) : ((await res.json()) as T)
}

async function authRequest(path: string, body?: unknown): Promise<AuthResponse> {
  const session = await request<AuthResponse>(path, { method: 'POST', body: JSON.stringify(body ?? {}) }, false)
  setSession(session)
  return session
}

export const api = {
  login: (email: string, password: string) => authRequest('/auth/login', { email, password }),
  register: (email: string, password: string, displayName: string) =>
    authRequest('/auth/register', { email, password, displayName }),
  switchWorkspace: (workspaceId: string) => authRequest('/auth/switch-workspace', { workspaceId }),
  logout: async () => {
    await request<void>('/auth/logout', { method: 'POST' }, false).catch(() => {})
    setSession(null)
  },

  listWorkspaces: () => request<WorkspaceSummary[]>('/workspaces'),
  listProjects: () => request<Project[]>('/projects'),
  createProject: (name: string, keyPrefix: string) =>
    request<Project>('/projects', { method: 'POST', body: JSON.stringify({ name, keyPrefix }) }),
  listTasks: (projectId: string) => request<Task[]>(`/projects/${projectId}/tasks`),
  createTask: (projectId: string, input: CreateTaskInput) =>
    request<Task>(`/projects/${projectId}/tasks`, { method: 'POST', body: JSON.stringify(input) }),
}
