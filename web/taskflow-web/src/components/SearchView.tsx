import { useEffect, useState } from 'react'
import { useLabels, useMembers, useProjects, useTaskSearch } from '../api/queries'
import { STATUSES, type TaskQuery, type TaskStatus } from '../api/types'
import { Icon } from './icons'
import { Button, Empty, ErrorText, Input, LabelChip, ListSkeleton, PriorityBadge, Select, StatusPill, timeAgo } from './ui'

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

  const filtering = !!search || Object.values(filters).some(Boolean)

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-col gap-2 rounded-xl border border-line bg-panel/60 p-2.5 backdrop-blur-sm">
        <label className="flex items-center gap-2 rounded-md border border-line bg-void/70 pl-3 transition duration-150 focus-within:border-neon focus-within:shadow-neon hover:border-violet focus-within:hover:border-neon">
          <Icon name="search" className="text-dim" />
          <Input
            bare
            type="search"
            placeholder="Buscar en títulos y descripciones…"
            value={text}
            onChange={(e) => setText(e.target.value)}
            aria-label="Buscar tareas"
            className="flex-1 py-2"
          />
        </label>
        <div className="grid grid-cols-2 gap-2 lg:grid-cols-4">
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
      </div>

      <ErrorText error={error} />
      {isLoading ? (
        <ListSkeleton />
      ) : tasks.length === 0 ? (
        !error && (
          <div className="rounded-xl border border-dashed border-line bg-panel/40">
            <Empty icon="search" title={filtering ? 'No hay tareas que coincidan' : 'Todavía no hay tareas'}>
              {filtering ? 'Probá con otras palabras o quitá algún filtro.' : 'Las tareas de todos los proyectos del workspace van a aparecer acá.'}
            </Empty>
            {filtering && (
              <div className="-mt-6 flex justify-center pb-8">
                <Button
                  variant="secondary"
                  onClick={() => {
                    setText('')
                    setFilters({})
                  }}
                >
                  Limpiar filtros
                </Button>
              </div>
            )}
          </div>
        )
      ) : (
        <>
          <p className="-mb-2 text-xs text-dim">
            {tasks.length} {tasks.length === 1 ? 'tarea' : 'tareas'}
            {hasNextPage && ' (hay más)'}
          </p>
          <ul className="stagger divide-y divide-line/70 overflow-hidden rounded-xl border border-line bg-panel/70 backdrop-blur-sm">
            {tasks.map((t) => {
              const project = projects.find((p) => p.id === t.projectId)
              const assignee = members.find((m) => m.userId === t.assigneeId)
              return (
                <li key={t.id}>
                  <button
                    type="button"
                    onClick={() => onOpen(t.id)}
                    data-task-id={t.id}
                    className="flex w-full flex-wrap items-center gap-x-3 gap-y-1.5 border-l-2 border-transparent px-3 py-2.5 text-left text-sm transition duration-150 hover:border-neon hover:bg-raised/70 focus-visible:border-neon focus-visible:bg-raised/70 focus-visible:outline-none"
                  >
                    <span className="w-12 shrink-0 truncate font-display text-xs text-violet">{project?.keyPrefix}</span>
                    {/* En móvil el título ocupa la primera línea y los metadatos bajan a la segunda. */}
                    <span className="min-w-0 basis-[calc(100%-4rem)] font-medium break-words sm:flex-1 sm:basis-0">{t.title}</span>
                    {t.labelIds.map((id) => labels.find((l) => l.id === id)).filter(Boolean).map((l) => <LabelChip key={l!.id} label={l!} />)}
                    <StatusPill status={t.status} label={STATUSES.find((s) => s.value === t.status)!.label} />
                    <PriorityBadge priority={t.priority} />
                    {assignee && <span className="hidden max-w-28 truncate text-xs text-dim md:block">{assignee.displayName}</span>}
                    <span className="ml-auto w-20 shrink-0 text-right text-xs whitespace-nowrap text-dim sm:ml-0">{timeAgo(t.createdAt)}</span>
                  </button>
                </li>
              )
            })}
          </ul>
        </>
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
