using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FiMAdminApi.Endpoints;

public static class HealthEndpoints
{
    public static WebApplication RegisterHealthEndpoints(this WebApplication app)
    {
        var usersGroup = app.MapGroup("/health")
            .WithTags("Health Checks");

        usersGroup.MapGet("", async ([FromServices] HealthCheckService hc, [FromServices] ILoggerFactory logger) =>
            {
                var report = await hc.CheckHealthAsync();

                return report.Status switch
                {
                    HealthStatus.Healthy => TypedResults.Ok(report),
                    HealthStatus.Degraded => Results.Ok(report),
                    HealthStatus.Unhealthy => Results.Json(report, statusCode: StatusCodes.Status503ServiceUnavailable),
                    _ => throw new ArgumentOutOfRangeException()
                };
            })
            .WithSummary("Get Service Status")
            .WithDescription("Note that this endpoint may return a cached response (up to 10 minutes). Check the `Age` header for the age of the data in seconds.")
            .Produces<ThinHealthReport>()
            .Produces<ThinHealthReport>(StatusCodes.Status503ServiceUnavailable).CacheOutput(pol =>
            {
                pol.AddPolicy<LaxCachingPolicy>();
                pol.Expire(TimeSpan.FromMinutes(10));
            });

        return app;
    }

    public class ThinHealthReport(HealthReport report)
    {
        public required HealthStatus Status { get; set; } = report.Status;
        public required string TotalDuration { get; set; } = report.TotalDuration.ToString();

        public required Dictionary<string, Entry> Entries { get; set; } =
            report.Entries.Select(kvp => new KeyValuePair<string, Entry>(kvp.Key, new Entry(kvp.Value))).ToDictionary();

        public class Entry(HealthReportEntry entry)
        {
            public IReadOnlyDictionary<string, object> Data { get; set; } = entry.Data;
            public string? Description { get; set; } = entry.Description;
            public string Duration { get; set; } = entry.Duration.ToString();
            public HealthStatus Status { get; set; } = entry.Status;
            public IEnumerable<string>? Tags { get; set; } = entry.Tags;
        }
    }

    public class LaxCachingPolicy : IOutputCachePolicy
    {
        public LaxCachingPolicy()
        {
        }

        /// <inheritdoc />
        ValueTask IOutputCachePolicy.CacheRequestAsync(OutputCacheContext context, CancellationToken cancellationToken)
        {
            context.EnableOutputCaching = true;
            context.AllowCacheLookup = true;
            context.AllowCacheStorage = true;
            context.AllowLocking = true;

            // Vary by any query by default
            context.CacheVaryByRules.QueryKeys = "*";

            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        ValueTask IOutputCachePolicy.ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        ValueTask IOutputCachePolicy.ServeResponseAsync(OutputCacheContext context, CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }
    }
}