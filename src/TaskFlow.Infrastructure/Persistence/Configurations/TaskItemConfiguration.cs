using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;
using TaskFlow.Domain.Labels;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;
using TaskFlow.Domain.Workspaces;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

internal sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public const string SearchVector = "SearchVector";
    private const string NotDeleted = "deleted_at IS NULL";

    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.ToTable("tasks");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Title).HasMaxLength(TaskItem.TitleMaxLength).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(TaskItem.DescriptionMaxLength);

        // Enums como texto: la DB se lee sin tabla de referencia y agregar un valor
        // nuevo al enum no corre los existentes.
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Priority).HasConversion<string>().HasMaxLength(20);

        // numeric para fractional indexing (posición intermedia entre dos vecinos).
        builder.Property(t => t.Position).HasPrecision(20, 10);

        builder.HasOne<Workspace>().WithMany().HasForeignKey(t => t.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Project>().WithMany().HasForeignKey(t => t.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(t => t.AssigneeId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(t => t.ReporterId).OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(t => t.Labels).WithOne().HasForeignKey(l => l.TaskId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(t => t.Labels).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Búsqueda full-text en español: columna tsvector GENERADA por Postgres (siempre al día, sin triggers)
        // + índice GIN. Es una shadow property: el dominio no se entera de que existe.
        // Con stemming, buscar "factura" también encuentra "facturas".
        builder.Property<NpgsqlTsVector>(SearchVector)
            .HasComputedColumnSql("to_tsvector('spanish', coalesce(title, '') || ' ' || coalesce(description, ''))", stored: true);
        builder.HasIndex(SearchVector).HasMethod("GIN");

        // Índices parciales: las filas borradas (soft delete) no ocupan lugar en los índices calientes.
        builder.HasIndex(t => new { t.WorkspaceId, t.Status }).HasFilter(NotDeleted);
        builder.HasIndex(t => new { t.ProjectId, t.Status, t.Position }).HasFilter(NotDeleted);
        builder.HasIndex(t => t.AssigneeId).HasFilter(NotDeleted);
        // Keyset pagination: ORDER BY created_at DESC, id DESC dentro de un workspace.
        builder.HasIndex(t => new { t.WorkspaceId, t.CreatedAt, t.Id })
            .IsDescending(false, true, true)
            .HasFilter(NotDeleted)
            .HasDatabaseName("ix_tasks_workspace_created_id");
    }
}

internal sealed class TaskLabelConfiguration : IEntityTypeConfiguration<TaskLabel>
{
    public void Configure(EntityTypeBuilder<TaskLabel> builder)
    {
        builder.ToTable("task_labels");
        builder.HasKey(l => new { l.TaskId, l.LabelId });
        builder.HasOne<Label>().WithMany().HasForeignKey(l => l.LabelId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(l => l.LabelId); // "tareas con la etiqueta X"
    }
}
