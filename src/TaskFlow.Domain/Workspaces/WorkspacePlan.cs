namespace TaskFlow.Domain.Workspaces;

public enum WorkspacePlan
{
    Free,
    Pro,
}

public static class PlanLimits
{
    /// <summary>Máximo de miembros del workspace. Null = sin límite.</summary>
    public static int? MaxMembers(this WorkspacePlan plan) => plan == WorkspacePlan.Free ? 3 : null;

    /// <summary>Máximo de proyectos (sin contar los borrados). Null = sin límite.</summary>
    public static int? MaxProjects(this WorkspacePlan plan) => plan == WorkspacePlan.Free ? 20 : null;
}
