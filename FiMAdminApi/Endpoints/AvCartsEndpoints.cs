using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Asp.Versioning.Builder;
using FiMAdminApi.Data.EfPgsql;
using FiMAdminApi.Models.Enums;
using FiMAdminApi.Models.Models;
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
            .RequireAuthorization(nameof(GlobalPermission.Equipment_Manage));

        matchesGroup.MapPut("/{cartId:guid:required}/stream-info", UpdateStreamInfo);

        return app;
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

    public class StreamInfo
    {
        public required string RtmpUrl { get; set; }
        public required string RtmpKey { get; set; }
        public required bool Enabled { get; set; }
    }
}