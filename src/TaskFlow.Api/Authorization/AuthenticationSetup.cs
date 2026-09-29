using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TaskFlow.Application.Abstractions;
using TaskFlow.Infrastructure.Identity;

namespace TaskFlow.Api.Authorization;

public static class AuthenticationSetup
{
    public static IServiceCollection AddTaskFlowAuth(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Se configura desde IOptions<JwtOptions> (y no leyendo builder.Configuration a mano) para que
        // los tests puedan sobreescribir el secreto con UseSetting y todo use el mismo valor.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((options, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;
                // Sin esto, "sub" llega como "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier".
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256], // nada de "alg: none" ni cambios de algoritmo
                    // Default: 5 min de tolerancia. En 0, un token vence exactamente cuando dice.
                    ClockSkew = TimeSpan.Zero,
                    NameClaimType = TaskFlowClaims.Subject,
                    RoleClaimType = TaskFlowClaims.Role,
                };
            });

        services.AddAuthorization(WorkspacePolicies.Configure);
        return services;
    }
}
