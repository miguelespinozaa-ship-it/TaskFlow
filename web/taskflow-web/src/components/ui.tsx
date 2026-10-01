import type { ComponentProps, InputHTMLAttributes, ReactNode, SelectHTMLAttributes, TextareaHTMLAttributes } from 'react'
import type { Label, TaskPriority } from '../api/types'

const cx = (...classes: (string | false | null | undefined)[]) => classes.filter(Boolean).join(' ')

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
        'inline-flex items-center justify-center gap-1 rounded-md px-3 py-1.5 text-sm font-medium transition duration-150',
        'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-neon active:scale-95',
        'disabled:cursor-not-allowed disabled:opacity-50 disabled:shadow-none disabled:active:scale-100',
        variants[variant],
        className,
      )}
      {...props}
    />
  )
}

const field =
  'rounded-md border border-line bg-void/70 px-2.5 py-1.5 text-sm text-ink placeholder:text-dim/60 transition duration-150 ' +
  'hover:border-violet focus:border-neon focus:outline-none focus:shadow-neon disabled:cursor-not-allowed disabled:opacity-60 disabled:hover:border-line'

export const Input = ({ className, ...props }: InputHTMLAttributes<HTMLInputElement>) => (
  <input className={cx(field, className)} {...props} />
)

export const Select = ({ className, ...props }: SelectHTMLAttributes<HTMLSelectElement>) => (
  <select className={cx(field, className)} {...props} />
)

export const Textarea = ({ className, ...props }: TextareaHTMLAttributes<HTMLTextAreaElement>) => (
  <textarea className={cx(field, 'min-h-20', className)} {...props} />
)

const priorityStyles: Record<TaskPriority, string> = {
  Low: 'border-line text-dim',
  Medium: 'border-violet/60 bg-violet/15 text-violet',
  High: 'border-amber/60 bg-amber/10 text-amber',
  Urgent: 'border-hot/70 bg-hot/15 text-hot shadow-[0_0_10px_-2px_var(--color-hot)]',
}

const priorityLabels: Record<TaskPriority, string> = { Low: 'Baja', Medium: 'Media', High: 'Alta', Urgent: 'Urgente' }

export const PriorityBadge = ({ priority }: { priority: TaskPriority }) => (
  <span className={cx('rounded-full border px-2 py-0.5 text-xs font-medium', priorityStyles[priority])}>{priorityLabels[priority]}</span>
)

export function LabelChip({ label, onClick, active = true }: { label: Label; onClick?: () => void; active?: boolean }) {
  const className = cx(
    'inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-xs transition duration-150',
    active ? 'animate-pop border-transparent font-medium text-white' : 'border-line bg-transparent text-dim',
    onClick && 'cursor-pointer hover:scale-105 hover:border-violet',
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

export const ErrorText = ({ error }: { error: unknown }) =>
  error ? (
    <p role="alert" className="animate-shake text-sm text-hot">
      {error instanceof Error ? error.message : String(error)}
    </p>
  ) : null

export const Empty = ({ children }: { children: ReactNode }) => (
  <p className="animate-fade py-4 text-center text-sm text-dim">{children}</p>
)

export function Avatar({ name, size = 'sm' }: { name: string; size?: 'sm' | 'md' }) {
  const initials = name
    .split(/\s+/)
    .map((p) => p[0])
    .slice(0, 2)
    .join('')
    .toUpperCase()
  return (
    <span
      title={name}
      className={cx(
        'inline-flex shrink-0 items-center justify-center rounded-full bg-gradient-to-br from-violet to-neon font-display font-bold text-deep',
        size === 'sm' ? 'h-6 w-6 text-[10px]' : 'h-8 w-8 text-xs',
      )}
    >
      {initials}
    </span>
  )
}

export const formatDate = (iso: string) =>
  new Date(iso).toLocaleDateString('es', { day: 'numeric', month: 'short' })

export const formatDateTime = (iso: string) =>
  new Date(iso).toLocaleString('es', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' })

export { cx }
