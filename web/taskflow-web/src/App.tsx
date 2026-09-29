import { useCallback, useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { onSessionChange, refreshSession } from './api/client'
import { auth, useWorkspaces } from './api/queries'
import type { AuthResponse } from './api/types'
import { ActivityList } from './components/ActivityFeed'
import { AuthScreen } from './components/AuthScreen'
import { BoardView } from './components/BoardView'
import { MembersView } from './components/MembersView'
import { SearchView } from './components/SearchView'
import { TaskDetail } from './components/TaskDetail'
import { Button, cx, ErrorText, Select, Spinner } from './components/ui'

export default function App() {
  const qc = useQueryClient()
  const [session, setSession] = useState<AuthResponse | null>(null)
  const [booting, setBooting] = useState(true)

  const currentWorkspace = useRef<string | undefined>(undefined)

  useEffect(() => {
    const unsubscribe = onSessionChange((next) => {
      // Cambió el tenant (o se cerró la sesión): la cache tiene datos de OTRO workspace. Se descarta
      // entera para que nunca se pinte, ni por un instante, información del tenant anterior.
      if (currentWorkspace.current !== next?.workspace.id) qc.clear()
      currentWorkspace.current = next?.workspace.id
      setSession(next)
    })
    // Al cargar: si hay cookie de refresh válida, recuperamos la sesión sin pedir login.
    refreshSession().finally(() => setBooting(false))
    return () => {
      unsubscribe()
    }
  }, [qc])

  if (booting) return <Spinner />
  if (!session) return <AuthScreen />
  // key: al cambiar de workspace se remonta todo con los datos del tenant nuevo.
  return <Shell key={session.workspace.id} session={session} />
}

type Tab = 'board' | 'search' | 'activity' | 'members'

const TABS: { id: Tab; label: string }[] = [
  { id: 'board', label: 'Board' },
  { id: 'search', label: 'Buscar' },
  { id: 'activity', label: 'Actividad' },
  { id: 'members', label: 'Miembros' },
]

function Shell({ session }: { session: AuthResponse }) {
  const [tab, setTab] = useState<Tab>('board')
  const [openTaskId, setOpenTaskId] = useState<string | null>(null)
  const [switchError, setSwitchError] = useState<unknown>()
  const { data: workspaces } = useWorkspaces()
  const role = session.workspace.role
  const canWrite = role !== 'Viewer' // la UI solo oculta; quien decide es la API (403)
  const isAdmin = role === 'Owner' || role === 'Admin'
  const closeTask = useCallback(() => setOpenTaskId(null), [])

  return (
    <div className="min-h-screen">
      <header className="border-b border-slate-200 bg-white dark:border-slate-700 dark:bg-slate-900">
        <div className="mx-auto flex max-w-7xl flex-wrap items-center gap-3 px-4 py-3">
          <h1 className="text-lg font-bold">TaskFlow</h1>
          <nav className="flex gap-1" aria-label="Secciones">
            {TABS.map((t) => (
              <button
                key={t.id}
                type="button"
                onClick={() => setTab(t.id)}
                aria-current={tab === t.id ? 'page' : undefined}
                className={cx(
                  'rounded-md px-3 py-1.5 text-sm',
                  tab === t.id
                    ? 'bg-indigo-50 font-medium text-indigo-700 dark:bg-indigo-950 dark:text-indigo-200'
                    : 'text-slate-600 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-slate-800',
                )}
              >
                {t.label}
              </button>
            ))}
          </nav>
          <div className="ml-auto flex items-center gap-2">
            <Select
              value={session.workspace.id}
              onChange={(e) => auth.switchWorkspace(e.target.value).catch(setSwitchError)}
              aria-label="Workspace"
            >
              {(workspaces ?? [session.workspace]).map((w) => (
                <option key={w.id} value={w.id}>
                  {w.name} ({w.role})
                </option>
              ))}
            </Select>
            <span className="hidden text-sm text-slate-500 sm:inline">{session.user.displayName}</span>
            <Button variant="secondary" onClick={() => auth.logout()}>
              Salir
            </Button>
          </div>
        </div>
      </header>

      <main className="mx-auto max-w-7xl px-4 py-5">
        <ErrorText error={switchError} />
        {tab === 'board' && <BoardView canWrite={canWrite} onOpen={setOpenTaskId} />}
        {tab === 'search' && <SearchView onOpen={setOpenTaskId} />}
        {tab === 'activity' && <ActivityList />}
        {tab === 'members' && <MembersView isAdmin={isAdmin} />}
      </main>

      {openTaskId && (
        <TaskDetail taskId={openTaskId} user={session.user} canWrite={canWrite} isAdmin={isAdmin} onClose={closeTask} />
      )}
    </div>
  )
}
