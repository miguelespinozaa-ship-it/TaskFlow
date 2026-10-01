import { useEffect, useMemo, useRef, useState } from 'react'
import {
  closestCorners,
  DndContext,
  DragOverlay,
  KeyboardSensor,
  MouseSensor,
  TouchSensor,
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
import { Icon } from './icons'
import { useErrorToast } from './toast'
import { Avatar, cx, dueInfo, Empty, LabelChip, PriorityBadge, statusStyles, timeAgo } from './ui'

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
    useSensor(MouseSensor, { activationConstraint: { distance: 5 } }),
    // En pantallas táctiles el dedo también hace scroll: se agarra manteniendo apretado un instante.
    useSensor(TouchSensor, { activationConstraint: { delay: 220, tolerance: 6 } }),
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
  useErrorToast(move.error, 'No se pudo mover la tarea (se restauró el board)')

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
      {/* En móvil las columnas van en fila con scroll horizontal; desde md, en grilla. El snap se apaga
          mientras se arrastra para que no pelee con el auto-scroll de dnd-kit. */}
      <div
        className={cx(
          'stagger -mx-4 flex items-start gap-3 overflow-x-auto px-4 pb-3 md:mx-0 md:grid md:grid-cols-2 md:items-stretch md:overflow-visible md:px-0 md:pb-0 xl:grid-cols-4',
          !activeId && 'snap-x snap-mandatory scroll-px-4',
        )}
      >
        {STATUSES.map(({ value, label }) => (
          <Column key={value} status={value} label={label} tasks={columns[value]} highlighted={overStatus === value} dragging={!!activeId} canWrite={canWrite}>
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
          <div className={cx('scale-105 rotate-2 cursor-grabbing border-neon shadow-lift', cardBase, priorityEdge[active.priority])}>
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
  dragging,
  canWrite,
  children,
}: {
  status: TaskStatus
  label: string
  tasks: Task[]
  highlighted: boolean
  dragging: boolean
  canWrite: boolean
  children: React.ReactNode
}) {
  // Droppable propio: sin él no se puede soltar en una columna vacía.
  const { setNodeRef } = useDroppable({ id: status })
  const tone = statusStyles[status]
  return (
    <section
      ref={setNodeRef}
      aria-label={label}
      className={cx(
        'flex min-h-40 w-[82vw] max-w-sm shrink-0 snap-start flex-col rounded-xl border border-t-2 p-2 backdrop-blur-sm transition duration-200 md:min-h-[26rem] md:w-auto md:max-w-none',
        // Al arrastrar una tarjeta encima, la columna "se enciende".
        highlighted ? 'animate-glow border-neon bg-neon/5' : cx('border-line/70 bg-panel/45', tone.edge),
      )}
    >
      <h2 className="mb-2 flex items-center gap-2 px-1.5 pt-1 font-display text-xs font-semibold tracking-widest text-ink uppercase">
        <span className={cx('size-2 rounded-full', tone.dot)} />
        {label}
        <span key={tasks.length} className="ml-auto min-w-6 animate-pop rounded-full bg-raised px-2 py-0.5 text-center text-xs font-normal tracking-normal text-dim">{tasks.length}</span>
      </h2>
      <SortableContext items={tasks.map((t) => t.id)} strategy={verticalListSortingStrategy}>
        <div className="flex flex-1 flex-col gap-2">
          {children}
          {tasks.length === 0 && (
            <div className={cx('flex flex-1 items-center justify-center rounded-lg border border-dashed transition duration-200', dragging ? 'border-neon/50 bg-neon/5' : 'border-line/60')}>
              <Empty compact icon={status === 'Done' ? 'check' : 'inbox'} title="Sin tareas">
                {dragging ? 'Soltala acá' : canWrite && 'Arrastrá una tarjeta hasta acá'}
              </Empty>
            </div>
          )}
        </div>
      </SortableContext>
    </section>
  )
}

const cardBase = 'rounded-lg border border-l-[3px] bg-raised/80 p-3 shadow-card'

// El borde izquierdo de la tarjeta lleva el color de la prioridad: se lee la columna de un vistazo.
const priorityEdge: Record<Task['priority'], string> = {
  Low: 'border-l-line',
  Medium: 'border-l-violet',
  High: 'border-l-amber',
  Urgent: 'border-l-hot',
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
      data-task-id={task.id}
      aria-label={task.title}
      className={cx(
        cardBase,
        // touch-manipulation + select-none: mantener apretado agarra la tarjeta en vez de seleccionar texto.
        'animate-rise cursor-grab touch-manipulation border-line transition duration-200 select-none [-webkit-touch-callout:none]',
        'hover:-translate-y-0.5 hover:border-neon hover:shadow-lift focus-visible:border-neon focus-visible:shadow-lift focus-visible:outline-none',
        priorityEdge[task.priority],
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
  const done = task.status === 'Done'
  const due = task.dueAt ? dueInfo(task.dueAt, done) : undefined
  return (
    <>
      <div className="flex items-center justify-between gap-2">
        <span className="inline-flex items-center gap-1 font-display text-xs text-violet">
          {done && <Icon name="check" className="size-3.5 text-neon" />}
          {keyPrefix}
        </span>
        <PriorityBadge priority={task.priority} />
      </div>
      <h3 className={cx('mt-1.5 text-sm leading-snug font-medium break-words', done ? 'text-dim' : 'text-ink')}>{task.title}</h3>
      {task.labelIds.length > 0 && (
        <div className="mt-2 flex flex-wrap gap-1">
          {task.labelIds.map((id) => labelsById.get(id)).filter(Boolean).map((l) => <LabelChip key={l!.id} label={l!} />)}
        </div>
      )}
      <div className="mt-2.5 flex items-center gap-2.5 border-t border-line/50 pt-2 text-xs text-dim">
        {due ? (
          <span className={cx('inline-flex items-center gap-1', due.tone)}>
            <Icon name="calendar" className="size-3.5" />
            {due.text}
          </span>
        ) : (
          <span className="inline-flex items-center gap-1" title="Última actualización">
            <Icon name="clock" className="size-3.5" />
            {timeAgo(task.updatedAt)}
          </span>
        )}
        {task.description && (
          <span title="Tiene descripción">
            <Icon name="text" className="size-3.5" />
          </span>
        )}
        <span className="ml-auto">
          {assignee ? (
            <Avatar name={assignee.displayName} />
          ) : (
            <span title="Sin asignar" className="flex size-6 items-center justify-center rounded-full border border-dashed border-line text-dim/70">
              <Icon name="user" className="size-3" />
            </span>
          )}
        </span>
      </div>
    </>
  )
}
