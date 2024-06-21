using System.Text.Json.Serialization;
using Asp.Versioning;
using FiMAdminApi;
using FiMAdminApi.Clients;
using FiMAdminApi.Data;
using FiMAdminApi.Data.Enums;
using FiMAdminApi.Endpoints;
using FiMAdminApi.Infrastructure;
using FiMAdminApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddControllers().AddJsonOptions(opt =>
{
    opt.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddApiConfiguration();

builder.Services.AddHealthChecks().AddNpgSql(builder.Configuration.GetConnectionString("fimDbConnection") ??
                                             throw new Exception("DB Connection string was null"), name: "Database");

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
    foreach (var permission in Enum.GetNames<GlobalPermission>())
    {
        opt.AddPolicy(permission, pol => pol
                .RequireAuthenticatedUser()
                .RequireClaim("globalPermission", permission, GlobalPermission.Superuser.ToString()));
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
builder.Services.AddOutputCache();

var app = builder.Build();

app.UseOutputCache();
app.UseApiConfiguration();

app.UseHttpsRedirection();
app.UseCors();

app.UseApiDocumentation();

app.UseAuthentication();
app.UseAuthorization();

var globalVs = app.NewApiVersionSet().HasApiVersion(new ApiVersion(1)).Build();
app
    .RegisterHealthEndpoints()
    .RegisterUsersEndpoints(globalVs)
    .RegisterEventsCreateEndpoints(globalVs);

app.Run();