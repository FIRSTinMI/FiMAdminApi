using System.Net.Mime;
using Asp.Versioning;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;

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

        services.AddOpenApi(opt =>
        {
            opt.AddDocumentTransformer((doc, _, _) =>
            {
                doc.Info = new OpenApiInfo
                {
                    Title = "FiM Admin API",
                    Description =
                        "A collection of endpoints that require more stringent authorization or business logic. Most read functionality should be handled by going directly to Supabase.",
                    Version = "v1"
                };
                return Task.CompletedTask;
            });
            opt.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
        });

        return services;
    }

    public static IEndpointRouteBuilder UseApiConfiguration(this IEndpointRouteBuilder app)
    {
        // Configure the HTTP request pipeline.
        app.MapOpenApi();
        
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
        app.MapGet("/docs", () =>
        {
            const string resp = """
<html>
<head>
  <script src="https://unpkg.com/@stoplight/elements/web-components.min.js"></script>
  <link rel="stylesheet" href="https://unpkg.com/@stoplight/elements/styles.min.css">
</head>
<body>
  <elements-api apiDescriptionUrl="/openapi/v1.json" router="hash"/>
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
                },
                ["Sync Secret"] = new OpenApiSecurityScheme()
                {
                    In = ParameterLocation.Header,
                    Name = "X-fim-sync-secret",
                    Type = SecuritySchemeType.ApiKey
                }
            };
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes = requirements;

            // Apply it as a requirement for all operations
            foreach (var operation in document.Paths.Values.SelectMany(path => path.Operations))
            {
                operation.Value.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecurityScheme { Reference = new OpenApiReference { Id = operation.Value.Tags.Any(t => t.Name == "Event Sync") ? "Sync Secret" : "Bearer", Type = ReferenceType.SecurityScheme } }] =
                        Array.Empty<string>()
                });
            }
        }
    }
}