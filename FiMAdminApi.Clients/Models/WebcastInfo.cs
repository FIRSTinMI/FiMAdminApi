using FiMAdminApi.Models.Enums;

namespace FiMAdminApi.Clients.Models;

public record WebcastInfo(string Url, DateOnly? Date, StreamPlatform Platform, string? InternalId, string Channel);