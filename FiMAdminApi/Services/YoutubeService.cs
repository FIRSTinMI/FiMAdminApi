using FiMAdminApi.Models.Models;
using Google.Apis.Auth.OAuth2;

namespace FiMAdminApi.Services;


public class YoutubeService(IConfiguration configuration, ILogger<EventStreamService> logger)
{
    public async Task CreateLivestreamEvent(String name, DateTime scheduledStart, DateTime scheduledEnd)
    {
        logger.LogError("Not yet implemented");
    }
}