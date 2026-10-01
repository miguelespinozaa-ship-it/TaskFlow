using System.Net.Http.Headers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Auth;
using TaskFlow.Infrastructure.GitHub;
using TaskFlow.Infrastructure.Identity;
using TaskFlow.Infrastructure.Persistence;
using TaskFlow.Infrastructure.Persistence.Repositories;
using TaskFlow.Infrastructure.Tenancy;

namespace TaskFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:Postgres.");

        services.AddDbContext<AppDbContext>(options => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());

        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<ILabelRepository, LabelRepository>();
        services.AddScoped<ICommentRepository, CommentRepository>();
        services.AddScoped<IActivityRepository, ActivityRepository>();
        services.AddScoped<IRepositoryLinkRepository, RepositoryLinkRepository>();

        AddIdentity(services);
        AddGitHub(services);
        return services;
    }

    private static void AddGitHub(IServiceCollection services)
    {
        services.AddOptions<GitHubOptions>().BindConfiguration(GitHubOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();

        // Typed client con IHttpClientFactory: reutiliza conexiones y no agota sockets como `new HttpClient()` por request.
        services.AddHttpClient<IGitHubClient, GitHubClient>((sp, http) =>
        {
            http.BaseAddress = GitHubClient.BaseAddress;
            http.Timeout = TimeSpan.FromSeconds(15);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("TaskFlow"); // GitHub rechaza requests sin User-Agent
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

            var token = sp.GetRequiredService<IOptions<GitHubOptions>>().Value.Token;
            if (!string.IsNullOrWhiteSpace(token))
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        });

        services.AddSingleton<RepositorySyncRunner>();
        services.AddHostedService<RepositorySyncWorker>();
    }

    private static void AddIdentity(IServiceCollection services)
    {
        services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart(); // arranque falla si falta el secreto, no el primer login

        // AddIdentityCore: solo usuarios + hashing + lockout. Sin cookies de Identity ni UI:
        // la autenticación de la API es JWT.
        services.AddIdentityCore<ApplicationUser>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = 8;
                o.Password.RequireDigit = true;
                o.Password.RequireLowercase = true;
                o.Password.RequireUppercase = true;
                o.Password.RequireNonAlphanumeric = false;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddEntityFrameworkStores<AppDbContext>();

        services.AddScoped<TokenService>();
        services.AddScoped<IAuthService, AuthService>();
    }
}
