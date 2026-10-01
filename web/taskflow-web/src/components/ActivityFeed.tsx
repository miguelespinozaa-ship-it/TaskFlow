import { useLabels, useMembers, useActivity } from '../api/queries'
import { PRIORITIES, STATUSES, type Activity } from '../api/types'
import { Avatar, Button, Empty, ErrorText, formatDateTime, Spinner } from './ui'

const entityNames: Record<string, string> = { task: 'la tarea', project: 'el proyecto', comment: 'un comentario', label: 'la etiqueta' }
const fieldNames: Record<string, string> = {
  title: 'título',
  name: 'nombre',
  description: 'descripción',
  status: 'estado',
  priority: 'prioridad',
  position: 'posición',
  assigneeId: 'asignado',
  dueAt: 'vencimiento',
  completedAt: 'completada',
  isArchived: 'archivado',
  body: 'texto',
  color: 'color',
}

type Lookup = (value: unknown, field: string) => string

function describe(a: Activity, show: Lookup): string {
  const who = a.actorName ?? 'El sistema'
  const target = entityNames[a.entityType] ?? a.entityType
  const title = (a.changes.title ?? a.changes.name) as string | undefined

  if (a.action === 'created') return `${who} creó ${target}${title ? ` «${title}»` : ''}`
  if (a.action === 'deleted') return `${who} eliminó ${target}`

  const parts = Object.entries(a.changes)
    // La posición cambia en cada movimiento; mostrarla solo agrega ruido.
    .filter(([field]) => field !== 'position' && field !== 'completedAt')
    .map(([field, change]) => {
      if (field === 'labels') {
        const { added = [], removed = [] } = change as { added?: string[]; removed?: string[] }
        return [added.length && `agregó ${added.map((id) => show(id, 'label')).join(', ')}`, removed.length && `quitó ${removed.map((id) => show(id, 'label')).join(', ')}`]
          .filter(Boolean)
          .join(' y ')
      }
      const { from, to } = change as { from: unknown; to: unknown }
      return `${fieldNames[field] ?? field}: ${show(from, field)} → ${show(to, field)}`
    })
  if (parts.length) return `${who} cambió ${target} — ${parts.join('; ')}`
  return 'position' in a.changes ? `${who} movió ${target} en el board` : `${who} actualizó ${target}`
}

export function ActivityList({ entityId, compact = false }: { entityId?: string; compact?: boolean }) {
  const { data, isLoading, error, fetchNextPage, hasNextPage, isFetchingNextPage } = useActivity(entityId)
  const { data: members = [] } = useMembers()
  const { data: labels = [] } = useLabels()

  const show: Lookup = (value, field) => {
    if (value === null || value === undefined || value === '') return '—'
    if (field === 'status') return STATUSES.find((s) => s.value === value)?.label ?? String(value)
    if (field === 'priority') return PRIORITIES.find((p) => p.value === value)?.label ?? String(value)
    if (field === 'assigneeId') return members.find((m) => m.userId === value)?.displayName ?? 'alguien'
    if (field === 'label') return labels.find((l) => l.id === value)?.name ?? 'una etiqueta borrada'
    if (field === 'dueAt') return new Date(String(value)).toLocaleDateString('es')
    if (typeof value === 'string' && value.length > 60) return `${value.slice(0, 60)}…`
    return String(value)
  }

  const items = data?.pages.flatMap((p) => p.items) ?? []

  if (isLoading) return <Spinner />
  return (
    <div>
      <ErrorText error={error} />
      {items.length === 0 && <Empty>Sin actividad todavía.</Empty>}
      <ol className="stagger flex flex-col gap-2">
        {items.map((a) => (
          <li key={a.id} className="flex items-start gap-2 text-sm">
            {!compact && <Avatar name={a.actorName ?? 'Sistema'} />}
            <div>
              <p className="text-ink">{describe(a, show)}</p>
              <time className="text-xs text-dim" dateTime={a.createdAt}>
                {formatDateTime(a.createdAt)}
              </time>
            </div>
          </li>
        ))}
      </ol>
      {hasNextPage && (
        <Button variant="secondary" className="mt-3" onClick={() => fetchNextPage()} disabled={isFetchingNextPage}>
          {isFetchingNextPage ? 'Cargando…' : 'Cargar más'}
        </Button>
      )}
    </div>
  )
}
