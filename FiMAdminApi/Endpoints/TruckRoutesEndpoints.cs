using Asp.Versioning.Builder;
using FiMAdminApi.Clients;
using FiMAdminApi.Clients.Models;
using FiMAdminApi.Data.Enums;
using FiMAdminApi.Data.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace FiMAdminApi.Endpoints;

public static class TruckRoutesEndpoints
{
    public static WebApplication RegisterTruckRotuesEndpoints(this WebApplication app, ApiVersionSet vs)
    {
        var truckRoutesGroup = app.MapGroup("/api/v{apiVersion:apiVersion}/routes").WithApiVersionSet(vs)
            .HasApiVersion(1).WithTags("Truck Routes").RequireAuthorization(nameof(GlobalPermission.Equipment_Manage));

        //truckRoutesGroup.MapGet("/{seasonYear:int}/{teamId:int}", GetTeam);
        truckRoutesGroup.MapGet("/test", TestEndpoint);

        return app;
    }

    private static async Task TestEndpoint([FromServices] BlueAllianceWriteClient client)
    {
        await client.UpdateEventInfo(new Season
        {
            StartTime = new DateTime(2014, 1, 2),
            LevelId = 1,
            Name = "",
            EndTime = DateTime.MaxValue
        }, "casj", []);
    }
    
    // private static async Task<Ok<Team>> GetTeam([FromRoute] int teamId, [FromRoute] int seasonYear, [FromServices] IServiceProvider sp)
    // {
    //     var client = sp.GetKeyedService<IDataClient>("FrcEvents");
    //     
    //     return client.GetTeamsByNumbers(new Season()
    //     {
    //         StartTime = new DateTime(seasonYear, 1, 2)
    //     }, [])
    // }
}