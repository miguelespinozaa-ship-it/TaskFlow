namespace TaskFlow.Application.Abstractions;

public interface ICurrentUser
{
    Guid? UserId { get; }
}

public static class CurrentUserExtensions
{
    public static Guid RequireUserId(this ICurrentUser user) =>
        user.UserId ?? throw new Common.Exceptions.UnauthorizedException("No autenticado.");
}
