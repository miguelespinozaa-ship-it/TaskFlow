import { useEffect, useState, type FormEvent } from 'react'
import { api, type Project, type Task, type TaskPriority, type TaskStatus } from './api'

const COLUMNS: { status: TaskStatus; label: string }[] = [
  { status: 'Todo', label: 'Por hacer' },
  { status: 'InProgress', label: 'En progreso' },
  { status: 'InReview', label: 'En revisión' },
  { status: 'Done', label: 'Hecho' },
]

const PRIORITIES: TaskPriority[] = ['Low', 'Medium', 'High', 'Urgent']

export default function App() {
  const [projects, setProjects] = useState<Project[]>([])
  const [projectId, setProjectId] = useState<string>()
  const [tasks, setTasks] = useState<Task[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string>()

  useEffect(() => {
    api
      .listProjects()
      .then((list) => {
        setProjects(list)
        setProjectId(list[0]?.id)
        if (list.length === 0) setLoading(false)
      })
      .catch((e: Error) => {
        setError(`No se pudo conectar con la API: ${e.message}`)
        setLoading(false)
      })
  }, [])

  useEffect(() => {
    if (!projectId) return
    setLoading(true)
    api
      .listTasks(projectId)
      .then(setTasks)
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false))
  }, [projectId])

  const project = projects.find((p) => p.id === projectId)

  return (
    <div className="app">
      <header className="topbar">
        <h1>TaskFlow</h1>
        {projects.length > 0 && (
          <select value={projectId} onChange={(e) => setProjectId(e.target.value)} aria-label="Proyecto">
            {projects.map((p) => (
              <option key={p.id} value={p.id}>
                {p.keyPrefix} · {p.name}
              </option>
            ))}
          </select>
        )}
      </header>

      {error && <p className="error" role="alert">{error}</p>}

      {project && (
        <>
          <CreateTaskForm
            projectId={project.id}
            onCreated={(task) => setTasks((prev) => [...prev, task])}
          />
          {loading ? (
            <p className="muted">Cargando tareas…</p>
          ) : (
            <Board tasks={tasks} keyPrefix={project.keyPrefix} />
          )}
        </>
      )}

      {!loading && !error && projects.length === 0 && (
        <p className="muted">No hay proyectos. Arrancá la API en modo Development para crear el seed de demo.</p>
      )}
    </div>
  )
}

function Board({ tasks, keyPrefix }: { tasks: Task[]; keyPrefix: string }) {
  return (
    <div className="board">
      {COLUMNS.map((col) => {
        const items = tasks.filter((t) => t.status === col.status).sort((a, b) => a.position - b.position)
        return (
          <section key={col.status} className="column">
            <h2>
              {col.label} <span className="count">{items.length}</span>
            </h2>
            {items.length === 0 && <p className="muted small">Sin tareas</p>}
            {items.map((t) => (
              <article key={t.id} className="card">
                <div className="card-head">
                  <span className="muted small">{keyPrefix}</span>
                  <span className={`badge badge-${t.priority.toLowerCase()}`}>{t.priority}</span>
                </div>
                <h3>{t.title}</h3>
                {t.description && <p className="small">{t.description}</p>}
              </article>
            ))}
          </section>
        )
      })}
    </div>
  )
}

function CreateTaskForm({ projectId, onCreated }: { projectId: string; onCreated: (t: Task) => void }) {
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [priority, setPriority] = useState<TaskPriority>('Medium')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string>()

  async function submit(e: FormEvent) {
    e.preventDefault()
    setSaving(true)
    setError(undefined)
    try {
      const task = await api.createTask(projectId, {
        title,
        description: description || undefined,
        priority,
      })
      onCreated(task)
      setTitle('')
      setDescription('')
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setSaving(false)
    }
  }

  return (
    <form className="create-form" onSubmit={submit}>
      <input
        placeholder="Nueva tarea…"
        value={title}
        onChange={(e) => setTitle(e.target.value)}
        maxLength={200}
        aria-label="Título"
      />
      <input
        placeholder="Descripción (opcional)"
        value={description}
        onChange={(e) => setDescription(e.target.value)}
        aria-label="Descripción"
      />
      <select value={priority} onChange={(e) => setPriority(e.target.value as TaskPriority)} aria-label="Prioridad">
        {PRIORITIES.map((p) => (
          <option key={p}>{p}</option>
        ))}
      </select>
      <button type="submit" disabled={saving}>
        {saving ? 'Creando…' : 'Crear'}
      </button>
      {error && <p className="error small" role="alert">{error}</p>}
    </form>
  )
}
