import { useEffect, useMemo, useRef, useState } from 'react'
import {
  closestCorners,
  DndContext,
  DragOverlay,
  KeyboardSensor,
  PointerSensor,
  useDroppable,
  useSensor,
  useSensors,
  type DragEndEvent,
  type DragOverEvent,
  type DragStartEvent,
} from '@dnd-kit/core'
import { arrayMove, SortableContext, sortableKeyboardCoordinates, useSortable, verticalListSortingStrategy } from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import { useMoveTask } from '../api/queries'
import { STATUSES, type Label, type Member, type Task, type TaskStatus } from '../api/types'
import { Avatar, cx, Empty, formatDate, LabelChip, PriorityBadge } from './ui'

type Columns = Record<TaskStatus, Task[]>

// Conserva el orden en que llegan (la API ya ordena por columna y posición). No reordenamos por
// `position` acá: durante un movimiento optimista las posiciones todavía son las viejas.
function group(tasks: Task[]): Columns {
  const columns: Columns = { Todo: [], InProgress: [], InReview: [], Done: [] }
  for (const t of tasks) columns[t.status].push(t)
  return columns
}

const isStatus = (id: string): id is TaskStatus => STATUSES.some((s) => s.value === id)

interface BoardProps {
  projectId: string
  keyPrefix: string
  tasks: Task[]
  members: Member[]
  labels: Label[]
  canWrite: boolean
  onOpen: (taskId: string) => void
}

export function Board({ projectId, keyPrefix, tasks, members, labels, canWrite, onOpen }: BoardProps) {
  const move = useMoveTask(projectId)
  const [columns, setColumns] = useState<Columns>(() => group(tasks))
  const [activeId, setActiveId] = useState<string | null>(null)
  // Columna sobre la que está la tarjeta arrastrada (aunque el cursor esté encima de otra tarjeta).
  const [overStatus, setOverStatus] = useState<TaskStatus | null>(null)
  const origin = useRef<{ status: TaskStatus; afterTaskId: string | null } | null>(null)

  // Mientras NO se arrastra, el board refleja al servidor. Durante el arrastre manda el estado local.
  useEffect(() => {
    if (!activeId) setColumns(group(tasks))
  }, [tasks, activeId])

  const sensors = useSensors(
    // distance: un click normal abre el detalle; recién a los 5px empieza el arrastre.
    useSensor(PointerSensor, { activationConstraint: { distance: 5 } }),
    // Accesible: espacio para agarrar, flechas para mover, espacio para soltar.
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  )

  const membersById = useMemo(() => new Map(members.map((m) => [m.userId, m])), [members])
  const labelsById = useMemo(() => new Map(labels.map((l) => [l.id, l])), [labels])

  const findColumn = (cols: Columns, id: string): TaskStatus | undefined =>
    isStatus(id) ? id : STATUSES.map((s) => s.value).find((s) => cols[s].some((t) => t.id === id))

  const placement = (cols: Columns, taskId: string) => {
    const status = findColumn(cols, taskId)!
    const index = cols[status].findIndex((t) => t.id === taskId)
    return { status, afterTaskId: index > 0 ? cols[status][index - 1].id : null }
  }

  function onDragStart({ active }: DragStartEvent) {
    setActiveId(String(active.id))
    origin.current = placement(columns, String(active.id))
  }

  // Cruzar de columna se resuelve en dragOver para que la tarjeta "entre" en la columna destino
  // mientras se arrastra (y las demás le hagan lugar).
  function onDragOver({ active, over }: DragOverEvent) {
    setOverStatus(over ? (findColumn(columns, String(over.id)) ?? null) : null)
    if (!over) return
    setColumns((cols) => {
      const from = findColumn(cols, String(active.id))
      const to = findColumn(cols, String(over.id))
      if (!from || !to || from === to) return cols

      const task = cols[from].find((t) => t.id === active.id)!
      const overIndex = cols[to].findIndex((t) => t.id === over.id)
      const insertAt = overIndex >= 0 ? overIndex : cols[to].length
      return {
        ...cols,
        [from]: cols[from].filter((t) => t.id !== active.id),
        [to]: [...cols[to].slice(0, insertAt), { ...task, status: to }, ...cols[to].slice(insertAt)],
      }
    })
  }

  function onDragEnd({ active, over }: DragEndEvent) {
    const taskId = String(active.id)
    setActiveId(null)
    setOverStatus(null)
    if (!over) {
      setColumns(group(tasks))
      return
    }

    // Reordenar dentro de la misma columna.
    let final = columns
    const status = findColumn(columns, taskId)!
    const overStatus = findColumn(columns, String(over.id))
    if (status === overStatus && over.id !== active.id && !isStatus(String(over.id))) {
      const col = columns[status]
      final = {
        ...columns,
        [status]: arrayMove(col, col.findIndex((t) => t.id === taskId), col.findIndex((t) => t.id === over.id)),
      }
      setColumns(final)
    }

    const target = placement(final, taskId)
    if (origin.current && origin.current.status === target.status && origin.current.afterTaskId === target.afterTaskId) return

    move.mutate({
      taskId,
      status: target.status,
      afterTaskId: target.afterTaskId,
      optimisticBoard: STATUSES.flatMap((s) => final[s.value]),
    })
  }

  const active = activeId ? tasks.find((t) => t.id === activeId) : undefined

  return (
    <DndContext
      sensors={sensors}
      collisionDetection={closestCorners}
      onDragStart={onDragStart}
      onDragOver={onDragOver}
      onDragEnd={onDragEnd}
      onDragCancel={() => {
        setActiveId(null)
        setOverStatus(null)
        setColumns(group(tasks))
      }}
    >
      {move.isError && (
        <p role="alert" className="mb-2 animate-shake text-sm text-hot">
          No se pudo mover la tarea: {move.error.message}. Se restauró el board.
        </p>
      )}
      <div className="stagger grid gap-3 md:grid-cols-2 xl:grid-cols-4">
        {STATUSES.map(({ value, label }) => (
          <Column key={value} status={value} label={label} tasks={columns[value]} highlighted={overStatus === value}>
            {columns[value].map((task) => (
              <SortableCard key={task.id} task={task} disabled={!canWrite} onOpen={onOpen}>
                <CardBody task={task} keyPrefix={keyPrefix} membersById={membersById} labelsById={labelsById} />
              </SortableCard>
            ))}
          </Column>
        ))}
      </div>
      <DragOverlay>
        {active && (
          <div className="scale-105 rotate-2 cursor-grabbing rounded-md border border-neon bg-raised p-3 shadow-neon">
            <CardBody task={active} keyPrefix={keyPrefix} membersById={membersById} labelsById={labelsById} />
          </div>
        )}
      </DragOverlay>
    </DndContext>
  )
}

function Column({
  status,
  label,
  tasks,
  highlighted,
  children,
}: {
  status: TaskStatus
  label: string
  tasks: Task[]
  highlighted: boolean
  children: React.ReactNode
}) {
  // Droppable propio: sin él no se puede soltar en una columna vacía.
  const { setNodeRef } = useDroppable({ id: status })
  return (
    <section
      ref={setNodeRef}
      aria-label={label}
      className={cx(
        'flex min-h-48 flex-col rounded-lg border bg-panel/70 p-2 backdrop-blur-sm transition duration-200',
        // Al arrastrar una tarjeta encima, la columna "se enciende".
        highlighted ? 'animate-glow border-neon bg-neon/5' : 'border-line',
      )}
    >
      <h2 className="mb-2 flex items-center justify-between px-1 font-display text-xs font-semibold tracking-widest text-mint uppercase">
        {label}
        <span key={tasks.length} className="animate-pop rounded-full bg-raised px-2 py-0.5 text-xs font-normal tracking-normal text-ink">{tasks.length}</span>
      </h2>
      <SortableContext items={tasks.map((t) => t.id)} strategy={verticalListSortingStrategy}>
        <div className="flex flex-1 flex-col gap-2">
          {children}
          {tasks.length === 0 && <Empty>Sin tareas</Empty>}
        </div>
      </SortableContext>
    </section>
  )
}

function SortableCard({ task, disabled, onOpen, children }: { task: Task; disabled: boolean; onOpen: (id: string) => void; children: React.ReactNode }) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({ id: task.id, disabled })
  return (
    <article
      ref={setNodeRef}
      style={{ transform: CSS.Transform.toString(transform), transition }}
      {...attributes}
      // Sin arrastre (Viewer) la tarjeta sigue siendo un botón que abre el detalle: dnd-kit la marcaría
      // aria-disabled y un lector de pantalla la anunciaría como inactiva.
      aria-disabled={undefined}
      aria-roledescription={disabled ? undefined : attributes['aria-roledescription']}
      {...listeners}
      onClick={() => onOpen(task.id)}
      onKeyDown={(e) => {
        // Enter abre; espacio queda para el KeyboardSensor (agarrar/soltar).
        if (e.key === 'Enter') {
          // Sin preventDefault: el detalle se abre y enfoca "Cerrar" DURANTE este keydown, y el keypress
          // que sigue cae sobre ese botón y lo "clickea": el detalle se cerraba solo.
          e.preventDefault()
          onOpen(task.id)
        } else listeners?.onKeyDown?.(e)
      }}
      data-testid="task-card"
      aria-label={task.title}
      className={cx(
        'animate-rise cursor-grab rounded-md border border-line bg-raised/80 p-3 shadow-card transition duration-200',
        'hover:-translate-y-0.5 hover:border-neon hover:shadow-neon focus-visible:border-neon focus-visible:shadow-neon focus-visible:outline-none',
        disabled && 'cursor-pointer',
        // El hueco que deja la tarjeta mientras se arrastra: contorno punteado.
        isDragging && 'border-dashed border-violet opacity-40 shadow-none',
      )}
    >
      {children}
    </article>
  )
}

function CardBody({
  task,
  keyPrefix,
  membersById,
  labelsById,
}: {
  task: Task
  keyPrefix: string
  membersById: Map<string, Member>
  labelsById: Map<string, Label>
}) {
  const assignee = task.assigneeId ? membersById.get(task.assigneeId) : undefined
  const overdue = task.dueAt && task.status !== 'Done' && new Date(task.dueAt) < new Date()
  return (
    <>
      <div className="flex items-center justify-between gap-2">
        <span className="font-display text-xs text-violet">{keyPrefix}</span>
        <PriorityBadge priority={task.priority} />
      </div>
      <h3 className="mt-1 text-sm font-medium text-ink">{task.title}</h3>
      {task.labelIds.length > 0 && (
        <div className="mt-2 flex flex-wrap gap-1">
          {task.labelIds.map((id) => labelsById.get(id)).filter(Boolean).map((l) => <LabelChip key={l!.id} label={l!} />)}
        </div>
      )}
      {(assignee || task.dueAt) && (
        <div className="mt-2 flex items-center justify-between text-xs text-dim">
          {task.dueAt ? <span className={cx(overdue && 'font-medium text-hot')}>Vence {formatDate(task.dueAt)}</span> : <span />}
          {assignee && <Avatar name={assignee.displayName} />}
        </div>
      )}
    </>
  )
}
