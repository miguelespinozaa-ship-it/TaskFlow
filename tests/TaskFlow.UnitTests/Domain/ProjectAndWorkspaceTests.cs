using TaskFlow.Domain.Common;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Workspaces;

namespace TaskFlow.UnitTests.Domain;

public sealed class ProjectAndWorkspaceTests
{
    private static readonly DateTime Now = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("demo")]
    [InlineData("acme-corp")]
    [InlineData("team-42")]
    public void Workspace_acepta_slugs_validos(string slug)
    {
        Workspace.Create("Acme", slug, Now).Slug.ShouldBe(slug);
    }

    [Theory]
    [InlineData("Acme")]
    [InlineData("acme corp")]
    [InlineData("-acme")]
    [InlineData("acme--corp")]
    [InlineData("")]
    public void Workspace_rechaza_slugs_invalidos(string slug)
    {
        Should.Throw<DomainException>(() => Workspace.Create("Acme", slug, Now));
    }

    [Fact]
    public void Project_sin_workspace_falla()
    {
        Should.Throw<DomainException>(() => Project.Create(Guid.Empty, "P", "PR", null, Now));
    }

    [Theory]
    [InlineData("pr")]
    [InlineData("P1")]
    [InlineData("DEMASIADOLARGO")]
    public void Project_rechaza_prefijos_invalidos(string prefix)
    {
        Should.Throw<DomainException>(() => Project.Create(Guid.CreateVersion7(), "P", prefix, null, Now));
    }

    [Fact]
    public void Archive_dos_veces_falla()
    {
        var project = Project.Create(Guid.CreateVersion7(), "P", "PR", null, Now);
        project.Archive();

        Should.Throw<DomainException>(project.Archive);
    }
}
