using System.ComponentModel.DataAnnotations;
using Asp.Versioning.Builder;
using FiMAdminApi.Clients;
using FiMAdminApi.Clients.Models;
using FiMAdminApi.Data;
using FiMAdminApi.Data.Enums;
using FiMAdminApi.Data.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniValidation;

namespace FiMAdminApi.Endpoints;

public static class TruckRoutesEndpoints
{
    public static WebApplication RegisterTruckRotuesEndpoints(this WebApplication app, ApiVersionSet vs)
    {
        var truckRoutesGroup = app.MapGroup("/api/v{apiVersion:apiVersion}/routes").WithApiVersionSet(vs)
            .HasApiVersion(1).WithTags("Truck Routes").RequireAuthorization(nameof(GlobalPermission.Equipment_Manage));

        truckRoutesGroup.MapPost("/", CreateRoute);
        truckRoutesGroup.MapPut("/{id:int}", EditRoute);

        return app;
    }

    private static async Task<Results<Ok<TruckRoute>, ValidationProblem>> CreateRoute(
        [FromBody] CreateTruckRoute request,
        [FromServices] DataContext dbContext)
    {
        var (isValid, errors) = await MiniValidator.TryValidateAsync(request);
        if (!isValid)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var dbRoute = new TruckRoute
        {
            Name = request.Name
        };
        await dbContext.TruckRoutes.AddAsync(dbRoute);
        await dbContext.SaveChangesAsync();

        return TypedResults.Ok(dbRoute);
    }
    
    private static async Task<Results<Ok<TruckRoute>, NotFound, ValidationProblem>> EditRoute(
        [FromRoute] int id,
        [FromBody] EditTruckRoute request,
        [FromServices] DataContext dbContext)
    {
        var (isValid, errors) = await MiniValidator.TryValidateAsync(request);
        if (!isValid)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var dbRoute = await dbContext.TruckRoutes.FirstOrDefaultAsync(r => r.Id == id);
        if (dbRoute is null)
        {
            return TypedResults.NotFound();
        }

        dbRoute.Name = request.Name;
        
        await dbContext.SaveChangesAsync();

        return TypedResults.Ok(dbRoute);
    }

    public class CreateTruckRoute
    {
        [Required]
        public string Name { get; set; }
    }
    
    public class EditTruckRoute
    {
        [Required]
        public string Name { get; set; }
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