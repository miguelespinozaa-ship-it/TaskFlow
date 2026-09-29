import { useState, type FormEvent } from 'react'
import { useBoard, useCreateProject, useCreateTask, useLabels, useMembers, useProjects } from '../api/queries'
import { PRIORITIES, type TaskPriority } from '../api/types'
import { Board } from './Board'
import { Button, Empty, ErrorText, Input, Select, Spinner } from './ui'

export function BoardView({ canWrite, onOpen }: { canWrite: boolean; onOpen: (taskId: string) => void }) {
  const projects = useProjects()
  const [selected, setSelected] = useState<string>()
  const projectId = selected ?? projects.data?.[0]?.id
  const project = projects.data?.find((p) => p.id === projectId)
  const board = useBoard(projectId)
  const { data: members = [] } = useMembers()
  const { data: labels = [] } = useLabels()

  if (projects.isLoading) return <Spinner />

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-3">
        {projects.data && projects.data.length > 0 && (
          <Select value={projectId} onChange={(e) => setSelected(e.target.value)} aria-label="Proyecto">
            {projects.data.map((p) => (
              <option key={p.id} value={p.id}>
                {p.keyPrefix} · {p.name}
              </option>
            ))}
          </Select>
        )}
        {canWrite && <CreateProjectForm onCreated={setSelected} />}
        {!canWrite && (
          <span className="rounded-full bg-slate-200 px-2 py-0.5 text-xs text-slate-600 dark:bg-slate-700 dark:text-slate-300">
            Solo lectura (Viewer)
          </span>
        )}
      </div>
      <ErrorText error={projects.error} />

      {project ? (
        <>
          {canWrite && <CreateTaskForm projectId={project.id} />}
          <ErrorText error={board.error} />
          {board.isLoading ? (
            <Spinner label="Cargando tareas…" />
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
        <Empty>Este workspace no tiene proyectos todavía.{canWrite && ' Creá uno arriba.'}</Empty>
      )}
    </div>
  )
}

function CreateProjectForm({ onCreated }: { onCreated: (id: string) => void }) {
  const create = useCreateProject()
  const [name, setName] = useState('')
  const [keyPrefix, setKeyPrefix] = useState('')

  function submit(e: FormEvent) {
    e.preventDefault()
    create.mutate(
      { name, keyPrefix },
      {
        onSuccess: (p) => {
          onCreated(p.id)
          setName('')
          setKeyPrefix('')
        },
      },
    )
  }

  return (
    <form onSubmit={submit} className="flex flex-wrap items-center gap-2">
      <Input placeholder="Nuevo proyecto" value={name} onChange={(e) => setName(e.target.value)} aria-label="Nombre del proyecto" />
      <Input
        placeholder="PREFIJO"
        value={keyPrefix}
        onChange={(e) => setKeyPrefix(e.target.value.toUpperCase().replace(/[^A-Z]/g, ''))}
        maxLength={10}
        className="w-28"
        aria-label="Prefijo"
      />
      <Button type="submit" variant="secondary" disabled={!name.trim() || !keyPrefix || create.isPending}>
        + Proyecto
      </Button>
      <ErrorText error={create.error} />
    </form>
  )
}

function CreateTaskForm({ projectId }: { projectId: string }) {
  const create = useCreateTask(projectId)
  const [title, setTitle] = useState('')
  const [priority, setPriority] = useState<TaskPriority>('Medium')

  function submit(e: FormEvent) {
    e.preventDefault()
    create.mutate({ title, priority }, { onSuccess: () => setTitle('') })
  }

  return (
    <form onSubmit={submit} className="flex flex-wrap items-center gap-2">
      <Input
        placeholder="Nueva tarea…"
        value={title}
        onChange={(e) => setTitle(e.target.value)}
        maxLength={200}
        aria-label="Título"
        className="min-w-64 flex-1"
      />
      <Select value={priority} onChange={(e) => setPriority(e.target.value as TaskPriority)} aria-label="Prioridad">
        {PRIORITIES.map((p) => (
          <option key={p.value} value={p.value}>
            {p.label}
          </option>
        ))}
      </Select>
      <Button type="submit" disabled={create.isPending}>
        {create.isPending ? 'Creando…' : 'Crear'}
      </Button>
      <div className="w-full">
        <ErrorText error={create.error} />
      </div>
    </form>
  )
}
