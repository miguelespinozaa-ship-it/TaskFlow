export type TaskStatus = 'Todo' | 'InProgress' | 'InReview' | 'Done'
export type TaskPriority = 'Low' | 'Medium' | 'High' | 'Urgent'

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

export interface CreateTaskInput {
  title: string
  description?: string
  priority: TaskPriority
}

interface ProblemDetails {
  title?: string
  errors?: Record<string, string[]>
}

const BASE_URL = import.meta.env.VITE_API_URL ?? 'http://localhost:5080'

export class ApiError extends Error {
  constructor(message: string, readonly status: number) {
    super(message)
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE_URL}/api/v1${path}`, {
    ...init,
    headers: { 'Content-Type': 'application/json', ...init?.headers },
  })
  if (!res.ok) {
    // La API siempre responde errores como ProblemDetails (RFC 9457).
    const problem = (await res.json().catch(() => ({}))) as ProblemDetails
    const detail = problem.errors ? Object.values(problem.errors).flat().join(' ') : ''
    throw new ApiError(detail || problem.title || `Error ${res.status}`, res.status)
  }
  return (await res.json()) as T
}

export const api = {
  listProjects: () => request<Project[]>('/projects'),
  listTasks: (projectId: string) => request<Task[]>(`/projects/${projectId}/tasks`),
  createTask: (projectId: string, input: CreateTaskInput) =>
    request<Task>(`/projects/${projectId}/tasks`, { method: 'POST', body: JSON.stringify(input) }),
}
