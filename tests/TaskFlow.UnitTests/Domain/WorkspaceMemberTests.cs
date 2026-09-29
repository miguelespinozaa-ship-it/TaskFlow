using TaskFlow.Domain.Common;
using TaskFlow.Domain.Workspaces;

namespace TaskFlow.UnitTests.Domain;

public sealed class WorkspaceMemberTests
{
    private static readonly DateTime Now = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_sin_usuario_o_workspace_falla()
    {
        Should.Throw<DomainException>(() => WorkspaceMember.Create(Guid.Empty, Guid.NewGuid(), WorkspaceRole.Member, Now));
        Should.Throw<DomainException>(() => WorkspaceMember.Create(Guid.NewGuid(), Guid.Empty, WorkspaceRole.Member, Now));
    }

    [Fact]
    public void ChangeRole_cambia_el_rol()
    {
        var member = WorkspaceMember.Create(Guid.NewGuid(), Guid.NewGuid(), WorkspaceRole.Viewer, Now);

        member.ChangeRole(WorkspaceRole.Admin);

        member.Role.ShouldBe(WorkspaceRole.Admin);
    }

    [Fact]
    public void El_rol_del_Owner_no_se_puede_cambiar()
    {
        var owner = WorkspaceMember.Create(Guid.NewGuid(), Guid.NewGuid(), WorkspaceRole.Owner, Now);

        Should.Throw<DomainException>(() => owner.ChangeRole(WorkspaceRole.Member));
    }

    [Fact]
    public void Rol_fuera_del_enum_falla()
    {
        Should.Throw<DomainException>(() => WorkspaceMember.Create(Guid.NewGuid(), Guid.NewGuid(), (WorkspaceRole)42, Now));
    }

    [Theory]
    [InlineData("juan.perez", "juan-perez")]
    [InlineData("José.Núñez+test", "jose-nunez-test")]
    [InlineData("__a__b__", "a-b")]
    [InlineData("!!!", "workspace")]
    [InlineData("", "workspace")]
    public void WorkspaceSlug_genera_slugs_validos(string input, string expected)
    {
        var slug = WorkspaceSlug.FromText(input);

        slug.ShouldBe(expected);
        Should.NotThrow(() => Workspace.Create("W", slug, Now)); // siempre pasa la validación de Workspace
    }

    [Fact]
    public void WorkspaceSlug_trunca_textos_largos()
    {
        WorkspaceSlug.FromText(new string('a', 100)).Length.ShouldBeLessThanOrEqualTo(40);
    }
}
