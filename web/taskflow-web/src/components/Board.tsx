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
        setColumns(group(tasks))
      }}
    >
      {move.isError && (
        <p role="alert" className="mb-2 text-sm text-red-600">
          No se pudo mover la tarea: {move.error.message}. Se restauró el board.
        </p>
      )}
      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
        {STATUSES.map(({ value, label }) => (
          <Column key={value} status={value} label={label} tasks={columns[value]}>
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
          <div className="rotate-2 rounded-md border border-indigo-300 bg-white p-3 shadow-lg dark:bg-slate-800">
            <CardBody task={active} keyPrefix={keyPrefix} membersById={membersById} labelsById={labelsById} />
          </div>
        )}
      </DragOverlay>
    </DndContext>
  )
}

function Column({ status, label, tasks, children }: { status: TaskStatus; label: string; tasks: Task[]; children: React.ReactNode }) {
  // Droppable propio: sin él no se puede soltar en una columna vacía.
  const { setNodeRef, isOver } = useDroppable({ id: status })
  return (
    <section
      ref={setNodeRef}
      aria-label={label}
      className={cx(
        'flex min-h-48 flex-col rounded-lg border bg-slate-50 p-2 dark:bg-slate-900/60',
        isOver ? 'border-indigo-400' : 'border-slate-200 dark:border-slate-700',
      )}
    >
      <h2 className="mb-2 flex items-center justify-between px-1 text-sm font-semibold text-slate-700 dark:text-slate-200">
        {label}
        <span className="rounded-full bg-slate-200 px-2 text-xs font-normal dark:bg-slate-700">{tasks.length}</span>
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
        'cursor-pointer rounded-md border border-slate-200 bg-white p-3 shadow-sm transition hover:border-indigo-300 focus:outline-none focus:ring-2 focus:ring-indigo-500 dark:border-slate-700 dark:bg-slate-800',
        isDragging && 'opacity-40',
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
        <span className="text-xs text-slate-400">{keyPrefix}</span>
        <PriorityBadge priority={task.priority} />
      </div>
      <h3 className="mt-1 text-sm font-medium text-slate-900 dark:text-slate-100">{task.title}</h3>
      {task.labelIds.length > 0 && (
        <div className="mt-2 flex flex-wrap gap-1">
          {task.labelIds.map((id) => labelsById.get(id)).filter(Boolean).map((l) => <LabelChip key={l!.id} label={l!} />)}
        </div>
      )}
      {(assignee || task.dueAt) && (
        <div className="mt-2 flex items-center justify-between text-xs text-slate-500">
          {task.dueAt ? <span className={cx(overdue && 'font-medium text-red-600')}>Vence {formatDate(task.dueAt)}</span> : <span />}
          {assignee && <Avatar name={assignee.displayName} />}
        </div>
      )}
    </>
  )
}
