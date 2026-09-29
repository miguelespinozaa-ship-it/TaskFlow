using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Domain.Auditing;
using TaskFlow.Domain.Labels;
using TaskFlow.Domain.Tasks;
using TaskFlow.Domain.Workspaces;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

internal sealed class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.ToTable("comments");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Body).HasMaxLength(Comment.BodyMaxLength).IsRequired();

        builder.HasOne<Workspace>().WithMany().HasForeignKey(c => c.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TaskItem>().WithMany().HasForeignKey(c => c.TaskId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(c => c.AuthorId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.TaskId, c.CreatedAt }).HasFilter("deleted_at IS NULL");
    }
}

internal sealed class LabelConfiguration : IEntityTypeConfiguration<Label>
{
    public void Configure(EntityTypeBuilder<Label> builder)
    {
        builder.ToTable("labels");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.Name).HasMaxLength(Label.NameMaxLength).IsRequired();
        builder.Property(l => l.Color).HasMaxLength(7).IsRequired();

        builder.HasOne<Workspace>().WithMany().HasForeignKey(l => l.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(l => new { l.WorkspaceId, l.Name }).IsUnique();
    }
}

internal sealed class ActivityConfiguration : IEntityTypeConfiguration<Activity>
{
    public void Configure(EntityTypeBuilder<Activity> builder)
    {
        builder.ToTable("activities");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.EntityType).HasMaxLength(30).IsRequired();
        builder.Property(a => a.Action).HasMaxLength(30).IsRequired();

        // jsonb: una sola tabla de auditoría para todas las entidades, y se puede consultar por dentro
        // (changes->'status'->>'to' = 'Done') si hiciera falta.
        builder.Property(a => a.Changes).HasColumnType("jsonb").IsRequired();

        builder.HasOne<Workspace>().WithMany().HasForeignKey(a => a.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        // Sin FK a users a propósito: el historial tiene que sobrevivir aunque se borre el usuario.

        builder.HasIndex(a => new { a.WorkspaceId, a.CreatedAt, a.Id }).IsDescending(false, true, true);
        builder.HasIndex(a => new { a.EntityId, a.CreatedAt });
    }
}
