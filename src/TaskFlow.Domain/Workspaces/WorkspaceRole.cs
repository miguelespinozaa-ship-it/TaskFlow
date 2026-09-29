namespace TaskFlow.Domain.Workspaces;

/// <summary>Rol de un usuario DENTRO de un workspace. El mismo usuario puede ser Owner en uno y Viewer en otro.</summary>
public enum WorkspaceRole
{
    Viewer,
    Member,
    Admin,
    Owner,
}
