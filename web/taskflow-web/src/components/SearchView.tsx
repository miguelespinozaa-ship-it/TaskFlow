import { useEffect, useState } from 'react'
import { useLabels, useMembers, useProjects, useTaskSearch } from '../api/queries'
import { STATUSES, type TaskQuery, type TaskStatus } from '../api/types'
import { Button, Empty, ErrorText, formatDate, Input, LabelChip, PriorityBadge, Select, Spinner } from './ui'

function useDebounced<T>(value: T, ms = 300) {
  const [debounced, setDebounced] = useState(value)
  useEffect(() => {
    const t = setTimeout(() => setDebounced(value), ms)
    return () => clearTimeout(t)
  }, [value, ms])
  return debounced
}

export function SearchView({ onOpen }: { onOpen: (taskId: string) => void }) {
  const [text, setText] = useState('')
  const [filters, setFilters] = useState<Omit<TaskQuery, 'search'>>({})
  const search = useDebounced(text.trim())
  const query: TaskQuery = { ...filters, search: search || undefined }

  const { data, isLoading, error, fetchNextPage, hasNextPage, isFetchingNextPage } = useTaskSearch(query)
  const { data: projects = [] } = useProjects()
  const { data: members = [] } = useMembers()
  const { data: labels = [] } = useLabels()

  const tasks = data?.pages.flatMap((p) => p.items) ?? []
  const set = (patch: Partial<TaskQuery>) => setFilters((f) => ({ ...f, ...patch }))

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap gap-2">
        <Input
          type="search"
          placeholder="Buscar en títulos y descripciones…"
          value={text}
          onChange={(e) => setText(e.target.value)}
          aria-label="Buscar tareas"
          className="min-w-64 flex-1"
        />
        <Select aria-label="Filtrar por proyecto" value={filters.projectId ?? ''} onChange={(e) => set({ projectId: e.target.value || undefined })}>
          <option value="">Todos los proyectos</option>
          {projects.map((p) => (
            <option key={p.id} value={p.id}>
              {p.name}
            </option>
          ))}
        </Select>
        <Select aria-label="Filtrar por estado" value={filters.status ?? ''} onChange={(e) => set({ status: (e.target.value || undefined) as TaskStatus })}>
          <option value="">Cualquier estado</option>
          {STATUSES.map((s) => (
            <option key={s.value} value={s.value}>
              {s.label}
            </option>
          ))}
        </Select>
        <Select aria-label="Filtrar por asignado" value={filters.assigneeId ?? ''} onChange={(e) => set({ assigneeId: e.target.value || undefined })}>
          <option value="">Cualquier persona</option>
          {members.map((m) => (
            <option key={m.userId} value={m.userId}>
              {m.displayName}
            </option>
          ))}
        </Select>
        <Select aria-label="Filtrar por etiqueta" value={filters.labelId ?? ''} onChange={(e) => set({ labelId: e.target.value || undefined })}>
          <option value="">Cualquier etiqueta</option>
          {labels.map((l) => (
            <option key={l.id} value={l.id}>
              {l.name}
            </option>
          ))}
        </Select>
      </div>

      <ErrorText error={error} />
      {isLoading ? (
        <Spinner />
      ) : tasks.length === 0 ? (
        <Empty>No hay tareas que coincidan.</Empty>
      ) : (
        <ul className="stagger divide-y divide-line overflow-hidden rounded-lg border border-line bg-panel/80 backdrop-blur-sm">
          {tasks.map((t) => {
            const project = projects.find((p) => p.id === t.projectId)
            return (
              <li key={t.id}>
                <button
                  type="button"
                  onClick={() => onOpen(t.id)}
                  className="flex w-full items-center gap-3 border-l-2 border-transparent px-3 py-2 text-left text-sm transition duration-150 hover:border-neon hover:bg-raised hover:pl-4"
                >
                  <span className="w-12 shrink-0 font-display text-xs text-violet">{project?.keyPrefix}</span>
                  <span className="flex-1 font-medium">{t.title}</span>
                  {t.labelIds.map((id) => labels.find((l) => l.id === id)).filter(Boolean).map((l) => <LabelChip key={l!.id} label={l!} />)}
                  <span className="w-24 text-xs text-dim">{STATUSES.find((s) => s.value === t.status)?.label}</span>
                  <PriorityBadge priority={t.priority} />
                  <span className="w-16 text-right text-xs text-dim">{formatDate(t.createdAt)}</span>
                </button>
              </li>
            )
          })}
        </ul>
      )}
      {hasNextPage && (
        <div>
          <Button variant="secondary" onClick={() => fetchNextPage()} disabled={isFetchingNextPage}>
            {isFetchingNextPage ? 'Cargando…' : 'Cargar más'}
          </Button>
        </div>
      )}
    </div>
  )
}
