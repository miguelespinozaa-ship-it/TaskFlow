import type { ComponentProps, InputHTMLAttributes, ReactNode, SelectHTMLAttributes, TextareaHTMLAttributes } from 'react'
import type { Label, TaskPriority, TaskStatus } from '../api/types'
import { cx } from './cx'
import { Icon, PriorityIcon, type IconName } from './icons'

type Variant = 'primary' | 'secondary' | 'ghost' | 'danger'

const variants: Record<Variant, string> = {
  primary: 'bg-neon font-semibold text-deep hover:shadow-neon hover:brightness-110',
  secondary: 'border border-line bg-raised/60 text-ink hover:border-violet hover:shadow-violet',
  ghost: 'text-dim hover:bg-raised hover:text-ink',
  danger: 'text-hot hover:bg-hot/15',
}

export function Button({ variant = 'primary', className, ...props }: ComponentProps<'button'> & { variant?: Variant }) {
  return (
    <button
      className={cx(
        'inline-flex shrink-0 items-center justify-center gap-1.5 rounded-md px-3 py-1.5 text-sm font-medium whitespace-nowrap transition duration-150',
        'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-neon active:scale-95',
        'disabled:cursor-not-allowed disabled:opacity-50 disabled:shadow-none disabled:active:scale-100',
        variants[variant],
        className,
      )}
      {...props}
    />
  )
}

const fieldBase = 'min-w-0 text-sm text-ink placeholder:text-dim/60 disabled:cursor-not-allowed'
const fieldBox =
  'rounded-md border border-line bg-void/70 py-1.5 transition duration-150 hover:border-violet focus:border-neon focus:outline-none focus:shadow-neon disabled:opacity-60 disabled:hover:border-line'
// `bare`: sin caja propia, para campos que viven dentro de un contenedor que ya dibuja borde y foco.
const fieldBare = 'bg-transparent py-1.5 focus:outline-none'

interface Bare {
  bare?: boolean
}

export const Input = ({ className, bare, ...props }: InputHTMLAttributes<HTMLInputElement> & Bare) => (
  <input className={cx(fieldBase, bare ? fieldBare : fieldBox, 'px-2.5', className)} {...props} />
)

export const Select = ({ className, bare, ...props }: SelectHTMLAttributes<HTMLSelectElement> & Bare) => (
  <select className={cx(fieldBase, bare ? fieldBare : fieldBox, 'select-chevron cursor-pointer pr-7 pl-2.5', className)} {...props} />
)

export const Textarea = ({ className, ...props }: TextareaHTMLAttributes<HTMLTextAreaElement>) => (
  <textarea className={cx(fieldBase, fieldBox, 'min-h-20 px-2.5', className)} {...props} />
)

const priorityStyles: Record<TaskPriority, string> = {
  Low: 'border-line text-dim',
  Medium: 'border-violet/60 bg-violet/15 text-violet',
  High: 'border-amber/60 bg-amber/10 text-amber',
  Urgent: 'border-hot/70 bg-hot/15 text-hot shadow-[0_0_10px_-2px_var(--color-hot)]',
}

const priorityLabels: Record<TaskPriority, string> = { Low: 'Baja', Medium: 'Media', High: 'Alta', Urgent: 'Urgente' }

export const PriorityBadge = ({ priority }: { priority: TaskPriority }) => (
  <span className={cx('inline-flex shrink-0 items-center gap-1 rounded-full border px-2 py-0.5 text-xs font-medium', priorityStyles[priority])}>
    <PriorityIcon priority={priority} />
    {priorityLabels[priority]}
  </span>
)

// Un color por estado, siempre el mismo en columnas, detalle y búsqueda.
export const statusStyles: Record<TaskStatus, { dot: string; bar: string; text: string; edge: string; pill: string }> = {
  Todo: { dot: 'bg-dim', bar: 'bg-line', text: 'text-dim', edge: 'border-t-dim/70', pill: 'border-line bg-raised/50 text-dim' },
  InProgress: { bar: 'bg-violet', dot: 'bg-violet shadow-[0_0_8px_var(--color-violet)]', text: 'text-violet', edge: 'border-t-violet', pill: 'border-violet/50 bg-violet/15 text-violet' },
  InReview: { bar: 'bg-amber', dot: 'bg-amber shadow-[0_0_8px_var(--color-amber)]', text: 'text-amber', edge: 'border-t-amber', pill: 'border-amber/50 bg-amber/10 text-amber' },
  Done: { bar: 'bg-neon', dot: 'bg-neon shadow-[0_0_8px_var(--color-neon)]', text: 'text-neon', edge: 'border-t-neon', pill: 'border-neon/40 bg-neon/10 text-neon' },
}

export const StatusPill = ({ status, label }: { status: TaskStatus; label: string }) => (
  <span className={cx('inline-flex shrink-0 items-center gap-1.5 rounded-full border px-2 py-0.5 text-xs font-medium', statusStyles[status].pill)}>
    <span className={cx('size-1.5 rounded-full', statusStyles[status].dot)} />
    {label}
  </span>
)

export function LabelChip({ label, onClick, active = true }: { label: Label; onClick?: () => void; active?: boolean }) {
  const className = cx(
    'inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-xs transition duration-150',
    active ? 'animate-pop border-transparent font-medium text-white' : 'border-line bg-transparent text-dim',
    onClick && 'cursor-pointer hover:scale-105 hover:border-violet focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-neon',
  )
  const style = active ? { backgroundColor: label.color, boxShadow: `0 0 12px -3px ${label.color}` } : undefined

  // Solo es un <button> cuando se puede activar. Como adorno (tarjetas, resultados de búsqueda) es un
  // <span>: un botón dentro de otro botón es HTML inválido.
  return onClick ? (
    <button type="button" onClick={onClick} aria-pressed={active} className={className} style={style}>
      {label.name}
    </button>
  ) : (
    <span className={className} style={style}>
      {label.name}
    </span>
  )
}

/** Barra de "escaneo" animada. El texto queda para lectores de pantalla (role=status). */
export const Spinner = ({ label = 'Cargando…' }: { label?: string }) => (
  <div role="status" className="flex flex-col items-center gap-2 py-8">
    <div className="h-1 w-40 overflow-hidden rounded-full bg-raised">
      <div className="h-full w-1/3 animate-scan rounded-full bg-neon shadow-neon" />
    </div>
    <p className="font-display text-xs tracking-widest text-dim uppercase">{label}</p>
  </div>
)

/** Bloque gris con brillo que ocupa el lugar del contenido mientras carga. */
export const Skeleton = ({ className }: { className?: string }) => <div aria-hidden="true" className={cx('skeleton rounded-md', className)} />

/** Lista de filas fantasma, para vistas en forma de lista (búsqueda, actividad, miembros). */
export const ListSkeleton = ({ rows = 5 }: { rows?: number }) => (
  <div role="status" className="divide-y divide-line/60 overflow-hidden rounded-xl border border-line bg-panel/60">
    <span className="sr-only">Cargando…</span>
    {Array.from({ length: rows }, (_, i) => (
      <div key={i} className="flex items-center gap-3 px-4 py-3">
        <Skeleton className="size-8 rounded-full" />
        <div className="flex flex-1 flex-col gap-1.5">
          <Skeleton className={cx('h-3', i % 2 ? 'w-1/2' : 'w-2/3')} />
          <Skeleton className="h-2.5 w-1/4" />
        </div>
        <Skeleton className="hidden h-5 w-16 rounded-full sm:block" />
      </div>
    ))}
  </div>
)

export const ErrorText = ({ error }: { error: unknown }) =>
  error ? (
    <p role="alert" className="flex animate-shake items-start gap-2 rounded-md border border-hot/50 bg-hot/10 px-3 py-2 text-sm text-hot">
      <Icon name="alert" className="mt-0.5" />
      <span className="min-w-0 break-words">{error instanceof Error ? error.message : String(error)}</span>
    </p>
  ) : null

/** Estado vacío: ícono, una línea que dice qué pasa y, opcional, qué hacer. */
export const Empty = ({ icon = 'inbox', title, children, compact = false }: { icon?: IconName; title: string; children?: ReactNode; compact?: boolean }) => (
  <div className={cx('flex animate-fade flex-col items-center text-center', compact ? 'gap-1.5 py-5' : 'gap-2 py-12')}>
    <span
      className={cx(
        'flex items-center justify-center rounded-full border border-dashed border-line bg-raised/30 text-violet',
        compact ? 'size-9' : 'size-14',
      )}
    >
      <Icon name={icon} className={compact ? 'size-4' : 'size-6'} />
    </span>
    <p className={cx('font-medium text-ink', compact ? 'text-sm' : 'text-base')}>{title}</p>
    {children && <p className="max-w-xs text-xs text-dim">{children}</p>}
  </div>
)

// El degradado sale del nombre: cada persona tiene siempre el mismo y se distingue de un vistazo.
const avatarTones = ['from-violet to-neon', 'from-mint to-violet', 'from-amber to-hot', 'from-neon to-mint', 'from-hot to-violet']

export function Avatar({ name, size = 'sm' }: { name: string; size?: 'sm' | 'md' }) {
  const initials = name
    .split(/\s+/)
    .map((p) => p[0])
    .slice(0, 2)
    .join('')
    .toUpperCase()
  const tone = avatarTones[[...name].reduce((sum, ch) => sum + ch.charCodeAt(0), 0) % avatarTones.length]
  return (
    <span
      title={name}
      className={cx(
        'inline-flex shrink-0 items-center justify-center rounded-full bg-gradient-to-br font-display font-bold text-deep ring-1 ring-void/60',
        tone,
        size === 'sm' ? 'size-6 text-[10px]' : 'size-8 text-xs',
      )}
    >
      {initials}
    </span>
  )
}

/** Título de sección: ícono + rótulo en mayúsculas + filete que llega hasta el borde. */
export const SectionTitle = ({ icon, children }: { icon: IconName; children: ReactNode }) => (
  <h3 className="mb-2.5 flex items-center gap-2 font-display text-xs font-semibold tracking-widest text-mint uppercase after:h-px after:flex-1 after:bg-line/70">
    <Icon name={icon} className="size-3.5" />
    {children}
  </h3>
)

export const formatDate = (iso: string) =>
  new Date(iso).toLocaleDateString('es', { day: 'numeric', month: 'short' })

export const formatDateTime = (iso: string) =>
  new Date(iso).toLocaleString('es', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' })

export function timeAgo(iso: string) {
  const minutes = Math.floor((Date.now() - new Date(iso).getTime()) / 60_000)
  if (minutes < 1) return 'recién'
  if (minutes < 60) return `hace ${minutes} min`
  if (minutes < 60 * 24) return `hace ${Math.floor(minutes / 60)} h`
  if (minutes < 60 * 24 * 7) return `hace ${Math.floor(minutes / (60 * 24))} d`
  return formatDate(iso)
}

const localDay = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`

export const isOverdue = (iso: string) => iso.slice(0, 10) < localDay(new Date())

/**
 * El vencimiento se guarda como medianoche UTC del día elegido. Se compara y se muestra como DÍA
 * (en UTC): con la zona local, en UTC-3 aparecía el día anterior y vencía el mismo día a las 21 h.
 */
export function dueInfo(iso: string, done: boolean) {
  const day = iso.slice(0, 10)
  const today = localDay(new Date())
  const tomorrow = localDay(new Date(Date.now() + 86_400_000))
  const date = new Date(iso).toLocaleDateString('es', { day: 'numeric', month: 'short', timeZone: 'UTC' })
  if (done) return { text: date, tone: 'text-dim' }
  if (isOverdue(iso)) return { text: `Venció ${date}`, tone: 'font-medium text-hot' }
  if (day === today) return { text: 'Vence hoy', tone: 'font-medium text-amber' }
  if (day === tomorrow) return { text: 'Vence mañana', tone: 'text-amber' }
  return { text: date, tone: 'text-dim' }
}

export { cx }
