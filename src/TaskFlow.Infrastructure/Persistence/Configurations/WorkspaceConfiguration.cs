using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskFlow.Domain.Workspaces;

namespace TaskFlow.Infrastructure.Persistence.Configurations;

internal sealed class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    public void Configure(EntityTypeBuilder<Workspace> builder)
    {
        builder.ToTable("workspaces");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).ValueGeneratedNever();
        builder.Property(w => w.Name).HasMaxLength(100).IsRequired();
        builder.Property(w => w.Slug).HasMaxLength(60).IsRequired();
        builder.Property(w => w.Plan).HasConversion<string>().HasMaxLength(20).HasDefaultValue(WorkspacePlan.Free);
        builder.HasIndex(w => w.Slug).IsUnique();
    }
}
