using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Asp.Versioning.Builder;
using FiMAdminApi.Data.EfPgsql;
using FiMAdminApi.Helpers;
using FiMAdminApi.Models.Enums;
using FiMAdminApi.Models.Models;
using FiMAdminApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FiMAdminApi.Endpoints;

public static class AvCartsEndpoints
{
    public static WebApplication RegisterAvCartsEndpoints(this WebApplication app, ApiVersionSet vs)
    {
        var matchesGroup = app.MapGroup("/api/v{apiVersion:apiVersion}/av-carts")
            .WithTags("AV Carts").WithApiVersionSet(vs).HasApiVersion(1)
            .RequireAuthorization(nameof(GlobalPermission.Equipment_Av_ManageStream));

        matchesGroup.MapGet("/{cartId:guid:required}/vmix-config", GetVmixConfig);
        matchesGroup.MapPut("/{cartId:guid:required}/slack-status", UpdateSlackStatus);
        matchesGroup.MapPut("/{cartId:guid:required}/stream-info", UpdateStreamInfo);
        matchesGroup.MapPut("/{cartId:guid:required}/stream/start", StartStream);
        matchesGroup.MapPut("/{cartId:guid:required}/stream/stop", StopStream);
        matchesGroup.MapPut("/{cartId:guid:required}/stream/push-keys", PushStreamKeys);

        return app;
    }
    
    private static async Task<Results<Ok<string>, NoContent>> GetVmixConfig(
        [FromRoute] Guid cartId,
        [FromServices] AvCartService service,
        HttpContext httpContext)
    {
        var resp = await service.GetVmixConfig(cartId);

        if (resp == null) return TypedResults.NoContent();

        httpContext.Response.ContentType = "text/json";
        return TypedResults.Ok(resp);
    }

    private static async Task<Results<Ok, NotFound, ForbidHttpResult>> UpdateStreamInfo([FromRoute] Guid cartId,
        [FromBody] [MaxLength(5)] StreamInfo[] streamInfo, [FromServices] DataContext dataContext,
        [FromServices] IAuthorizationService authSvc, ClaimsPrincipal user)
    {
        var cart = await dataContext.AvCarts.FirstOrDefaultAsync(e => e.Id == cartId);

        if (cart is null) return TypedResults.NotFound();

        cart.Configuration ??= new AvConfiguration();

        cart.Configuration.StreamInfo = streamInfo.Select((i, idx) => new AvConfiguration.StreamInformation
        {
            Index = idx,
            CartId = cartId,
            Enabled = i.Enabled,
            RtmpUrl = i.RtmpUrl,
            RtmpKey = i.RtmpKey
        }).ToList();
        
        await dataContext.SaveChangesAsync();

        return TypedResults.Ok();
    }

    private static async Task<Ok> StartStream(
        [FromRoute] Guid cartId,
        [FromQuery] int? streamNum,
        [FromServices] AvCartService service)
    {
        await service.StartStream(cartId, streamNum);

        return TypedResults.Ok();
    }
    
    private static async Task<Ok> StopStream(
        [FromRoute] Guid cartId,
        [FromQuery] int? streamNum,
        [FromServices] AvCartService service)
    {
        await service.StopStream(cartId, streamNum);

        return TypedResults.Ok();
    }
    
    private static async Task<Ok> PushStreamKeys(
        [FromRoute] Guid cartId,
        [FromServices] AvCartService service)
    {
        await service.PushStreamKeys(cartId);

        return TypedResults.Ok();
    }

    private static async Task<Results<Ok, NotFound>> UpdateSlackStatus(
        [FromRoute] Guid cartId,
        [FromBody] UpdateSlackStatusRequest request,
        [FromServices] DataContext dbContext,
        [FromServices] SlackService slackService)
    {
        var cart = await dbContext.AvCarts.Where(c => c.Id == cartId).FirstOrDefaultAsync();
        if (cart is null || string.IsNullOrEmpty(cart.SlackUserId)) return TypedResults.NotFound();

        var name = request.EventNameOverride;
        if (name is null)
        {
            var evt = await dbContext.Events
                .Where(e => e.TruckRouteId != null && dbContext.AvCarts.Any(c => c.TruckRouteId == e.TruckRouteId && c.Id == cartId))
                .FirstOrDefaultAsync();

            name = evt?.Name ?? "";
        }
        
        var numberMatch = EventNameHelperRegexes.EquipmentNumber().Match(cart.Name);

        var slackName = $"FIM AV {numberMatch.Groups[1].Value}";
        if (!string.IsNullOrEmpty(name))
        {
            slackName += $" [{name}]";
        }
        
        await slackService.SetEventInformationForUser(cart.SlackUserId, slackName);

        return TypedResults.Ok();
    }

    public class StreamInfo
    {
        public required string RtmpUrl { get; set; }
        public required string RtmpKey { get; set; }
        public required bool Enabled { get; set; }
    }

    public record UpdateSlackStatusRequest(string? EventNameOverride);
}