using TaskFlow.Domain.Common;
using TaskFlow.Domain.Labels;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.UnitTests.Domain;

public sealed class BoardPositionTests
{
    [Fact]
    public void Columna_vacia_empieza_en_el_gap() => BoardPosition.Between(null, null).ShouldBe(BoardPosition.Gap);

    [Fact]
    public void Al_final_suma_el_gap() => BoardPosition.Between(3000m, null).ShouldBe(4000m);

    [Fact]
    public void Al_principio_resta_el_gap() => BoardPosition.Between(null, 1000m).ShouldBe(0m);

    [Fact]
    public void Entre_dos_vecinas_es_el_punto_medio() => BoardPosition.Between(1000m, 2000m).ShouldBe(1500m);

    [Fact]
    public void Vecinas_desordenadas_fallan() =>
        Should.Throw<ArgumentException>(() => BoardPosition.Between(2000m, 1000m));

    [Fact]
    public void Tras_muchas_mitades_pide_rebalanceo()
    {
        decimal? previous = 1000m, next = 2000m;
        var halvings = 0;
        while (!BoardPosition.NeedsRebalance(previous, next))
        {
            next = BoardPosition.Between(previous, next);
            halvings++;
        }

        // 1000 / 2^n < 2e-6  →  n ≈ 29. Sin rebalanceo, numeric(20,10) se quedaría sin decimales poco después.
        halvings.ShouldBeInRange(25, 35);
    }
}

public sealed class TaskItemPhase2Tests
{
    private static readonly DateTime Now = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    private static TaskItem NewTask() =>
        TaskItem.Create(Project.Create(Guid.NewGuid(), "P", "PR", null, Now), "T", null, TaskPriority.Low, 1000m, Now);

    [Fact]
    public void SetLabels_reemplaza_el_conjunto_y_deduplica()
    {
        var task = NewTask();
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();

        task.SetLabels([a, b, b], Now);
        task.SetLabels([b, c], Now);

        task.Labels.Select(l => l.LabelId).ShouldBe([b, c], ignoreOrder: true);
    }

    [Fact]
    public void SetLabels_con_mas_del_maximo_falla()
    {
        var ids = Enumerable.Range(0, TaskItem.MaxLabels + 1).Select(_ => Guid.NewGuid()).ToList();

        Should.Throw<DomainException>(() => NewTask().SetLabels(ids, Now));
    }

    [Fact]
    public void Mover_a_Done_fija_CompletedAt_y_salir_lo_limpia()
    {
        var task = NewTask();

        task.MoveTo(TaskItemStatus.Done, 1m, Now);
        task.CompletedAt.ShouldBe(Now);

        task.MoveTo(TaskItemStatus.Todo, 1m, Now.AddHours(1));
        task.CompletedAt.ShouldBeNull();
    }

    [Fact]
    public void MarkDeleted_es_idempotente_y_conserva_la_primera_fecha()
    {
        var task = NewTask();

        task.MarkDeleted(Now);
        task.MarkDeleted(Now.AddDays(1));

        task.DeletedAt.ShouldBe(Now);
    }

    [Fact]
    public void Prioridad_invalida_falla() =>
        Should.Throw<DomainException>(() => NewTask().ChangePriority((TaskPriority)99, Now));

    [Fact]
    public void Solo_el_autor_edita_su_comentario()
    {
        var author = Guid.NewGuid();
        var comment = Comment.Create(NewTask(), author, "hola", Now);

        Should.Throw<ForbiddenDomainException>(() => comment.Edit(Guid.NewGuid(), "hackeado", Now));
        comment.Edit(author, "editado", Now.AddMinutes(1));

        comment.Body.ShouldBe("editado");
        comment.EditedAt.ShouldBe(Now.AddMinutes(1));
    }

    [Fact]
    public void No_se_comenta_una_tarea_eliminada()
    {
        var task = NewTask();
        task.MarkDeleted(Now);

        Should.Throw<DomainException>(() => Comment.Create(task, Guid.NewGuid(), "x", Now));
    }

    [Theory]
    [InlineData("rojo")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    public void Label_rechaza_colores_invalidos(string color) =>
        Should.Throw<DomainException>(() => Label.Create(Guid.NewGuid(), "bug", color));

    [Fact]
    public void Label_normaliza_el_color_a_minusculas() =>
        Label.Create(Guid.NewGuid(), "bug", "#AABBCC").Color.ShouldBe("#aabbcc");

    [Fact]
    public void Project_desarchivar_uno_activo_falla() =>
        Should.Throw<DomainException>(() => Project.Create(Guid.NewGuid(), "P", "PR", null, Now).Unarchive());
}
