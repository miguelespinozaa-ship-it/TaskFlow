import { useState, type FormEvent } from 'react'
import { useBoard, useCreateProject, useCreateTask, useLabels, useMembers, useProjects } from '../api/queries'
import { PRIORITIES, STATUSES, type Task, type TaskPriority } from '../api/types'
import { Board } from './Board'
import { Icon } from './icons'
import { toast, useErrorToast } from './toast'
import { Button, cx, Empty, ErrorText, Input, isOverdue, Select, Skeleton, statusStyles } from './ui'

interface Props {
  /** El proyecto elegido vive en el shell: se mantiene al pasar a la pestaña Commits y volver. */
  projectId: string | undefined
  onSelectProject: (id: string) => void
  canWrite: boolean
  onOpen: (taskId: string) => void
}

export function BoardView({ projectId: selected, onSelectProject: setSelected, canWrite, onOpen }: Props) {
  const projects = useProjects()
  const projectId = selected ?? projects.data?.[0]?.id
  const project = projects.data?.find((p) => p.id === projectId)
  const board = useBoard(projectId)
  const { data: members = [] } = useMembers()
  const { data: labels = [] } = useLabels()

  if (projects.isLoading) return <BoardSkeleton withToolbar />

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-3">
        {project && (
          <div className="flex min-w-0 items-center gap-2">
            <span className="hidden rounded-md border border-violet/50 bg-violet/15 px-2 py-1.5 font-display text-xs font-semibold tracking-wider text-violet sm:block">
              {project.keyPrefix}
            </span>
            <Select
              value={projectId}
              onChange={(e) => setSelected(e.target.value)}
              aria-label="Proyecto"
              className="max-w-[60vw] truncate text-base font-semibold sm:max-w-xs"
            >
              {projects.data!.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.keyPrefix} · {p.name}
                </option>
              ))}
            </Select>
          </div>
        )}
        {project && board.data && <BoardStats tasks={board.data} />}
        {!canWrite && (
          <span className="inline-flex items-center gap-1.5 rounded-full border border-violet/60 bg-violet/15 px-2.5 py-1 text-xs text-violet">
            <Icon name="eye" className="size-3.5" />
            Solo lectura (Viewer)
          </span>
        )}
        {canWrite && <CreateProjectForm onCreated={setSelected} />}
      </div>
      <ErrorText error={projects.error} />

      {project ? (
        <>
          {canWrite && <CreateTaskForm projectId={project.id} />}
          <ErrorText error={board.error} />
          {board.isLoading ? (
            <BoardSkeleton />
          ) : (
            <Board
              projectId={project.id}
              keyPrefix={project.keyPrefix}
              tasks={board.data ?? []}
              members={members}
              labels={labels}
              canWrite={canWrite}
              onOpen={onOpen}
            />
          )}
        </>
      ) : (
        !projects.error && (
          <div className="rounded-xl border border-dashed border-line bg-panel/40">
            <Empty icon="folder" title="Este workspace no tiene proyectos todavía">
              {canWrite
                ? 'Creá el primero arriba: un nombre y un prefijo corto (por ejemplo WEB) que identifica a sus tareas.'
                : 'Cuando alguien del equipo cree uno, lo vas a ver acá.'}
            </Empty>
          </div>
        )
      )}
    </div>
  )
}

/** Resumen del proyecto: una barra partida por estado y el conteo de vencidas. */
function BoardStats({ tasks }: { tasks: Task[] }) {
  if (tasks.length === 0) return null
  const done = tasks.filter((t) => t.status === 'Done').length
  const overdue = tasks.filter((t) => t.dueAt && t.status !== 'Done' && isOverdue(t.dueAt)).length
  return (
    <div className="flex items-center gap-3 text-xs text-dim">
      <div className="flex h-1.5 w-28 overflow-hidden rounded-full bg-raised sm:w-40" aria-hidden="true">
        {STATUSES.map(({ value }) => {
          const count = tasks.filter((t) => t.status === value).length
          return count ? <span key={value} style={{ flexGrow: count }} className={cx('transition-[flex-grow] duration-300', statusStyles[value].bar)} /> : null
        })}
      </div>
      <span>
        <strong className="font-semibold text-ink">{done}</strong> de {tasks.length} {tasks.length === 1 ? 'hecha' : 'hechas'}
      </span>
      {overdue > 0 && (
        <span className="inline-flex items-center gap-1 font-medium text-hot">
          <Icon name="alert" className="size-3.5" />
          {overdue} {overdue === 1 ? 'vencida' : 'vencidas'}
        </span>
      )}
    </div>
  )
}

function CreateProjectForm({ onCreated }: { onCreated: (id: string) => void }) {
  const create = useCreateProject()
  const [name, setName] = useState('')
  const [keyPrefix, setKeyPrefix] = useState('')
  useErrorToast(create.error, 'No se pudo crear el proyecto')

  function submit(e: FormEvent) {
    e.preventDefault()
    create.mutate(
      { name, keyPrefix },
      {
        onSuccess: (p) => {
          onCreated(p.id)
          setName('')
          setKeyPrefix('')
          toast.success(`Proyecto «${p.name}» creado`)
        },
      },
    )
  }

  // Secundario a propósito: un solo grupo compacto, atenuado hasta que se usa, al final de la barra.
  return (
    <form
      onSubmit={submit}
      className="ml-auto flex w-full items-center rounded-lg border border-line/70 bg-panel/40 pl-2.5 text-dim opacity-80 transition duration-150 focus-within:border-violet focus-within:opacity-100 hover:opacity-100 sm:w-auto"
    >
      <Icon name="folder" className="size-3.5" />
      <Input bare placeholder="Nuevo proyecto" value={name} onChange={(e) => setName(e.target.value)} aria-label="Nombre del proyecto" className="flex-1 sm:w-36 sm:flex-none" />
      <Input
        bare
        placeholder="PREFIJO"
        value={keyPrefix}
        onChange={(e) => setKeyPrefix(e.target.value.toUpperCase().replace(/[^A-Z]/g, ''))}
        maxLength={10}
        className="w-24 border-l border-line/70 font-display text-xs"
        aria-label="Prefijo"
      />
      <Button type="submit" variant="ghost" className="rounded-l-none border-l border-line/70" disabled={!name.trim() || !keyPrefix || create.isPending}>
        + Proyecto
      </Button>
    </form>
  )
}

function CreateTaskForm({ projectId }: { projectId: string }) {
  const create = useCreateTask(projectId)
  const [title, setTitle] = useState('')
  const [priority, setPriority] = useState<TaskPriority>('Medium')
  useErrorToast(create.error, 'No se pudo crear la tarea')

  function submit(e: FormEvent) {
    e.preventDefault()
    if (!title.trim()) return
    create.mutate(
      { title, priority },
      {
        onSuccess: (t) => {
          setTitle('')
          toast.success(`Tarea «${t.title}» creada en Por hacer`)
        },
      },
    )
  }

  // Una sola barra (título + prioridad + botón) en vez de tres controles sueltos.
  return (
    <form
      onSubmit={submit}
      className="flex items-center gap-1 rounded-xl border border-line bg-panel/70 py-1 pr-1 pl-3 backdrop-blur-sm transition duration-150 focus-within:border-neon focus-within:shadow-neon hover:border-violet focus-within:hover:border-neon"
    >
      <Icon name="plus" className="text-neon" />
      <Input
        bare
        placeholder="Nueva tarea…"
        value={title}
        onChange={(e) => setTitle(e.target.value)}
        maxLength={200}
        aria-label="Título"
        className="flex-1"
      />
      <Select bare value={priority} onChange={(e) => setPriority(e.target.value as TaskPriority)} aria-label="Prioridad" className="rounded-md text-dim hover:bg-raised hover:text-ink">
        {PRIORITIES.map((p) => (
          <option key={p.value} value={p.value}>
            {p.label}
          </option>
        ))}
      </Select>
      <Button type="submit" disabled={!title.trim() || create.isPending}>
        {create.isPending ? 'Creando…' : 'Crear'}
      </Button>
    </form>
  )
}

/** Mismo esqueleto que el board real (4 columnas con tarjetas) para que al cargar no salte el layout. */
function BoardSkeleton({ withToolbar = false }: { withToolbar?: boolean }) {
  return (
    <div role="status" className="flex flex-col gap-4">
      <span className="sr-only">Cargando tareas…</span>
      {withToolbar && (
        <>
          <Skeleton className="h-9 w-56" />
          <Skeleton className="h-11 w-full rounded-xl" />
        </>
      )}
      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
        {[3, 2, 1, 2].map((cards, i) => (
          <div key={i} className={cx('flex-col gap-2 rounded-xl border border-line/60 bg-panel/40 p-2.5', i > 0 ? 'hidden md:flex' : 'flex')}>
            <Skeleton className="mb-1 h-4 w-24" />
            {Array.from({ length: cards }, (_, j) => (
              <div key={j} className="flex flex-col gap-2 rounded-lg border border-line/50 bg-raised/40 p-3">
                <Skeleton className="h-2.5 w-12" />
                <Skeleton className={cx('h-3.5', j % 2 ? 'w-2/3' : 'w-5/6')} />
                <div className="mt-1 flex items-center justify-between">
                  <Skeleton className="h-2.5 w-16" />
                  <Skeleton className="size-6 rounded-full" />
                </div>
              </div>
            ))}
          </div>
        ))}
      </div>
    </div>
  )
}
