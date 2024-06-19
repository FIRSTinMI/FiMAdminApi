using System.ComponentModel;
using System.Net.Mime;
using System.Reflection;
using System.Text.Json.Serialization;
using Asp.Versioning;
using FiMAdminApi;
using FiMAdminApi.Clients;
using FiMAdminApi.Clients.Endpoints;
using FiMAdminApi.Data;
using FiMAdminApi.Data.Enums;
using FiMAdminApi.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddControllers().AddJsonOptions(opt =>
{
    opt.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddApiVersioning(opt =>
{
    opt.DefaultApiVersion = new ApiVersion(1.0);
    opt.ReportApiVersions = true;
    opt.ApiVersionReader = new UrlSegmentApiVersionReader();
}).AddMvc().AddApiExplorer(opt =>
{
    opt.GroupNameFormat = "'v'VVV";
    opt.SubstituteApiVersionInUrl = true;
});
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();

// TODO: Remove when OpenAPI is working
builder.Services.AddSwaggerGen(opt =>
{
    // // Add the security scheme at the document level
    // var requirements = new Dictionary<string, OpenApiSecurityScheme>
    // {
    //     ["Bearer"] = new OpenApiSecurityScheme
    //     {
    //         Type = SecuritySchemeType.Http,
    //         Scheme = "bearer", // "bearer" refers to the header name here
    //         In = ParameterLocation.Header,
    //         BearerFormat = "Json Web Token"
    //     }
    // };
    // document.Components ??= new OpenApiComponents();
    // document.Components.SecuritySchemes = requirements;
    //
    // // Apply it as a requirement for all operations
    // foreach (var operation in document.Paths.Values.SelectMany(path => path.Operations))
    // {
    //     operation.Value.Security.Add(new OpenApiSecurityRequirement
    //     {
    //         [new OpenApiSecurityScheme { Reference = new OpenApiReference { Id = "Bearer", Type = ReferenceType.SecurityScheme } }] = Array.Empty<string>()
    //     });
    // }

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

builder.Services.AddOpenApi(opt =>
{
    opt.UseTransformer((doc, _, _) =>
    {
        doc.Info = new OpenApiInfo
        {
            Title = "FiM Admin API",
            Description = "A collection of endpoints that require more stringent authorization or business logic",
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

var key = builder.Configuration["Supabase:ServiceKey"];
var supabaseUrl = builder.Configuration["Supabase:BaseUrl"];
if (key is null || supabaseUrl is null)
{
    throw new ApplicationException("Supabase service key and URL are required");
}
var supabase = new Supabase.Client(supabaseUrl, key);
await supabase.InitializeAsync();
builder.Services.AddScoped(_ => supabase);
builder.Services.AddScoped(_ => supabase.AdminAuth(key));

var connectionString = builder.Configuration.GetConnectionString("fimDbConnection");
if (connectionString is null)
{
    throw new ApplicationException("FiM Connection String is required");
}

builder.Services.AddNpgsql<DataContext>(connectionString, optionsAction: opt =>
{
    opt.UseSnakeCaseNamingConvention();
});

// For authn/authz we're using tokens directly from Supabase. These tokens get validated by the supabase auth infrastructure
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddScheme<JwtBearerOptions, SupabaseJwtHandler>(JwtBearerDefaults.AuthenticationScheme, _ => { });
builder.Services.AddAuthorization(opt =>
{
    foreach (var role in Enum.GetNames<GlobalRole>())
    {
        opt.AddPolicy(role, pol => pol
                .RequireAuthenticatedUser()
                .RequireClaim("globalRole", role, GlobalRole.Superuser.ToString()));
    }
});

builder.Services.AddCors(opt =>
{
    opt.AddDefaultPolicy(pol =>
    {
        pol.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? (string[])["http://localhost:5173"]);
        pol.AllowCredentials();
        pol.AllowAnyMethod();
        pol.AllowAnyHeader();
    });
});

builder.Services.AddScoped<UpsertEventsService>();
builder.Services.AddClients();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapOpenApi();

// TODO: Remove when OpenAPI is working
app.MapSwagger();

app.UseHttpsRedirection();
app.UseCors();

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

app.UseAuthentication();
app.UseAuthorization();

var globalVs = app.NewApiVersionSet().HasApiVersion(new ApiVersion(1)).Build();
app
    .RegisterUsersEndpoints(globalVs)
    .RegisterEventsCreateEndpoints(globalVs);

app.Run();

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