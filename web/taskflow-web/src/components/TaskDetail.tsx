import { useEffect, useRef, useState, type FormEvent, type KeyboardEvent as ReactKeyboardEvent, type ReactNode } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { request } from '../api/client'
import {
  useAddComment,
  useAssignTask,
  useComments,
  useCreateLabel,
  useDeleteComment,
  useDeleteTask,
  useLabels,
  useMembers,
  useProjects,
  useSetTaskLabels,
  useTask,
  useUpdateTask,
} from '../api/queries'
import { PRIORITIES, STATUSES, type Task, type TaskPriority, type TaskStatus, type User } from '../api/types'
import { ActivityList } from './ActivityFeed'
import { Icon, type IconName } from './icons'
import { toast, useErrorToast } from './toast'
import { Avatar, Button, cx, ErrorText, formatDateTime, Input, LabelChip, SectionTitle, Select, Skeleton, StatusPill, Textarea, timeAgo } from './ui'

interface Props {
  taskId: string
  user: User
  canWrite: boolean
  isAdmin: boolean
  onClose: () => void
}

type SaveState = 'idle' | 'saving' | 'saved'

const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'

export function TaskDetail({ taskId, user, canWrite, isAdmin, onClose }: Props) {
  const { data: task, isLoading, error } = useTask(taskId)
  const { data: projects = [] } = useProjects()
  const closeRef = useRef<HTMLButtonElement>(null)
  const [saveState, setSaveState] = useState<SaveState>('idle')
  const project = task && projects.find((p) => p.id === task.projectId)

  useEffect(() => {
    closeRef.current?.focus()
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', onKey)
    // El fondo no se desplaza mientras el panel está abierto.
    const { overflow } = document.body.style
    document.body.style.overflow = 'hidden'
    return () => {
      window.removeEventListener('keydown', onKey)
      document.body.style.overflow = overflow
    }
  }, [onClose])

  // Es un diálogo modal: Tab da la vuelta adentro en vez de escaparse a la página de atrás.
  function trapFocus(e: ReactKeyboardEvent<HTMLElement>) {
    if (e.key !== 'Tab') return
    const focusable = e.currentTarget.querySelectorAll<HTMLElement>(FOCUSABLE)
    const first = focusable[0]
    const last = focusable[focusable.length - 1]
    if (e.shiftKey && document.activeElement === first) {
      e.preventDefault()
      last.focus()
    } else if (!e.shiftKey && document.activeElement === last) {
      e.preventDefault()
      first.focus()
    }
  }

  return (
    <div className="fixed inset-0 z-40 flex animate-fade justify-end bg-void/70 backdrop-blur-sm" onClick={onClose}>
      <aside
        role="dialog"
        aria-modal="true"
        aria-label="Detalle de la tarea"
        onClick={(e) => e.stopPropagation()}
        onKeyDown={trapFocus}
        className="flex h-dvh w-full max-w-xl animate-drawer flex-col border-neon/40 bg-panel shadow-violet sm:border-l"
      >
        <header className="flex shrink-0 items-center gap-2 border-b border-line bg-raised/30 py-2.5 pr-2 pl-5">
          {task ? (
            <>
              {project && <span className="font-display text-xs font-semibold tracking-wider text-violet">{project.keyPrefix}</span>}
              <StatusPill status={task.status} label={STATUSES.find((s) => s.value === task.status)!.label} />
            </>
          ) : (
            <span className="font-display text-xs tracking-widest text-mint uppercase">Tarea</span>
          )}
          <span role="status" className="ml-auto inline-flex items-center gap-1 text-xs text-dim">
            {saveState === 'saving' && 'Guardando…'}
            {saveState === 'saved' && (
              <span className="inline-flex animate-fade items-center gap-1 text-neon">
                <Icon name="check" className="size-3.5" />
                Guardado
              </span>
            )}
          </span>
          <Button ref={closeRef} variant="ghost" onClick={onClose} aria-label="Cerrar" className="px-2">
            <Icon name="x" />
          </Button>
        </header>
        <div className="flex-1 overflow-y-auto overscroll-contain p-5">
          {isLoading && <DetailSkeleton />}
          <ErrorText error={error} />
          {task && <TaskForm task={task} user={user} canWrite={canWrite} isAdmin={isAdmin} onDeleted={onClose} onSaveState={setSaveState} />}
        </div>
      </aside>
    </div>
  )
}

const DetailSkeleton = () => (
  <div role="status" className="flex flex-col gap-5">
    <span className="sr-only">Cargando…</span>
    <Skeleton className="h-8 w-3/4" />
    <div className="grid grid-cols-2 gap-3">
      {[0, 1, 2, 3].map((i) => (
        <Skeleton key={i} className="h-14" />
      ))}
    </div>
    <Skeleton className="h-4 w-28" />
    <Skeleton className="h-24" />
    <Skeleton className="h-4 w-28" />
    <Skeleton className="h-16" />
  </div>
)

/** Una propiedad de la tarea: rótulo chico con ícono arriba, control abajo. */
const Property = ({ icon, label, children }: { icon: IconName; label: string; children: ReactNode }) => (
  <label className="flex min-w-0 flex-col gap-1">
    <span className="flex items-center gap-1.5 text-xs text-dim">
      <Icon name={icon} className="size-3.5" />
      {label}
    </span>
    {children}
  </label>
)

function TaskForm({
  task,
  user,
  canWrite,
  isAdmin,
  onDeleted,
  onSaveState,
}: {
  task: Task
  user: User
  canWrite: boolean
  isAdmin: boolean
  onDeleted: () => void
  onSaveState: (state: SaveState) => void
}) {
  const qc = useQueryClient()
  const update = useUpdateTask(task.id)
  const assign = useAssignTask(task.id)
  const setLabels = useSetTaskLabels(task.id)
  const remove = useDeleteTask()
  const { data: members = [] } = useMembers()
  const { data: labels = [] } = useLabels()
  const [title, setTitle] = useState(task.title)
  const [description, setDescription] = useState(task.description ?? '')
  const [moving, setMoving] = useState(false)
  const [confirmingDelete, setConfirmingDelete] = useState(false)

  // Un efecto por campo: con uno solo, guardar la descripción pisaba el título que se estaba
  // escribiendo en ese momento (y al revés) con el valor viejo del servidor.
  useEffect(() => setTitle(task.title), [task.title])
  useEffect(() => setDescription(task.description ?? ''), [task.description])

  useErrorToast(update.error ?? assign.error ?? setLabels.error, 'No se pudo guardar')
  useErrorToast(remove.error, 'No se pudo eliminar la tarea')

  // Los campos se guardan solos (al cambiar o al salir del campo). El encabezado lo cuenta:
  // "Guardando…" mientras hay un cambio en vuelo y "Guardado" un par de segundos después.
  const saving = update.isPending || assign.isPending || setLabels.isPending || moving
  const wasSaving = useRef(false)
  useEffect(() => {
    if (saving) {
      wasSaving.current = true
      onSaveState('saving')
      return
    }
    if (!wasSaving.current) return
    wasSaving.current = false
    onSaveState('saved')
    const timer = setTimeout(() => onSaveState('idle'), 2000)
    return () => clearTimeout(timer)
  }, [saving, onSaveState])

  const reporter = members.find((m) => m.userId === task.reporterId)

  // Cambiar el estado desde el detalle la deja primera en la columna destino (mismo endpoint que el drag).
  async function changeStatus(status: TaskStatus) {
    setMoving(true)
    try {
      await request<Task>(`/tasks/${task.id}/move`, { method: 'POST', json: { status, afterTaskId: null } })
      qc.invalidateQueries({ queryKey: ['tasks'] })
      qc.invalidateQueries({ queryKey: ['activity'] })
    } catch (err) {
      toast.error(err, 'No se pudo cambiar el estado')
    } finally {
      setMoving(false)
    }
  }

  const toggleLabel = (id: string) =>
    setLabels.mutate(task.labelIds.includes(id) ? task.labelIds.filter((l) => l !== id) : [...task.labelIds, id])

  return (
    <div className="stagger flex flex-col gap-6">
      {/* El título se lee como un encabezado; la caja del campo aparece al pasar el mouse o enfocar. */}
      <Input
        aria-label="Título de la tarea"
        bare
        className="-mx-2.5 rounded-md border border-transparent text-xl font-semibold transition duration-150 hover:border-line focus:border-neon focus:bg-void/70 focus:shadow-neon disabled:hover:border-transparent"
        value={title}
        disabled={!canWrite}
        maxLength={200}
        onChange={(e) => setTitle(e.target.value)}
        onBlur={() => title.trim() && title !== task.title && update.mutate({ title })}
        onKeyDown={(e) => e.key === 'Enter' && e.currentTarget.blur()}
      />

      <div className="grid grid-cols-2 gap-x-3 gap-y-4 rounded-xl border border-line/70 bg-void/30 p-3.5 text-sm">
        <Property icon="status" label="Estado">
          <Select value={task.status} disabled={!canWrite} onChange={(e) => changeStatus(e.target.value as TaskStatus)} aria-label="Estado">
            {STATUSES.map((s) => (
              <option key={s.value} value={s.value}>
                {s.label}
              </option>
            ))}
          </Select>
        </Property>
        <Property icon="flag" label="Prioridad">
          <Select
            value={task.priority}
            disabled={!canWrite}
            onChange={(e) => update.mutate({ priority: e.target.value as TaskPriority })}
            aria-label="Prioridad de la tarea"
          >
            {PRIORITIES.map((p) => (
              <option key={p.value} value={p.value}>
                {p.label}
              </option>
            ))}
          </Select>
        </Property>
        <Property icon="user" label="Asignada a">
          <Select
            value={task.assigneeId ?? ''}
            disabled={!canWrite}
            onChange={(e) => assign.mutate(e.target.value || null)}
            aria-label="Asignada a"
          >
            <option value="">Sin asignar</option>
            {members.map((m) => (
              <option key={m.userId} value={m.userId}>
                {m.displayName}
              </option>
            ))}
          </Select>
        </Property>
        <Property icon="calendar" label="Vencimiento">
          <Input
            type="date"
            aria-label="Vencimiento"
            disabled={!canWrite}
            value={task.dueAt ? task.dueAt.slice(0, 10) : ''}
            onChange={(e) =>
              update.mutate(e.target.value ? { dueAt: new Date(`${e.target.value}T00:00:00Z`).toISOString() } : { clearDueAt: true })
            }
          />
        </Property>
      </div>

      <section>
        <SectionTitle icon="tag">Etiquetas</SectionTitle>
        <div className="flex flex-wrap items-center gap-1.5">
          {labels.map((l) => (
            <LabelChip key={l.id} label={l} active={task.labelIds.includes(l.id)} onClick={canWrite ? () => toggleLabel(l.id) : undefined} />
          ))}
          {labels.length === 0 && <span className="text-xs text-dim">No hay etiquetas en este workspace.{canWrite && ' Crea la primera aquí abajo.'}</span>}
        </div>
        {canWrite && <NewLabelForm />}
      </section>

      <section>
        <SectionTitle icon="text">Descripción</SectionTitle>
        <Textarea
          aria-label="Descripción de la tarea"
          value={description}
          disabled={!canWrite}
          placeholder={canWrite ? 'Agrega detalles…' : 'Sin descripción'}
          // field-sizing: el campo crece con el texto en vez de mostrar un scroll interno.
          className="block w-full leading-relaxed [field-sizing:content]"
          onChange={(e) => setDescription(e.target.value)}
          onBlur={() => description !== (task.description ?? '') && update.mutate({ description })}
        />
      </section>

      <Comments taskId={task.id} user={user} canWrite={canWrite} isAdmin={isAdmin} />

      <section>
        <SectionTitle icon="activity">Historial</SectionTitle>
        <ActivityList entityId={task.id} compact />
      </section>

      <footer className="flex flex-wrap items-center justify-between gap-x-3 gap-y-2 border-t border-line/70 pt-4 text-xs text-dim">
        <p>
          Creada {formatDateTime(task.createdAt)}
          {reporter && ` por ${reporter.displayName}`} · actualizada {timeAgo(task.updatedAt)}
        </p>
        {canWrite &&
          (confirmingDelete ? (
            // Confirmación en el lugar, en dos pasos, en vez de un window.confirm del navegador.
            <span className="flex animate-fade items-center gap-1">
              <span className="mr-1 text-hot">¿Eliminar para siempre?</span>
              <Button
                variant="danger"
                autoFocus
                disabled={remove.isPending}
                onClick={() =>
                  remove.mutate(task.id, {
                    onSuccess: () => {
                      toast.success(`Tarea «${task.title}» eliminada`)
                      onDeleted()
                    },
                  })
                }
              >
                Sí, eliminar
              </Button>
              <Button variant="ghost" onClick={() => setConfirmingDelete(false)}>
                Cancelar
              </Button>
            </span>
          ) : (
            <Button variant="danger" onClick={() => setConfirmingDelete(true)}>
              <Icon name="trash" className="size-3.5" />
              Eliminar tarea
            </Button>
          ))}
      </footer>
    </div>
  )
}

function NewLabelForm() {
  const create = useCreateLabel()
  const [name, setName] = useState('')
  const [color, setColor] = useState('#9f5fff')
  useErrorToast(create.error, 'No se pudo crear la etiqueta')

  function submit(e: FormEvent) {
    e.preventDefault()
    create.mutate(
      { name, color },
      {
        onSuccess: (l) => {
          setName('')
          toast.success(`Etiqueta «${l.name}» creada`)
        },
      },
    )
  }

  return (
    <form onSubmit={submit} className="mt-3 flex items-center gap-2">
      <Input placeholder="Nueva etiqueta" value={name} onChange={(e) => setName(e.target.value)} maxLength={50} aria-label="Nombre de etiqueta" className="w-40" />
      <input
        type="color"
        value={color}
        onChange={(e) => setColor(e.target.value)}
        aria-label="Color"
        className="size-8 shrink-0 cursor-pointer rounded-md border border-line bg-void/70 p-0.5 transition duration-150 hover:border-violet focus-visible:outline-2 focus-visible:outline-neon"
      />
      <Button type="submit" variant="secondary" disabled={!name.trim() || create.isPending}>
        Crear
      </Button>
    </form>
  )
}

function Comments({ taskId, user, canWrite, isAdmin }: { taskId: string; user: User; canWrite: boolean; isAdmin: boolean }) {
  const { data: comments = [], isLoading } = useComments(taskId)
  const add = useAddComment(taskId)
  const remove = useDeleteComment(taskId)
  const [body, setBody] = useState('')
  useErrorToast(add.error, 'No se pudo comentar')
  useErrorToast(remove.error, 'No se pudo borrar el comentario')

  function send() {
    if (!body.trim() || add.isPending) return
    add.mutate(body, { onSuccess: () => setBody('') })
  }

  function submit(e: FormEvent) {
    e.preventDefault()
    send()
  }

  return (
    <section>
      <SectionTitle icon="message">
        Comentarios <span className="font-normal tracking-normal text-dim">· {comments.length}</span>
      </SectionTitle>
      {isLoading && <Skeleton className="h-14" />}
      {!isLoading && comments.length === 0 && <p className="text-sm text-dim">Todavía no hay comentarios.{canWrite && ' Sé la primera persona en escribir.'}</p>}
      <ul className="stagger flex flex-col gap-3">
        {comments.map((c) => (
          <li key={c.id} className="flex gap-2.5">
            <Avatar name={c.authorName} size="md" />
            <div className={cx('min-w-0 flex-1 rounded-lg rounded-tl-sm border bg-raised/50 px-3 py-2 text-sm', c.authorId === user.id ? 'border-violet/50' : 'border-line')}>
              <div className="flex items-center justify-between gap-2 text-xs text-dim">
                <span className="min-w-0 truncate">
                  <strong className="text-ink">{c.authorName}</strong> · <time dateTime={c.createdAt} title={formatDateTime(c.createdAt)}>{timeAgo(c.createdAt)}</time>
                  {c.editedAt && ' (editado)'}
                </span>
                {/* Mismo criterio que la API: el autor o un Admin/Owner. La API lo valida igual. */}
                {canWrite && (c.authorId === user.id || isAdmin) && (
                  <button type="button" className="rounded text-dim transition duration-150 hover:text-hot focus-visible:text-hot focus-visible:outline-2 focus-visible:outline-neon" onClick={() => remove.mutate(c.id)}>
                    Borrar
                  </button>
                )}
              </div>
              <p className="mt-1 leading-relaxed break-words whitespace-pre-wrap">{c.body}</p>
            </div>
          </li>
        ))}
      </ul>
      {canWrite && (
        <form onSubmit={submit} className="mt-3 flex gap-2.5">
          <Avatar name={user.displayName} size="md" />
          <div className="flex min-w-0 flex-1 flex-col gap-2">
            <Textarea
              placeholder="Escribe un comentario…"
              value={body}
              onChange={(e) => setBody(e.target.value)}
              // Ctrl/⌘ + Enter envía sin soltar el teclado.
              onKeyDown={(e) => e.key === 'Enter' && (e.ctrlKey || e.metaKey) && send()}
              aria-label="Comentario"
              className="min-h-16 [field-sizing:content]"
            />
            <div className="flex items-center gap-3">
              <Button type="submit" disabled={!body.trim() || add.isPending}>
                {add.isPending ? 'Enviando…' : 'Comentar'}
              </Button>
              <span className="hidden text-xs text-dim sm:inline">Ctrl + Enter para enviar</span>
            </div>
          </div>
        </form>
      )}
    </section>
  )
}
