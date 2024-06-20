using System.ComponentModel;
using System.Net.Mime;
using System.Reflection;
using Asp.Versioning;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace FiMAdminApi.Infrastructure;

public static class ApiStartupExtensions
{
    public static IServiceCollection AddApiConfiguration(this IServiceCollection services)
    {
        services.AddApiVersioning(opt =>
        {
            opt.DefaultApiVersion = new ApiVersion(1.0);
            opt.ReportApiVersions = true;
            opt.ApiVersionReader = new UrlSegmentApiVersionReader();
        }).AddMvc().AddApiExplorer(opt =>
        {
            opt.GroupNameFormat = "'v'VVV";
            opt.SubstituteApiVersionInUrl = true;
        });
        services.AddProblemDetails();
        services.AddEndpointsApiExplorer();

        // TODO: Remove when OpenAPI is working
        services.AddSwaggerGen(opt =>
        {
            var scheme = new OpenApiSecurityScheme()
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                In = ParameterLocation.Header,
                BearerFormat = "JSON Web Token"
            };
            opt.AddSecurityDefinition("Bearer", scheme);
            opt.AddSecurityRequirement(new OpenApiSecurityRequirement()
            {
                { scheme, [] }
            });

            opt.OperationFilter<AuthorizeCheckOperationFilter>();
        });

        services.AddOpenApi(opt =>
        {
            opt.UseTransformer((doc, _, _) =>
            {
                doc.Info = new OpenApiInfo
                {
                    Title = "FiM Admin API",
                    Description =
                        "A collection of endpoints that require more stringent authorization or business logic",
                    Version = "v1"
                };
                return Task.CompletedTask;
            });
            opt.UseTransformer<BearerSecuritySchemeTransformer>();
            opt.UseTransformer((doc, ctx, ct) =>
            {
                foreach (var tag in doc.Tags)
                {
                    var controllerType = Assembly.GetExecutingAssembly()
                        .GetType($"FiMAdminApi.Controllers.{tag.Name}Controller", false);
                    if (controllerType is null) continue;

                    var description = controllerType.GetCustomAttribute<DescriptionAttribute>()?.Description;
                    tag.Description = description;
                }

                return Task.CompletedTask;
            });
        });

        return services;
    }

    public static IEndpointRouteBuilder UseApiConfiguration(this IEndpointRouteBuilder app)
    {
        // Configure the HTTP request pipeline.
        app.MapOpenApi();

        // TODO: Remove when OpenAPI is working
        app.MapSwagger();
        
        return app;
    }

    public static IEndpointRouteBuilder UseApiDocumentation(this IEndpointRouteBuilder app)
    {
        // Redirect from the root to API docs
        app.MapGet("/", ctx =>
        {
            ctx.Response.Redirect("/docs");
            return Task.CompletedTask;
        }).ExcludeFromDescription();

        // Serve API documentation
        // TODO: Update to `/openapi/v1.json` when OpenAPI is working
        app.MapGet("/docs", () =>
        {
            const string resp = """
                                <html>
                                <head>
                                  <script src="https://unpkg.com/@stoplight/elements/web-components.min.js"></script>
                                  <link rel="stylesheet" href="https://unpkg.com/@stoplight/elements/styles.min.css">
                                </head>
                                <body>
                                  <elements-api apiDescriptionUrl="/swagger/v1/swagger.json" router="hash" basePath="/docs"/>
                                </body>
                                </html>
                                """;
    
            return Results.Content(resp, MediaTypeNames.Text.Html);
        }).ExcludeFromDescription();

        return app;
    }
}

internal sealed class BearerSecuritySchemeTransformer(IAuthenticationSchemeProvider authenticationSchemeProvider) : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var authenticationSchemes = await authenticationSchemeProvider.GetAllSchemesAsync();
        if (authenticationSchemes.Any(authScheme => authScheme.Name == "Bearer"))
        {
            // Add the security scheme at the document level
            var requirements = new Dictionary<string, OpenApiSecurityScheme>
            {
                ["Bearer"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer", // "bearer" refers to the header name here
                    In = ParameterLocation.Header,
                    BearerFormat = "Json Web Token"
                }
            };
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes = requirements;

            // Apply it as a requirement for all operations
            foreach (var operation in document.Paths.Values.SelectMany(path => path.Operations))
            {
                operation.Value.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecurityScheme { Reference = new OpenApiReference { Id = "Bearer", Type = ReferenceType.SecurityScheme } }] = Array.Empty<string>()
                });
            }
        }
    }
}

// TODO: Remove when OpenAPI is working
public class AuthorizeCheckOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        operation.Responses.Add("401", new OpenApiResponse { Description = "Unauthorized" });
        // operation.Responses.Add("403", new OpenApiResponse { Description = "Forbidden" });

        var jwtBearerScheme = new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id = "Bearer"
            }
        };

        operation.Security = new List<OpenApiSecurityRequirement>
        {
            new OpenApiSecurityRequirement
            {
                [jwtBearerScheme] = Array.Empty<string>()
            }
        };
    }
}