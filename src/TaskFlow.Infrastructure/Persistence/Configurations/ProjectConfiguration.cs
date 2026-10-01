using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Workspaces;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Name).HasMaxLength(Project.NameMaxLength).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(Project.DescriptionMaxLength);
        builder.Property(p => p.KeyPrefix).HasMaxLength(10).IsRequired();

        builder.HasOne<Workspace>()
            .WithMany()
            .HasForeignKey(p => p.WorkspaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.WorkspaceId).HasFilter("deleted_at IS NULL");

        // La validación de la app tiene una carrera (dos creaciones simultáneas pasan las dos el "¿ya existe?").
        // El índice único la cierra. Es parcial: el prefijo de un proyecto borrado se puede volver a usar.
        builder.HasIndex(p => new { p.WorkspaceId, p.KeyPrefix }).IsUnique().HasFilter("deleted_at IS NULL");
    }
}
