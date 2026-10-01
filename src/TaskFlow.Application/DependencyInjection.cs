using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Application.Activities;
using TaskFlow.Application.Comments;
using TaskFlow.Application.Common;
using TaskFlow.Application.Labels;
using TaskFlow.Application.Projects;
using TaskFlow.Application.Repositories;
using TaskFlow.Application.Tasks;
using TaskFlow.Application.Workspaces;

namespace TaskFlow.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddScoped<IRequestValidator, RequestValidator>();

        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<IWorkspaceService, WorkspaceService>();
        services.AddScoped<ILabelService, LabelService>();
        services.AddScoped<ICommentService, CommentService>();
        services.AddScoped<IActivityService, ActivityService>();
        services.AddScoped<IRepositoryService, RepositoryService>();
        return services;
    }
}
