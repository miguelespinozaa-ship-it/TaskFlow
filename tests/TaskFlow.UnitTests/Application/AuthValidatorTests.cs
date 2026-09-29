using TaskFlow.Application.Auth;
using TaskFlow.Application.Workspaces;
using TaskFlow.Domain.Workspaces;

namespace TaskFlow.UnitTests.Application;

public sealed class AuthValidatorTests
{
    [Theory]
    [InlineData("no-es-email")]
    [InlineData("")]
    public void Register_rechaza_emails_invalidos(string email)
    {
        var result = new RegisterRequestValidator().Validate(new RegisterRequest(email, "Passw0rd123", "Nombre"));

        result.Errors.ShouldContain(e => e.PropertyName == nameof(RegisterRequest.Email));
    }

    [Fact]
    public void Register_exige_nombre()
    {
        var result = new RegisterRequestValidator().Validate(new RegisterRequest("a@b.com", "Passw0rd123", ""));

        result.Errors.ShouldContain(e => e.PropertyName == nameof(RegisterRequest.DisplayName));
    }

    [Fact]
    public void AddMember_no_permite_asignar_Owner()
    {
        var result = new AddMemberRequestValidator().Validate(new AddMemberRequest("a@b.com", WorkspaceRole.Owner));

        result.Errors.ShouldContain(e => e.PropertyName == nameof(AddMemberRequest.Role));
    }

    [Theory]
    [InlineData(WorkspaceRole.Admin)]
    [InlineData(WorkspaceRole.Member)]
    [InlineData(WorkspaceRole.Viewer)]
    public void AddMember_acepta_roles_asignables(WorkspaceRole role)
    {
        new AddMemberRequestValidator().Validate(new AddMemberRequest("a@b.com", role)).IsValid.ShouldBeTrue();
    }
}
