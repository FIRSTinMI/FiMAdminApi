using System.Net.Http.Headers;
using Asp.Versioning.Builder;
using FiMAdminApi.Data.EfPgsql;
using FiMAdminApi.Models.Enums;
using FiMAdminApi.Models.Models;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Object = Google.Apis.Storage.v1.Data.Object;

namespace FiMAdminApi.Endpoints;

public static class SponsorsEndpoints
{
    public static WebApplication RegisterSponsorsEndpoints(this WebApplication app, ApiVersionSet vs)
    {
        var routeGroup = app.MapGroup("/api/v{apiVersion:apiVersion}/sponsors")
            .WithApiVersionSet(vs).HasApiVersion(1).WithTags("Sponsors")
            .RequireAuthorization(nameof(GlobalPermission.Sponsors_Manage));

        routeGroup.MapPost("", UploadFile)
            .WithSummary("Upload a media file")
            .WithDescription(
                "Returns a created response directing to where the file can be found")
            .DisableAntiforgery();

        routeGroup.MapPut("season/{seasonId:int:required}", SetSeasonSponsors)
            .WithSummary("Set season sponsors")
            .WithDescription("Overwrite the season sponsors with the passed in set");
        
        routeGroup.MapPut("event/{eventId:guid:required}", SetEventSponsors)
            .WithSummary("Set season sponsors")
            .WithDescription("Overwrite the season sponsors with the passed in set");
        
        return app;
    }

    private static async Task<Results<Created<string>, ProblemHttpResult>> UploadFile(
        [FromServices] IConfiguration configuration,
        [FromServices] DataContext dbContext,
        IFormFile fileStream)
    {
        var accountCred = await GoogleCredential.GetApplicationDefaultAsync();
        var cred = accountCred.CreateScoped("https://www.googleapis.com/auth/devstorage.read_write");
        var storageClient = await StorageClient.CreateAsync(cred);
        
        var extension = Path.GetExtension(fileStream.FileName);
        var fileName = Path.ChangeExtension(Path.GetRandomFileName(), extension);
        
        var uploadResult = storageClient.UploadObject(new Object
        {
            CacheControl = new CacheControlHeaderValue
            {
                Public = true,
                MaxAge = TimeSpan.FromDays(1)
            }.ToString(),
            Bucket = configuration["Storage:SponsorsBucketName"],
            Name = fileName,
            ContentType = fileStream.ContentType,
            ContentDisposition = new ContentDispositionHeaderValue("inline").ToString()
        }, fileStream.OpenReadStream());

        return TypedResults.Created(uploadResult.MediaLink, uploadResult.MediaLink);
    }

    private static async Task<Results<Ok<BaseSponsor[]>, NotFound>> SetSeasonSponsors(
        [FromRoute] int seasonId,
        [FromBody] BaseSponsor[] sponsors,
        [FromServices] DataContext dbContext)
    {
        var seasonExists = await dbContext.Seasons.AnyAsync(s => s.Id == seasonId);

        if (!seasonExists) return TypedResults.NotFound();

        var dbSponsors = await dbContext.SeasonSponsors.FirstOrDefaultAsync(s => s.SeasonId == seasonId);
        if (dbSponsors is null)
        {
            dbSponsors = new SeasonSponsor
            {
                SeasonId = seasonId
            };
            dbContext.SeasonSponsors.Add(dbSponsors);
        }

        dbSponsors.Sponsors = sponsors;

        await dbContext.SaveChangesAsync();

        return TypedResults.Ok(dbSponsors.Sponsors);
    }
    
    private static async Task<Results<Ok<BaseSponsor[]>, NotFound>> SetEventSponsors(
        [FromRoute] Guid eventId,
        [FromBody] BaseSponsor[] sponsors,
        [FromServices] DataContext dbContext)
    {
        var eventExists = await dbContext.Events.AnyAsync(s => s.Id == eventId);

        if (!eventExists) return TypedResults.NotFound();

        var dbSponsors = await dbContext.EventSponsors.FirstOrDefaultAsync(s => s.EventId == eventId);
        if (dbSponsors is null)
        {
            dbSponsors = new EventSponsor
            {
                EventId = eventId
            };
            dbContext.EventSponsors.Add(dbSponsors);
        }

        dbSponsors.Sponsors = sponsors;

        await dbContext.SaveChangesAsync();

        return TypedResults.Ok(dbSponsors.Sponsors);
    }
}