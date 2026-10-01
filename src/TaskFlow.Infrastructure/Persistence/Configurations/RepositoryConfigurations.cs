using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Repositories;
using TaskFlow.Domain.Workspaces;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

internal sealed class RepositoryLinkConfiguration : IEntityTypeConfiguration<RepositoryLink>
{
    public void Configure(EntityTypeBuilder<RepositoryLink> builder)
    {
        builder.ToTable("repository_links");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.Owner).HasMaxLength(39).IsRequired();
        builder.Property(l => l.Name).HasMaxLength(100).IsRequired();
        builder.Property(l => l.DefaultBranch).HasMaxLength(255).IsRequired();
        builder.Property(l => l.ETag).HasColumnName("etag").HasMaxLength(200);
        builder.Property(l => l.LastSyncError).HasMaxLength(RepositoryLink.ErrorMaxLength);
        builder.Ignore(l => l.Repository);

        builder.HasOne<Workspace>().WithMany().HasForeignKey(l => l.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Project>().WithMany().HasForeignKey(l => l.ProjectId).OnDelete(DeleteBehavior.Cascade);

        // Un repositorio por proyecto, garantizado por la base.
        builder.HasIndex(l => l.ProjectId).IsUnique();
    }
}

internal sealed class RepositoryCommitConfiguration : IEntityTypeConfiguration<RepositoryCommit>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<RepositoryCommit> builder)
    {
        builder.ToTable("repository_commits");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Sha).HasMaxLength(40).IsFixedLength().IsRequired();
        builder.Property(c => c.Message).HasMaxLength(RepositoryCommit.MessageMaxLength).IsRequired();
        builder.Property(c => c.AuthorName).HasMaxLength(200).IsRequired();
        builder.Property(c => c.AuthorLogin).HasMaxLength(39);
        builder.Property(c => c.AuthorAvatarUrl).HasMaxLength(300);

        // Las carpetas se leen siempre junto con el commit y nunca se consultan por separado:
        // una columna jsonb evita una tabla hija y un JOIN por cada commit listado.
        builder.Property(c => c.Folders)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, Json),
                v => JsonSerializer.Deserialize<List<FolderChange>>(v, Json) ?? new List<FolderChange>(),
                new ValueComparer<IReadOnlyList<FolderChange>>(
                    (a, b) => a!.SequenceEqual(b!),
                    v => v.Aggregate(0, (hash, f) => HashCode.Combine(hash, f.GetHashCode())),
                    v => v.ToList()));

        builder.HasOne<Workspace>().WithMany().HasForeignKey(c => c.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Project>().WithMany().HasForeignKey(c => c.ProjectId).OnDelete(DeleteBehavior.Cascade);

        // Impide importar dos veces el mismo commit aunque la sincronización manual y la de fondo coincidan.
        builder.HasIndex(c => new { c.ProjectId, c.Sha }).IsUnique();
        // Listado paginado por cursor: más recientes primero.
        builder.HasIndex(c => new { c.ProjectId, c.CommittedAt, c.Id }).IsDescending(false, true, true);
    }
}
