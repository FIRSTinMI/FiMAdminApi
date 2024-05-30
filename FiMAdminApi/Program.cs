using System.Text.Json.Serialization;
using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using FiMAdminApi;
using FiMAdminApi.Data;
using FiMAdminApi.Data.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();

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
        opt.AddPolicy(role, pol => pol.RequireAuthenticatedUser().RequireClaim("globalRole", role));
    }
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseSwagger();
app.UseSwaggerUI(
    options =>
    {
        var descriptions = app.DescribeApiVersions();

        foreach (var description in descriptions)
        {
            options.SwaggerEndpoint($"/swagger/{description.GroupName}/swagger.json", description.GroupName.ToUpperInvariant());
        }
    });

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public class ConfigureSwaggerOptions : IConfigureOptions<SwaggerGenOptions>
{
    private readonly IApiVersionDescriptionProvider _provider;

    public ConfigureSwaggerOptions(IApiVersionDescriptionProvider provider)
    {
        _provider = provider;
    }

    public void Configure(SwaggerGenOptions options)
    {
        foreach (var description in _provider.ApiVersionDescriptions)
        {
            options.SwaggerDoc(description.GroupName, CreateInfoForApiVersion(description));
        }
        
        options.AddSecurityDefinition("Supabase Token", new OpenApiSecurityScheme
        {
            Description = "A JWT acquired by authenticating with the Supabase instance. Be sure to prefix with 'Bearer '",
            Name = "Authorization",
            Scheme = "Bearer",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.ApiKey
        });
        
        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Supabase Token"
                    },
                    Scheme = "Bearer",
                    Name = "Authorization",
                    In = ParameterLocation.Header
                },
                new List<string>()
            }
        });
    }

    private OpenApiInfo CreateInfoForApiVersion(ApiVersionDescription description)
    {
        var info = new OpenApiInfo
        {
            Title = "FiM Admin API",
            Version = description.ApiVersion.ToString(),
            Description = "Endpoints which require more stringent authorization or business logic. For everything else use Supabase directly."
        };

        return info;
    }
}