using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;
using TaskFlow.Domain.Workspaces;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

internal sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
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

        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(t => t.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Project>()
            .WithMany()
            .HasForeignKey(t => t.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => new { t.WorkspaceId, t.Status });
        builder.HasIndex(t => new { t.ProjectId, t.Status, t.Position });
    }
}
