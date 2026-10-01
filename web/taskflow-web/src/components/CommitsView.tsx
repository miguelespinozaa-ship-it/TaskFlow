import { useState, type FormEvent } from 'react'
import { useCommits, useLinkRepository, useProjects, useRepository, useSyncRepository, useUnlinkRepository } from '../api/queries'
import type { Commit, FolderChange, Project, Repository } from '../api/types'
import { Icon } from './icons'
import { toast, useErrorToast } from './toast'
import { Avatar, Button, cx, Empty, ErrorText, formatDateTime, Input, ListSkeleton, Select, timeAgo } from './ui'

interface Props {
  projectId: string | undefined
  onSelectProject: (id: string) => void
  canWrite: boolean
  isAdmin: boolean
}

export function CommitsView({ projectId: selected, onSelectProject, canWrite, isAdmin }: Props) {
  const projects = useProjects()
  const projectId = selected ?? projects.data?.[0]?.id
  const project = projects.data?.find((p) => p.id === projectId)
  const repository = useRepository(project?.id)

  if (projects.isLoading) return <ListSkeleton rows={4} />
  if (!project)
    return (
      <Empty icon="folder" title="Este workspace no tiene proyectos todavía">
        Crea un proyecto en el Board para poder conectarle un repositorio.
      </Empty>
    )

  return (
    <div className="flex max-w-4xl flex-col gap-4">
      <Select
        value={project.id}
        onChange={(e) => onSelectProject(e.target.value)}
        aria-label="Proyecto"
        className="max-w-[80vw] self-start truncate text-base font-semibold sm:max-w-xs"
      >
        {projects.data!.map((p) => (
          <option key={p.id} value={p.id}>
            {p.keyPrefix} · {p.name}
          </option>
        ))}
      </Select>

      <ErrorText error={repository.error} />
      {repository.isLoading ? (
        <ListSkeleton rows={4} />
      ) : repository.data ? (
        // key: al cambiar de proyecto se descarta el estado local (confirmación de desconectar, etc.).
        <LinkedRepository key={project.id} repository={repository.data} canWrite={canWrite} isAdmin={isAdmin} />
      ) : (
        <ConnectRepository key={project.id} project={project} isAdmin={isAdmin} />
      )}
    </div>
  )
}

function ConnectRepository({ project, isAdmin }: { project: Project; isAdmin: boolean }) {
  const link = useLinkRepository(project.id)
  const [value, setValue] = useState('')
  useErrorToast(link.error, 'No se pudo conectar')

  function submit(e: FormEvent) {
    e.preventDefault()
    link.mutate(value, {
      onSuccess: ({ imported, repository }) =>
        repository.lastSyncError
          ? toast.error(repository.lastSyncError, `${repository.fullName} quedó conectado, pero no se pudieron traer los commits`)
          : toast.success(`${repository.fullName} conectado · ${plural(imported, 'commit importado', 'commits importados')}`),
    })
  }

  return (
    <div className="rounded-xl border border-dashed border-line bg-panel/60 p-6 backdrop-blur-sm">
      <Empty icon="commit" title={`«${project.name}» no tiene un repositorio conectado`} compact>
        {isAdmin
          ? 'Conecta un repositorio público de GitHub para ver aquí cada commit: qué carpetas tocó y la explicación de quien lo hizo.'
          : 'Un Admin u Owner del workspace puede conectar un repositorio de GitHub.'}
      </Empty>
      {isAdmin && (
        <form onSubmit={submit} className="mx-auto mt-2 flex max-w-lg flex-wrap items-center gap-2">
          <Input
            value={value}
            onChange={(e) => setValue(e.target.value)}
            placeholder="owner/repositorio o https://github.com/…"
            aria-label="Repositorio de GitHub"
            className="min-w-56 flex-1 font-display"
            autoComplete="off"
            spellCheck={false}
          />
          <Button type="submit" disabled={!value.trim() || link.isPending}>
            <Icon name="link" />
            {link.isPending ? 'Conectando…' : 'Conectar'}
          </Button>
        </form>
      )}
    </div>
  )
}

function LinkedRepository({ repository, canWrite, isAdmin }: { repository: Repository; canWrite: boolean; isAdmin: boolean }) {
  const sync = useSyncRepository(repository.projectId)
  const unlink = useUnlinkRepository(repository.projectId)
  const commits = useCommits(repository.projectId, true)
  const [confirming, setConfirming] = useState(false)
  useErrorToast(sync.error, 'No se pudo sincronizar')
  useErrorToast(unlink.error, 'No se pudo desconectar')

  const items = commits.data?.pages.flatMap((p) => p.items) ?? []

  return (
    <>
      <section
        aria-label="Repositorio conectado"
        className="flex flex-wrap items-center gap-x-4 gap-y-3 rounded-xl border border-line bg-panel/70 p-4 backdrop-blur-sm"
      >
        <div className="min-w-0 flex-1">
          <a
            href={repository.htmlUrl}
            target="_blank"
            rel="noreferrer noopener"
            className="group inline-flex max-w-full items-center gap-1.5 font-display text-base font-semibold text-ink hover:text-neon"
          >
            <span className="truncate">{repository.fullName}</span>
            <Icon name="external" className="size-3.5 text-dim group-hover:text-neon" />
          </a>
          <p className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-dim">
            <span className="inline-flex items-center gap-1 rounded-full border border-violet/50 bg-violet/15 px-2 py-0.5 font-display text-violet">
              <Icon name="branch" className="size-3" />
              {repository.defaultBranch}
            </span>
            <span title={repository.lastSyncedAt ? formatDateTime(repository.lastSyncedAt) : undefined}>
              {repository.lastSyncedAt ? `Sincronizado ${timeAgo(repository.lastSyncedAt)}` : 'Todavía no se sincronizó'}
            </span>
          </p>
        </div>

        <div className="flex items-center gap-2">
          {canWrite && (
            <Button
              variant="secondary"
              disabled={sync.isPending}
              onClick={() =>
                sync.mutate(undefined, {
                  onSuccess: ({ imported }) =>
                    toast.success(imported ? plural(imported, 'commit importado', 'commits importados') : 'Ya estaba al día'),
                })
              }
            >
              <Icon name="refresh" className={cx(sync.isPending && 'animate-spin')} />
              {sync.isPending ? 'Sincronizando…' : 'Sincronizar'}
            </Button>
          )}
          {isAdmin &&
            (confirming ? (
              <>
                <Button
                  variant="danger"
                  disabled={unlink.isPending}
                  onClick={() => unlink.mutate(undefined, { onSuccess: () => toast.success(`${repository.fullName} desconectado`) })}
                >
                  Sí, desconectar
                </Button>
                <Button variant="ghost" onClick={() => setConfirming(false)}>
                  Cancelar
                </Button>
              </>
            ) : (
              <Button variant="ghost" onClick={() => setConfirming(true)}>
                Desconectar
              </Button>
            ))}
        </div>

        {repository.lastSyncError && (
          <p role="status" className="flex w-full items-start gap-2 rounded-md border border-amber/40 bg-amber/10 px-3 py-2 text-xs text-amber">
            <Icon name="alert" className="mt-0.5 size-3.5" />
            <span>
              La última sincronización falló: {repository.lastSyncError} Los commits de abajo son los que ya estaban importados.
            </span>
          </p>
        )}
      </section>

      <ErrorText error={commits.error} />
      {commits.isLoading ? (
        <ListSkeleton rows={5} />
      ) : items.length === 0 ? (
        <Empty icon="commit" title="Todavía no hay commits importados">
          {repository.lastSyncError ? 'Prueba sincronizar de nuevo en unos minutos.' : 'Cuando haya commits en la rama, aparecerán aquí.'}
        </Empty>
      ) : (
        <ol className="stagger flex flex-col gap-3" aria-label="Commits">
          {items.map((c) => (
            <CommitCard key={c.id} commit={c} />
          ))}
        </ol>
      )}
      {commits.hasNextPage && (
        <div>
          <Button variant="secondary" onClick={() => commits.fetchNextPage()} disabled={commits.isFetchingNextPage}>
            {commits.isFetchingNextPage ? 'Cargando…' : 'Cargar más'}
          </Button>
        </div>
      )}
    </>
  )
}

function CommitCard({ commit }: { commit: Commit }) {
  const [expanded, setExpanded] = useState(false)
  const long = !!commit.body && (commit.body.length > 280 || commit.body.split('\n').length > 4)
  // La carpeta con más cambios marca el 100 % de la barra; las demás se dibujan en proporción.
  const max = Math.max(1, ...commit.folders.map((f) => f.additions + f.deletions))

  return (
    <li className="rounded-xl border border-line bg-panel/70 p-4 backdrop-blur-sm transition duration-200 hover:border-violet">
      <header className="flex items-start gap-3">
        <AuthorAvatar commit={commit} />
        <div className="min-w-0 flex-1">
          <h3 className="font-medium break-words text-ink">{commit.title}</h3>
          <p className="mt-0.5 flex flex-wrap items-center gap-x-2 gap-y-0.5 text-xs text-dim">
            <span className="text-ink/80">{commit.authorName}</span>
            <time dateTime={commit.committedAt} title={formatDateTime(commit.committedAt)}>
              {timeAgo(commit.committedAt)}
            </time>
            <a
              href={commit.htmlUrl}
              target="_blank"
              rel="noreferrer noopener"
              aria-label={`Ver el commit ${commit.sha.slice(0, 7)} en GitHub`}
              className="inline-flex items-center gap-1 rounded border border-line px-1.5 font-display text-mint hover:border-neon hover:text-neon"
            >
              <Icon name="commit" className="size-3" />
              {commit.sha.slice(0, 7)}
            </a>
          </p>
        </div>
        <p className="shrink-0 text-right font-display text-xs">
          <span className="text-neon">+{commit.additions}</span> <span className="text-hot">−{commit.deletions}</span>
          <span className="mt-0.5 block text-dim">{plural(commit.filesChanged, 'archivo', 'archivos')}</span>
        </p>
      </header>

      {commit.body && (
        <div className="mt-3 border-l-2 border-violet/60 pl-3">
          {/* whitespace-pre-wrap: respeta los saltos de línea y viñetas del mensaje. React escapa el texto. */}
          <p className={cx('text-sm break-words whitespace-pre-wrap text-ink/90', long && !expanded && 'line-clamp-4')}>{commit.body}</p>
          {long && (
            <button type="button" onClick={() => setExpanded(!expanded)} aria-expanded={expanded} className="mt-1 text-xs text-mint hover:underline">
              {expanded ? 'Ver menos' : 'Ver explicación completa'}
            </button>
          )}
        </div>
      )}

      {commit.folders.length > 0 && (
        <ul className="mt-3 flex flex-col gap-1.5" aria-label="Carpetas con cambios">
          {commit.folders.map((f) => (
            <FolderRow key={f.path} folder={f} max={max} />
          ))}
        </ul>
      )}
    </li>
  )
}

function FolderRow({ folder, max }: { folder: FolderChange; max: number }) {
  const total = folder.additions + folder.deletions
  // Mínimo visible: una carpeta con un cambio de 1 línea no desaparece al lado de una de 500.
  const width = Math.max(4, Math.round((total / max) * 100))
  const added = total ? (folder.additions / total) * 100 : 0

  return (
    <li className="grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-3 gap-y-1 text-xs sm:grid-cols-[minmax(0,14rem)_minmax(0,1fr)_auto]">
      <span className="flex min-w-0 items-center gap-1.5 font-display text-ink/90">
        <Icon name="folder" className="size-3.5 text-violet" />
        <span className="truncate" title={folder.path || 'raíz del repositorio'}>
          {folder.path || <em className="text-dim not-italic">(raíz)</em>}
        </span>
      </span>
      <span className="col-span-2 row-start-2 h-1.5 overflow-hidden rounded-full bg-raised sm:col-span-1 sm:row-start-auto" aria-hidden="true">
        <span className="flex h-full overflow-hidden rounded-full" style={{ width: `${width}%` }}>
          <span className="h-full bg-neon" style={{ width: `${added}%` }} />
          <span className="h-full flex-1 bg-hot" />
        </span>
      </span>
      <span className="font-display whitespace-nowrap text-dim">
        {plural(folder.files, 'archivo', 'archivos')} · <span className="text-neon">+{folder.additions}</span>{' '}
        <span className="text-hot">−{folder.deletions}</span>
      </span>
    </li>
  )
}

function AuthorAvatar({ commit }: { commit: Commit }) {
  const [failed, setFailed] = useState(false)
  if (!commit.authorAvatarUrl || failed) return <Avatar name={commit.authorName} size="md" />
  return (
    <img
      src={commit.authorAvatarUrl}
      alt=""
      width={32}
      height={32}
      loading="lazy"
      // GitHub no necesita saber desde qué página (ni qué workspace) se pide la imagen.
      referrerPolicy="no-referrer"
      onError={() => setFailed(true)}
      className="size-8 shrink-0 rounded-full border border-line"
    />
  )
}

const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`
