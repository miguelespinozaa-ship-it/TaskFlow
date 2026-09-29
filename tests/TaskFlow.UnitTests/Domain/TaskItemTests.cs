using TaskFlow.Domain.Common;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.UnitTests.Domain;

public sealed class TaskItemTests
{
    private static readonly DateTime Now = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid WorkspaceId = Guid.CreateVersion7();

    private static Project NewProject() => Project.Create(WorkspaceId, "Proyecto", "PR", null, Now);

    private static TaskItem NewTask(Project? project = null) =>
        TaskItem.Create(project ?? NewProject(), "Escribir tests", null, TaskPriority.High, 1000m, Now);

    [Fact]
    public void Create_hereda_el_workspace_del_proyecto()
    {
        var project = NewProject();

        var task = NewTask(project);

        task.WorkspaceId.ShouldBe(project.WorkspaceId);
        task.ProjectId.ShouldBe(project.Id);
        task.Status.ShouldBe(TaskItemStatus.Todo);
        task.CreatedAt.ShouldBe(Now);
    }

    [Fact]
    public void Create_recorta_el_titulo_y_normaliza_descripcion_vacia()
    {
        var task = TaskItem.Create(NewProject(), "  Hola  ", "   ", TaskPriority.Low, 1m, Now);

        task.Title.ShouldBe("Hola");
        task.Description.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rechaza_titulo_vacio(string title)
    {
        Should.Throw<DomainException>(() => TaskItem.Create(NewProject(), title, null, TaskPriority.Low, 1m, Now));
    }

    [Fact]
    public void Create_rechaza_titulo_demasiado_largo()
    {
        var title = new string('x', TaskItem.TitleMaxLength + 1);

        Should.Throw<DomainException>(() => TaskItem.Create(NewProject(), title, null, TaskPriority.Low, 1m, Now));
    }

    [Fact]
    public void Create_en_proyecto_archivado_falla()
    {
        var project = NewProject();
        project.Archive();

        Should.Throw<DomainException>(() => NewTask(project));
    }

    [Fact]
    public void MarkCompleted_marca_done_y_fecha_de_completado()
    {
        var task = NewTask();
        var later = Now.AddHours(2);

        task.MarkCompleted(later);

        task.Status.ShouldBe(TaskItemStatus.Done);
        task.CompletedAt.ShouldBe(later);
        task.UpdatedAt.ShouldBe(later);
    }

    [Fact]
    public void MarkCompleted_dos_veces_falla()
    {
        var task = NewTask();
        task.MarkCompleted(Now);

        Should.Throw<DomainException>(() => task.MarkCompleted(Now));
    }

    [Fact]
    public void MoveTo_fuera_de_done_limpia_la_fecha_de_completado()
    {
        var task = NewTask();
        task.MarkCompleted(Now);

        task.MoveTo(TaskItemStatus.InProgress, 500m, Now.AddMinutes(1));

        task.Status.ShouldBe(TaskItemStatus.InProgress);
        task.Position.ShouldBe(500m);
        task.CompletedAt.ShouldBeNull();
    }
}
