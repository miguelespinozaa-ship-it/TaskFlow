import { useState, type FormEvent } from 'react'
import { useAddMember, useMembers } from '../api/queries'
import type { WorkspaceRole } from '../api/types'
import { Avatar, Button, ErrorText, formatDate, Input, Select, Spinner } from './ui'

const roleLabels: Record<WorkspaceRole, string> = { Owner: 'Owner', Admin: 'Admin', Member: 'Miembro', Viewer: 'Solo lectura' }

export function MembersView({ isAdmin }: { isAdmin: boolean }) {
  const { data: members = [], isLoading, error } = useMembers()
  const add = useAddMember()
  const [email, setEmail] = useState('')
  const [role, setRole] = useState<Exclude<WorkspaceRole, 'Owner'>>('Member')

  function submit(e: FormEvent) {
    e.preventDefault()
    add.mutate({ email, role }, { onSuccess: () => setEmail('') })
  }

  return (
    <div className="flex max-w-2xl flex-col gap-4">
      {isAdmin && (
        <form onSubmit={submit} className="flex flex-wrap items-center gap-2">
          <Input type="email" placeholder="email@empresa.com" value={email} onChange={(e) => setEmail(e.target.value)} aria-label="Email del miembro" required />
          <Select value={role} onChange={(e) => setRole(e.target.value as typeof role)} aria-label="Rol">
            <option value="Admin">Admin</option>
            <option value="Member">Miembro</option>
            <option value="Viewer">Solo lectura</option>
          </Select>
          <Button type="submit" disabled={add.isPending}>
            Agregar miembro
          </Button>
          <p className="w-full text-xs text-dim">La persona tiene que tener una cuenta. Las invitaciones por email llegan en la fase 4.</p>
          <ErrorText error={add.error} />
        </form>
      )}
      <ErrorText error={error} />
      {isLoading && <Spinner />}
      <ul className="stagger divide-y divide-line overflow-hidden rounded-lg border border-line bg-panel/80 backdrop-blur-sm">
        {members.map((m) => (
          <li key={m.userId} className="flex items-center gap-3 px-3 py-2 text-sm transition duration-150 hover:bg-raised">
            <Avatar name={m.displayName} size="md" />
            <div className="flex-1">
              <p className="font-medium">{m.displayName}</p>
              <p className="text-xs text-dim">{m.email}</p>
            </div>
            <span className="text-xs text-dim">desde {formatDate(m.joinedAt)}</span>
            <span className="w-24 text-right font-display text-xs font-medium text-mint">{roleLabels[m.role]}</span>
          </li>
        ))}
      </ul>
    </div>
  )
}
