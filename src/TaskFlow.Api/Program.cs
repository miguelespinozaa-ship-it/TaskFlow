using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Authorization;
using TaskFlow.Api.ErrorHandling;
using TaskFlow.Api.Operations;
using TaskFlow.Application;
using TaskFlow.Infrastructure;
using TaskFlow.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddTaskFlowObservability();

builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ExceptionToProblemDetails>();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddTaskFlowAuth();
builder.Services.AddTaskFlowHealthChecks();
builder.Services.AddTaskFlowRateLimiting();

// CORS con orígenes explícitos, nunca "*". En dev el front usa el proxy de Vite (mismo origen),
// esto queda para cuando front y API se sirvan desde dominios distintos.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

app.UseTaskFlowForwardedHeaders(); // antes que todo: el resto del pipeline tiene que ver la IP real del cliente
app.UseExceptionHandler();         // atrapa lo que falle en el resto del pipeline
app.UseStatusCodePages();          // 401/403/404 sin cuerpo -> ProblemDetails
app.UseTaskFlowRequestLogging();

// En un contenedor no hay nadie para correr `dotnet ef database update`: se aplican las migraciones al arrancar.
// Son las mismas migraciones versionadas de siempre, no EnsureCreated.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
    app.MapOpenApi().AllowAnonymous();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Seed:DemoData"))
    await DevDataSeeder.SeedAsync(app.Services);

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>(); // después de autenticar (necesita los claims), antes de autorizar
app.UseAuthorization();

app.MapControllers();
app.MapTaskFlowHealthChecks();

await app.RunAsync();

// Expone Program para WebApplicationFactory<Program> en los tests de integración.
public partial class Program;
