import { useEffect, useRef, useState, type FormEvent } from 'react'
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
  useSetTaskLabels,
  useTask,
  useUpdateTask,
} from '../api/queries'
import { PRIORITIES, STATUSES, type Task, type TaskPriority, type TaskStatus, type User } from '../api/types'
import { ActivityList } from './ActivityFeed'
import { Avatar, Button, ErrorText, formatDateTime, Input, LabelChip, Select, Spinner, Textarea } from './ui'

interface Props {
  taskId: string
  user: User
  canWrite: boolean
  isAdmin: boolean
  onClose: () => void
}

export function TaskDetail({ taskId, user, canWrite, isAdmin, onClose }: Props) {
  const { data: task, isLoading, error } = useTask(taskId)
  const closeRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    closeRef.current?.focus()
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  return (
    <div className="fixed inset-0 z-40 flex justify-end bg-slate-900/40" onClick={onClose}>
      <aside
        role="dialog"
        aria-modal="true"
        aria-label="Detalle de la tarea"
        onClick={(e) => e.stopPropagation()}
        className="flex h-full w-full max-w-xl flex-col overflow-y-auto bg-white p-5 shadow-xl dark:bg-slate-900"
      >
        <div className="mb-3 flex items-center justify-between">
          <span className="text-xs uppercase tracking-wide text-slate-400">Tarea</span>
          <Button ref={closeRef} variant="ghost" onClick={onClose} aria-label="Cerrar">
            ✕
          </Button>
        </div>
        {isLoading && <Spinner />}
        <ErrorText error={error} />
        {task && <TaskForm task={task} user={user} canWrite={canWrite} isAdmin={isAdmin} onDeleted={onClose} />}
      </aside>
    </div>
  )
}

function TaskForm({ task, user, canWrite, isAdmin, onDeleted }: { task: Task; user: User; canWrite: boolean; isAdmin: boolean; onDeleted: () => void }) {
  const qc = useQueryClient()
  const update = useUpdateTask(task.id)
  const assign = useAssignTask(task.id)
  const setLabels = useSetTaskLabels(task.id)
  const remove = useDeleteTask()
  const { data: members = [] } = useMembers()
  const { data: labels = [] } = useLabels()
  const [title, setTitle] = useState(task.title)
  const [description, setDescription] = useState(task.description ?? '')
  const [statusError, setStatusError] = useState<unknown>()

  useEffect(() => {
    setTitle(task.title)
    setDescription(task.description ?? '')
  }, [task.title, task.description])

  const reporter = members.find((m) => m.userId === task.reporterId)

  // Cambiar el estado desde el detalle la deja primera en la columna destino (mismo endpoint que el drag).
  async function changeStatus(status: TaskStatus) {
    setStatusError(undefined)
    try {
      await request<Task>(`/tasks/${task.id}/move`, { method: 'POST', json: { status, afterTaskId: null } })
      qc.invalidateQueries({ queryKey: ['tasks'] })
      qc.invalidateQueries({ queryKey: ['activity'] })
    } catch (err) {
      setStatusError(err)
    }
  }

  const toggleLabel = (id: string) =>
    setLabels.mutate(task.labelIds.includes(id) ? task.labelIds.filter((l) => l !== id) : [...task.labelIds, id])

  return (
    <div className="flex flex-col gap-4">
      <Input
        aria-label="Título de la tarea"
        className="text-lg font-semibold"
        value={title}
        disabled={!canWrite}
        maxLength={200}
        onChange={(e) => setTitle(e.target.value)}
        onBlur={() => title.trim() && title !== task.title && update.mutate({ title })}
      />

      <div className="grid grid-cols-2 gap-3 text-sm">
        <label className="flex flex-col gap-1">
          <span className="text-slate-500">Estado</span>
          <Select value={task.status} disabled={!canWrite} onChange={(e) => changeStatus(e.target.value as TaskStatus)} aria-label="Estado">
            {STATUSES.map((s) => (
              <option key={s.value} value={s.value}>
                {s.label}
              </option>
            ))}
          </Select>
        </label>
        <label className="flex flex-col gap-1">
          <span className="text-slate-500">Prioridad</span>
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
        </label>
        <label className="flex flex-col gap-1">
          <span className="text-slate-500">Asignada a</span>
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
        </label>
        <label className="flex flex-col gap-1">
          <span className="text-slate-500">Vencimiento</span>
          <Input
            type="date"
            aria-label="Vencimiento"
            disabled={!canWrite}
            value={task.dueAt ? task.dueAt.slice(0, 10) : ''}
            onChange={(e) =>
              update.mutate(e.target.value ? { dueAt: new Date(`${e.target.value}T00:00:00Z`).toISOString() } : { clearDueAt: true })
            }
          />
        </label>
      </div>
      <ErrorText error={statusError ?? update.error ?? assign.error} />

      <section>
        <h3 className="mb-1 text-sm text-slate-500">Etiquetas</h3>
        <div className="flex flex-wrap items-center gap-1.5">
          {labels.map((l) => (
            <LabelChip key={l.id} label={l} active={task.labelIds.includes(l.id)} onClick={canWrite ? () => toggleLabel(l.id) : undefined} />
          ))}
          {labels.length === 0 && <span className="text-xs text-slate-400">No hay etiquetas en este workspace.</span>}
        </div>
        {canWrite && <NewLabelForm />}
        <ErrorText error={setLabels.error} />
      </section>

      <label className="flex flex-col gap-1 text-sm">
        <span className="text-slate-500">Descripción</span>
        <Textarea
          aria-label="Descripción de la tarea"
          value={description}
          disabled={!canWrite}
          placeholder={canWrite ? 'Agregá detalles…' : 'Sin descripción'}
          onChange={(e) => setDescription(e.target.value)}
          onBlur={() => description !== (task.description ?? '') && update.mutate({ description })}
        />
      </label>

      <p className="text-xs text-slate-400">
        Creada {formatDateTime(task.createdAt)}
        {reporter && ` por ${reporter.displayName}`} · actualizada {formatDateTime(task.updatedAt)}
      </p>

      {canWrite && (
        <div>
          <Button
            variant="danger"
            onClick={() => window.confirm('¿Eliminar esta tarea?') && remove.mutate(task.id, { onSuccess: onDeleted })}
          >
            Eliminar tarea
          </Button>
        </div>
      )}

      <Comments taskId={task.id} user={user} canWrite={canWrite} isAdmin={isAdmin} />

      <section>
        <h3 className="mb-2 text-sm font-semibold">Historial</h3>
        <ActivityList entityId={task.id} compact />
      </section>
    </div>
  )
}

function NewLabelForm() {
  const create = useCreateLabel()
  const [name, setName] = useState('')
  const [color, setColor] = useState('#6366f1')

  function submit(e: FormEvent) {
    e.preventDefault()
    create.mutate({ name, color }, { onSuccess: () => setName('') })
  }

  return (
    <form onSubmit={submit} className="mt-2 flex items-center gap-2">
      <Input placeholder="Nueva etiqueta" value={name} onChange={(e) => setName(e.target.value)} aria-label="Nombre de etiqueta" className="w-40" />
      <input type="color" value={color} onChange={(e) => setColor(e.target.value)} aria-label="Color" className="h-8 w-8 rounded" />
      <Button type="submit" variant="secondary" disabled={!name.trim()}>
        Crear
      </Button>
      <ErrorText error={create.error} />
    </form>
  )
}

function Comments({ taskId, user, canWrite, isAdmin }: { taskId: string; user: User; canWrite: boolean; isAdmin: boolean }) {
  const { data: comments = [], isLoading } = useComments(taskId)
  const add = useAddComment(taskId)
  const remove = useDeleteComment(taskId)
  const [body, setBody] = useState('')

  function submit(e: FormEvent) {
    e.preventDefault()
    add.mutate(body, { onSuccess: () => setBody('') })
  }

  return (
    <section>
      <h3 className="mb-2 text-sm font-semibold">Comentarios ({comments.length})</h3>
      {isLoading && <Spinner />}
      <ul className="flex flex-col gap-3">
        {comments.map((c) => (
          <li key={c.id} className="flex gap-2">
            <Avatar name={c.authorName} size="md" />
            <div className="flex-1 rounded-md bg-slate-50 p-2 text-sm dark:bg-slate-800">
              <div className="flex items-center justify-between text-xs text-slate-500">
                <span>
                  <strong className="text-slate-700 dark:text-slate-200">{c.authorName}</strong> · {formatDateTime(c.createdAt)}
                  {c.editedAt && ' (editado)'}
                </span>
                {/* Mismo criterio que la API: el autor o un Admin/Owner. La API lo valida igual. */}
                {canWrite && (c.authorId === user.id || isAdmin) && (
                  <button type="button" className="text-red-600 hover:underline" onClick={() => remove.mutate(c.id)}>
                    Borrar
                  </button>
                )}
              </div>
              <p className="mt-1 whitespace-pre-wrap">{c.body}</p>
            </div>
          </li>
        ))}
      </ul>
      {canWrite && (
        <form onSubmit={submit} className="mt-3 flex flex-col gap-2">
          <Textarea placeholder="Escribí un comentario…" value={body} onChange={(e) => setBody(e.target.value)} aria-label="Comentario" />
          <div>
            <Button type="submit" disabled={!body.trim() || add.isPending}>
              Comentar
            </Button>
          </div>
          <ErrorText error={add.error ?? remove.error} />
        </form>
      )}
    </section>
  )
}
