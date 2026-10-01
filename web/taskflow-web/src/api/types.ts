// Espejo de los DTOs de la API (TaskFlow.Application). Enums como strings (JsonStringEnumConverter).

export type TaskStatus = 'Todo' | 'InProgress' | 'InReview' | 'Done'
export type TaskPriority = 'Low' | 'Medium' | 'High' | 'Urgent'
export type WorkspaceRole = 'Owner' | 'Admin' | 'Member' | 'Viewer'

export const STATUSES: { value: TaskStatus; label: string }[] = [
  { value: 'Todo', label: 'Por hacer' },
  { value: 'InProgress', label: 'En progreso' },
  { value: 'InReview', label: 'En revisión' },
  { value: 'Done', label: 'Hecho' },
]

export const PRIORITIES: { value: TaskPriority; label: string }[] = [
  { value: 'Low', label: 'Baja' },
  { value: 'Medium', label: 'Media' },
  { value: 'High', label: 'Alta' },
  { value: 'Urgent', label: 'Urgente' },
]

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

export type WorkspaceSummary = CurrentWorkspace

export interface AuthResponse {
  accessToken: string
  expiresAt: string
  user: User
  workspace: CurrentWorkspace
}

export interface Member {
  userId: string
  email: string
  displayName: string
  role: WorkspaceRole
  joinedAt: string
}

export interface Project {
  id: string
  workspaceId: string
  name: string
  description: string | null
  keyPrefix: string
  isArchived: boolean
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
  assigneeId: string | null
  reporterId: string | null
  dueAt: string | null
  completedAt: string | null
  createdAt: string
  updatedAt: string
  labelIds: string[]
}

export interface Label {
  id: string
  name: string
  color: string
}

export interface Comment {
  id: string
  taskId: string
  authorId: string
  authorName: string
  body: string
  createdAt: string
  editedAt: string | null
}

export interface Activity {
  id: string
  actorId: string | null
  actorName: string | null
  entityType: string
  entityId: string
  action: 'created' | 'updated' | 'deleted'
  changes: Record<string, unknown>
  createdAt: string
}

export interface Repository {
  projectId: string
  owner: string
  name: string
  fullName: string
  htmlUrl: string
  defaultBranch: string
  lastSyncedAt: string | null
  lastSyncError: string | null
}

/** Lo que un commit cambió dentro de una carpeta. path "" = raíz del repositorio. */
export interface FolderChange {
  path: string
  files: number
  additions: number
  deletions: number
}

export interface Commit {
  id: string
  sha: string
  title: string
  /** El resto del mensaje del commit: la explicación larga, si quien lo hizo la escribió. */
  body: string | null
  authorName: string
  authorLogin: string | null
  authorAvatarUrl: string | null
  committedAt: string
  htmlUrl: string
  additions: number
  deletions: number
  filesChanged: number
  folders: FolderChange[]
}

export interface SyncResult {
  imported: number
  repository: Repository
}

export interface CursorPage<T> {
  items: T[]
  nextCursor: string | null
}

export interface TaskQuery {
  projectId?: string
  status?: TaskStatus
  assigneeId?: string
  labelId?: string
  search?: string
}
