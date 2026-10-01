import { useLabels, useMembers, useActivity } from '../api/queries'
import { PRIORITIES, STATUSES, type Activity } from '../api/types'
import { Avatar, Button, cx, Empty, ErrorText, formatDateTime, ListSkeleton, Skeleton, timeAgo } from './ui'

const entityNames: Record<string, string> = { task: 'la tarea', project: 'el proyecto', comment: 'un comentario', label: 'la etiqueta', repository: 'el repositorio' }
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

/** Qué hizo, sin el nombre de quién: así el nombre se puede resaltar aparte. */
function describe(a: Activity, show: Lookup): string {
  const target = entityNames[a.entityType] ?? a.entityType
  const title = (a.changes.title ?? a.changes.name) as string | undefined

  if (a.action === 'created') return `creó ${target}${title ? ` «${title}»` : ''}`
  if (a.action === 'deleted') return `eliminó ${target}`

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
  if (parts.length) return `cambió ${target} — ${parts.join('; ')}`
  return 'position' in a.changes ? `movió ${target} en el board` : `actualizó ${target}`
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
    // El vencimiento es un día guardado como medianoche UTC: se muestra en UTC para no correrlo un día.
    if (field === 'dueAt') return new Date(String(value)).toLocaleDateString('es', { timeZone: 'UTC' })
    if (typeof value === 'string' && value.length > 60) return `${value.slice(0, 60)}…`
    return String(value)
  }

  const items = data?.pages.flatMap((p) => p.items) ?? []

  if (isLoading)
    return compact ? (
      <div role="status" className="flex flex-col gap-3">
        <span className="sr-only">Cargando…</span>
        <Skeleton className="h-3 w-3/4" />
        <Skeleton className="h-3 w-1/2" />
      </div>
    ) : (
      <div className="max-w-3xl">
        <ListSkeleton rows={6} />
      </div>
    )

  // Vista completa: agrupada por día. En el detalle de una tarea (compact) es una sola línea de tiempo.
  const groups: { day: string; items: Activity[] }[] = []
  for (const a of items) {
    const day = compact ? '' : dayLabel(a.createdAt)
    if (groups.at(-1)?.day === day) groups.at(-1)!.items.push(a)
    else groups.push({ day, items: [a] })
  }

  return (
    <div className={cx(!compact && 'max-w-3xl')}>
      <ErrorText error={error} />
      {items.length === 0 && !error && (
        <Empty compact={compact} icon="activity" title="Sin actividad todavía">
          {!compact && 'Cada cambio en tareas, proyectos y etiquetas queda registrado acá.'}
        </Empty>
      )}
      <div className="flex flex-col gap-6">
        {groups.map((g) => (
          <section key={g.day}>
            {g.day && <h2 className="mb-3 font-display text-xs font-semibold tracking-widest text-mint uppercase">{g.day}</h2>}
            {/* Línea de tiempo: el filete vertical une los puntos de cada evento. */}
            <ol className={cx('stagger relative flex flex-col before:absolute before:top-2 before:bottom-2 before:w-px before:bg-line/70', compact ? 'gap-3 before:left-[3px]' : 'gap-4 before:left-3')}>
              {g.items.map((a) => (
                <li key={a.id} className="relative flex items-start gap-3 text-sm">
                  {compact ? (
                    <span className={cx('mt-1.5 size-[7px] shrink-0 rounded-full ring-4 ring-panel', actionDot[a.action])} />
                  ) : (
                    <span className="rounded-full ring-4 ring-void">
                      <Avatar name={a.actorName ?? 'Sistema'} />
                    </span>
                  )}
                  <div className="min-w-0">
                    <p className="break-words text-dim">
                      <strong className="font-medium text-ink">{a.actorName ?? 'El sistema'}</strong> {describe(a, show)}
                    </p>
                    <time className="text-xs text-dim/80" dateTime={a.createdAt} title={formatDateTime(a.createdAt)}>
                      {compact ? timeAgo(a.createdAt) : new Date(a.createdAt).toLocaleTimeString('es', { hour: '2-digit', minute: '2-digit' })}
                    </time>
                  </div>
                </li>
              ))}
            </ol>
          </section>
        ))}
      </div>
      {hasNextPage && (
        <Button variant="secondary" className="mt-4" onClick={() => fetchNextPage()} disabled={isFetchingNextPage}>
          {isFetchingNextPage ? 'Cargando…' : 'Cargar más'}
        </Button>
      )}
    </div>
  )
}

const actionDot: Record<Activity['action'], string> = { created: 'bg-neon', updated: 'bg-violet', deleted: 'bg-hot' }

function dayLabel(iso: string) {
  const date = new Date(iso)
  const days = Math.round((new Date().setHours(0, 0, 0, 0) - new Date(date).setHours(0, 0, 0, 0)) / 86_400_000)
  if (days === 0) return 'Hoy'
  if (days === 1) return 'Ayer'
  return date.toLocaleDateString('es', { weekday: 'long', day: 'numeric', month: 'long' })
}
