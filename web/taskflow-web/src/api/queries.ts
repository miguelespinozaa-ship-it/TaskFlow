import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { request, setSession, toQueryString } from './client'
import type {
  Activity,
  AuthResponse,
  Comment,
  CursorPage,
  Label,
  Member,
  Commit,
  Project,
  Repository,
  SyncResult,
  Task,
  TaskPriority,
  TaskQuery,
  TaskStatus,
  WorkspacePlan,
  WorkspaceRole,
  WorkspaceUsage,
  WorkspaceSummary,
} from './types'

// Claves de cache centralizadas: invalidar "tasks" refresca board, búsqueda y detalle a la vez.
export const keys = {
  workspaces: ['workspaces'] as const,
  members: ['members'] as const,
  usage: ['usage'] as const,
  projects: ['projects'] as const,
  labels: ['labels'] as const,
  board: (projectId: string) => ['tasks', 'board', projectId] as const,
  task: (id: string) => ['tasks', 'detail', id] as const,
  search: (q: TaskQuery) => ['tasks', 'search', q] as const,
  comments: (taskId: string) => ['comments', taskId] as const,
  activity: (entityId?: string) => ['activity', entityId ?? 'all'] as const,
  repository: (projectId: string) => ['repository', projectId] as const,
  commits: (projectId: string) => ['commits', projectId] as const,
}

// ---------- Sesión ----------

async function authRequest(path: string, json?: unknown) {
  const session = await request<AuthResponse>(path, { method: 'POST', json: json ?? {} }, false)
  setSession(session)
  return session
}

export const auth = {
  login: (email: string, password: string) => authRequest('/auth/login', { email, password }),
  register: (email: string, password: string, displayName: string) =>
    authRequest('/auth/register', { email, password, displayName }),
  switchWorkspace: (workspaceId: string) => authRequest('/auth/switch-workspace', { workspaceId }),
  logout: async () => {
    await request<void>('/auth/logout', { method: 'POST' }, false).catch(() => {})
    setSession(null)
  },
}

// ---------- Lecturas ----------

export const useWorkspaces = () =>
  useQuery({ queryKey: keys.workspaces, queryFn: () => request<WorkspaceSummary[]>('/workspaces') })

export const useMembers = () =>
  useQuery({ queryKey: keys.members, queryFn: () => request<Member[]>('/workspaces/current/members') })

export const useProjects = () => useQuery({ queryKey: keys.projects, queryFn: () => request<Project[]>('/projects') })

export const useLabels = () => useQuery({ queryKey: keys.labels, queryFn: () => request<Label[]>('/labels') })

export const useBoard = (projectId: string | undefined) =>
  useQuery({
    queryKey: keys.board(projectId ?? ''),
    queryFn: () => request<Task[]>(`/projects/${projectId}/tasks`),
    enabled: !!projectId,
  })

export const useTask = (id: string) => useQuery({ queryKey: keys.task(id), queryFn: () => request<Task>(`/tasks/${id}`) })

export const useComments = (taskId: string) =>
  useQuery({ queryKey: keys.comments(taskId), queryFn: () => request<Comment[]>(`/tasks/${taskId}/comments`) })

/** Paginación por cursor: cada página trae el cursor de la siguiente. */
export const useTaskSearch = (query: TaskQuery) =>
  useInfiniteQuery({
    queryKey: keys.search(query),
    queryFn: ({ pageParam }) =>
      request<CursorPage<Task>>(`/tasks${toQueryString({ ...query, cursor: pageParam, pageSize: 20 })}`),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  })

export const useActivity = (entityId?: string) =>
  useInfiniteQuery({
    queryKey: keys.activity(entityId),
    queryFn: ({ pageParam }) =>
      request<CursorPage<Activity>>(
        `${entityId ? `/tasks/${entityId}/activity` : '/activity'}${toQueryString({ cursor: pageParam, pageSize: 20 })}`,
      ),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  })

// ---------- Escrituras ----------

/** Toda escritura puede generar actividad y cambiar tareas: refrescamos esas caches al terminar. */
function useInvalidateAfter() {
  const qc = useQueryClient()
  return () => {
    qc.invalidateQueries({ queryKey: ['tasks'] })
    qc.invalidateQueries({ queryKey: ['activity'] })
  }
}

export function useCreateProject() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (input: { name: string; keyPrefix: string }) => request<Project>('/projects', { method: 'POST', json: input }),
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.projects }),
  })
}

export interface CreateTaskInput {
  title: string
  description?: string
  priority: TaskPriority
}

export function useCreateTask(projectId: string) {
  const invalidate = useInvalidateAfter()
  return useMutation({
    mutationFn: (input: CreateTaskInput) => request<Task>(`/projects/${projectId}/tasks`, { method: 'POST', json: input }),
    onSuccess: invalidate,
  })
}

export interface UpdateTaskInput {
  title?: string
  description?: string
  priority?: TaskPriority
  dueAt?: string
  clearDueAt?: boolean
}

export function useUpdateTask(id: string) {
  const invalidate = useInvalidateAfter()
  return useMutation({
    mutationFn: (input: UpdateTaskInput) => request<Task>(`/tasks/${id}`, { method: 'PATCH', json: input }),
    onSuccess: invalidate,
  })
}

export function useAssignTask(id: string) {
  const invalidate = useInvalidateAfter()
  return useMutation({
    mutationFn: (assigneeId: string | null) => request<Task>(`/tasks/${id}/assign`, { method: 'POST', json: { assigneeId } }),
    onSuccess: invalidate,
  })
}

export function useSetTaskLabels(id: string) {
  const invalidate = useInvalidateAfter()
  return useMutation({
    mutationFn: (labelIds: string[]) => request<Task>(`/tasks/${id}/labels`, { method: 'PUT', json: { labelIds } }),
    onSuccess: invalidate,
  })
}

export function useDeleteTask() {
  const invalidate = useInvalidateAfter()
  return useMutation({
    mutationFn: (id: string) => request<void>(`/tasks/${id}`, { method: 'DELETE' }),
    onSuccess: invalidate,
  })
}

export interface MoveInput {
  taskId: string
  status: TaskStatus
  afterTaskId: string | null
  /** Orden completo del board tras soltar, para pintarlo antes de que responda la API. */
  optimisticBoard: Task[]
}

/**
 * Mover en el board con actualización optimista: la tarjeta queda donde se soltó al instante.
 * Si la API rechaza el movimiento (p. ej. un Viewer, o la tarea ya no existe), se restaura el board anterior.
 */
export function useMoveTask(projectId: string) {
  const qc = useQueryClient()
  const key = keys.board(projectId)
  return useMutation({
    mutationFn: ({ taskId, status, afterTaskId }: MoveInput) =>
      request<Task>(`/tasks/${taskId}/move`, { method: 'POST', json: { status, afterTaskId } }),
    onMutate: async ({ optimisticBoard }) => {
      await qc.cancelQueries({ queryKey: key }) // que un refetch en vuelo no pise el estado optimista
      const previous = qc.getQueryData<Task[]>(key)
      qc.setQueryData(key, optimisticBoard)
      return { previous }
    },
    onError: (_err, _input, context) => {
      if (context?.previous) qc.setQueryData(key, context.previous)
    },
    onSettled: () => {
      qc.invalidateQueries({ queryKey: ['tasks'] })
      qc.invalidateQueries({ queryKey: ['activity'] })
    },
  })
}

export function useCreateLabel() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (input: { name: string; color: string }) => request<Label>('/labels', { method: 'POST', json: input }),
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.labels }),
  })
}

export function useAddComment(taskId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: string) => request<Comment>(`/tasks/${taskId}/comments`, { method: 'POST', json: { body } }),
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.comments(taskId) }),
  })
}

export function useDeleteComment(taskId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (commentId: string) => request<void>(`/comments/${commentId}`, { method: 'DELETE' }),
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.comments(taskId) }),
  })
}

export function useAddMember() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (input: { email: string; role: Exclude<WorkspaceRole, 'Owner'> }) =>
      request<Member>('/workspaces/current/members', { method: 'POST', json: input }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: keys.members })
      qc.invalidateQueries({ queryKey: keys.usage }) // el contador de miembros del plan
    },
  })
}

// ---------- Repositorio de GitHub ----------

/** null = el proyecto no tiene repositorio conectado (la API responde 204). */
export const useRepository = (projectId: string | undefined) =>
  useQuery({
    queryKey: keys.repository(projectId ?? ''),
    queryFn: async () => (await request<Repository | undefined>(`/projects/${projectId}/repository`)) ?? null,
    enabled: !!projectId,
  })

export const useCommits = (projectId: string | undefined, enabled: boolean) =>
  useInfiniteQuery({
    queryKey: keys.commits(projectId ?? ''),
    queryFn: ({ pageParam }) =>
      request<CursorPage<Commit>>(`/projects/${projectId}/commits${toQueryString({ cursor: pageParam, pageSize: 20 })}`),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
    enabled: enabled && !!projectId,
    // El servidor sincroniza solo cada pocos minutos: la lista abierta se refresca para mostrar lo nuevo.
    refetchInterval: 60_000,
  })

/** Conectar, sincronizar y desconectar cambian el enlace, la lista de commits y el historial. */
function useRepositoryMutation<TInput, TResult>(projectId: string, mutationFn: (input: TInput) => Promise<TResult>) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn,
    // onSettled y no onSuccess: una sincronización fallida igual cambia el enlace (guarda el error).
    onSettled: () => {
      qc.invalidateQueries({ queryKey: keys.repository(projectId) })
      qc.invalidateQueries({ queryKey: keys.commits(projectId) })
      qc.invalidateQueries({ queryKey: ['activity'] })
    },
  })
}

export const useLinkRepository = (projectId: string) =>
  useRepositoryMutation(projectId, (repository: string) =>
    request<SyncResult>(`/projects/${projectId}/repository`, { method: 'PUT', json: { repository } }),
  )

export const useSyncRepository = (projectId: string) =>
  useRepositoryMutation(projectId, () => request<SyncResult>(`/projects/${projectId}/repository/sync`, { method: 'POST' }))

export const useUnlinkRepository = (projectId: string) =>
  useRepositoryMutation(projectId, () => request<void>(`/projects/${projectId}/repository`, { method: 'DELETE' }))

// ---------- Plan del workspace ----------

export const useWorkspaceUsage = () =>
  useQuery({ queryKey: keys.usage, queryFn: () => request<WorkspaceUsage>('/workspaces/current') })

export function useChangePlan() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (plan: WorkspacePlan) => request<WorkspaceUsage>('/workspaces/current/plan', { method: 'PUT', json: { plan } }),
    onSuccess: (usage) => qc.setQueryData(keys.usage, usage),
  })
}
