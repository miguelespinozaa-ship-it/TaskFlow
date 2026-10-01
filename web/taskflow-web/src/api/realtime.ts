import { useEffect } from 'react'
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { freshAccessToken } from './client'

/**
 * Mantiene abierta la conexión en tiempo real del workspace. Cuando alguien cambia algo, el servidor avisa
 * (sin datos) y se vuelven a pedir las consultas que estén en pantalla: lo que cada uno ve lo sigue
 * decidiendo la API con su propio token.
 */
export function useWorkspaceLive() {
  const qc = useQueryClient()

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/workspace', { accessTokenFactory: freshAccessToken })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    connection.on('WorkspaceChanged', () => qc.invalidateQueries())
    // Durante una desconexión se pudieron perder avisos: al volver se refresca todo una vez.
    connection.onreconnected(() => qc.invalidateQueries())
    // Si no conecta (red caída), la app sigue funcionando; solo deja de actualizarse sola.
    connection.start().catch(() => {})

    return () => {
      connection.stop()
    }
  }, [qc])
}
