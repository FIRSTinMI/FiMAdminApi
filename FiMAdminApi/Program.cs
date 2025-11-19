using System.Diagnostics;
using Asp.Versioning;
using FiMAdminApi;
using FiMAdminApi.Auth;
using FiMAdminApi.Clients;
using FiMAdminApi.Data;
using FiMAdminApi.Data.EfPgsql;
using FiMAdminApi.Endpoints;
using FiMAdminApi.EventHandlers;
using FiMAdminApi.EventSync;
using FiMAdminApi.Infrastructure;
using FiMAdminApi.Models.Enums;
using FiMAdminApi.Repositories;
using FiMAdminApi.Services;
using Firebase.Database;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql.NameTranslation;
using SlackNet;
using Supabase;

var builder = WebApplication.CreateSlimBuilder(args);
builder.Logging.AddConsole(opt =>
{
    builder.Configuration.GetSection("Logging:Console").Bind(opt);
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolver = SerializerContext.Default;
});

builder.Services.AddApiConfiguration();

// If a data protection directory is not provided, just use the default configuration
var dpDir = builder.Configuration["DataProtectionDirectory"];
if (!string.IsNullOrEmpty(dpDir))
{
    var dirInfo = new DirectoryInfo(dpDir);
    if (!dirInfo.Exists) throw new ApplicationException($"Data protection directory {dpDir} does not exist");
    builder.Services.AddDataProtection().PersistKeysToFileSystem(dirInfo);
}

builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("fimDbConnection") ??
                                             throw new Exception("DB Connection string was null"), name: "Database")
    .AddClientHealthChecks();

var key = builder.Configuration["Supabase:ServiceKey"];
var supabaseUrl = builder.Configuration["Supabase:BaseUrl"];
if (key is null || supabaseUrl is null)
{
    throw new ApplicationException("Supabase service key and URL are required");
}

var supabase = new Supabase.Client(supabaseUrl, key, new SupabaseOptions() {AutoConnectRealtime = false});
await supabase.InitializeAsync();
builder.Services.AddSingleton(_ => supabase);
builder.Services.AddScoped(sp => sp.GetRequiredService<Supabase.Client>().AdminAuth(key));

var connectionString = builder.Configuration.GetConnectionString("fimDbConnection");
if (connectionString is null)
{
    throw new ApplicationException("FiM Connection String is required");
}

// IMPORTANT!
// This null name translator should be a singleton, and should *not* be
// refactored back inside the `AddDbContext`. Having a new one constructed
// every time confuses EF's ServiceProviderCache, meaning it has to use a new
// service provider for every request.
var nullNameTranslator = new NpgsqlNullNameTranslator();

builder.Services.AddDbContext<DataContext>(opt =>
{
    opt.UseSnakeCaseNamingConvention();
    opt.UseNpgsql(connectionString,
        o => o
            .MapEnum<TournamentLevel>("tournament_level", nameTranslator: nullNameTranslator)
            .MapEnum<MatchWinner>("match_winner", nameTranslator: nullNameTranslator));
});

if (!string.IsNullOrEmpty(builder.Configuration["Slack:Token"]))
{
    builder.Services.AddSingleton(_ =>
        new SlackServiceBuilder().UseApiToken(builder.Configuration["Slack:Token"]).GetApiClient());
}

// For most authn/authz we're using tokens directly from Supabase. These tokens get validated by the supabase auth
// infrastructure
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddScheme<JwtBearerOptions, SupabaseJwtHandler>(JwtBearerDefaults.AuthenticationScheme, _ => { })
    .AddScheme<EventSyncAuthOptions, EventSyncAuthHandler>(EventSyncAuthHandler.EventSyncAuthScheme, _ => { });
builder.Services.AddAuthorization(opt =>
{
    opt.AddPolicy(EventSyncAuthHandler.EventSyncAuthScheme,
        pol => pol
            .AddAuthenticationSchemes(EventSyncAuthHandler.EventSyncAuthScheme)
            .RequireAuthenticatedUser());
    foreach (var permission in Enum.GetNames<GlobalPermission>())
    {
        opt.AddPolicy(permission, pol => pol
                .RequireAuthenticatedUser()
                .RequireClaim("globalPermission", permission, GlobalPermission.Superuser.ToString()));
    }
});
builder.Services.AddScoped<IAuthorizationHandler, EventAuthorizationHandler>();

builder.Services.AddCors(opt =>
{
    opt.AddDefaultPolicy(pol =>
    {
        pol.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ??
                        (string[]) ["http://localhost:5173"]);
        pol.AllowCredentials();
        pol.AllowAnyMethod();
        pol.AllowAnyHeader();
    });
});

builder.Services.AddScoped<UpsertEventsService>();
builder.Services.AddScoped<EventSyncService>();
builder.Services.AddScoped<EventTeamsService>();
builder.Services.AddScoped<EventRepository>();
builder.Services.AddScoped<SlackService>();
builder.Services.AddScoped<VaultService>();
builder.Services.AddScoped<TwitchService>();
builder.Services.AddScoped<YoutubeService>();
builder.Services.AddScoped<EventStreamService>();
builder.Services.AddScoped<ThumbnailService>();
builder.Services.AddClients(builder.Environment.IsProduction());
builder.Services.AddAvCartService();
builder.Services.AddEventSyncSteps();
builder.Services.AddOutputCache();
builder.Services.AddEventHandlers();

var accountCred = await GoogleCredential.GetApplicationDefaultAsync();
var credential = accountCred.CreateScoped(
    "https://www.googleapis.com/auth/userinfo.email",
    "https://www.googleapis.com/auth/firebase.database");

async Task<string> GetAccessToken()
{
    return await (credential as ITokenAccess).GetAccessTokenForRequestAsync();
}

builder.Services.AddSingleton(_ => new FirebaseClient(builder.Configuration["Firebase:BaseUrl"],
    new FirebaseOptions
    {
        AuthTokenAsyncFactory = GetAccessToken,
        AsAccessToken = true
    }));

var app = builder.Build();

app.UseOutputCache();
app.UseApiConfiguration();

// When running in a container, traffic is served over HTTP with an external load balancer handling SSL termination
if (!bool.TryParse(app.Configuration["RUNNING_IN_CONTAINER"], out var inContainer) || !inContainer)
    app.UseHttpsRedirection();

app.UseCors();

app.UseExceptionHandler();

app.UseApiDocumentation();

app.UseAuthentication();
app.UseAuthorization();

var globalVs = app.NewApiVersionSet().HasApiVersion(new ApiVersion(1)).Build();
app
    .RegisterHealthEndpoints()
    .RegisterUsersEndpoints(globalVs)
    .RegisterEventsCreateEndpoints(globalVs)
    .RegisterEventsEndpoints(globalVs)
    .RegisterTruckRoutesEndpoints(globalVs)
    .RegisterEventSyncEndpoints(globalVs)
    .RegisterMatchesEndpoints(globalVs)
    .RegisterAvTokenEndpoints(globalVs)
    .RegisterAvCartsEndpoints(globalVs)
    .RegisterTbaWriteEndpoints(globalVs)
    .RegisterTwitchEndpoints(globalVs)
    .RegisterYoutubeEndpoints(globalVs)
    .RegisterEventStreamEndpoints(globalVs)
    .RegisterSlackBotEndpoints(globalVs);
    app.RegisterThumbnailEndpoints(globalVs);

app.Run();