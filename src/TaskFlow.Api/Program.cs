using System.Text.Json.Serialization;
using TaskFlow.Api.Authorization;
using TaskFlow.Api.ErrorHandling;
using TaskFlow.Application;
using TaskFlow.Infrastructure;
using TaskFlow.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ExceptionToProblemDetails>();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddTaskFlowAuth();

// CORS con orígenes explícitos, nunca "*". En dev el front usa el proxy de Vite (mismo origen),
// esto queda para cuando front y API se sirvan desde dominios distintos.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

app.UseExceptionHandler();   // primero: atrapa lo que falle en el resto del pipeline
app.UseStatusCodePages();    // 401/403/404 sin cuerpo -> ProblemDetails

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    await DevDataSeeder.SeedAsync(app.Services);
}

app.UseCors();
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>(); // después de autenticar (necesita los claims), antes de autorizar
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

await app.RunAsync();

// Expone Program para WebApplicationFactory<Program> en los tests de integración.
public partial class Program;
