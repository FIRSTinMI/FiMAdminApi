using SlackNet;
using SlackNet.Blocks;
using SlackNet.WebApi;

namespace FiMAdminApi.Services;

public class SlackService(ISlackApiClient? slackClient, IConfiguration configuration, ILogger<SlackService> logger)
{
    public async Task SendMessage(SlackChannel channel, string message, string? buttonText = null, string? buttonUrl = null)
    {
        if (slackClient is null)
        {
            logger.LogError("Tried to send a Slack message but Slack wasn't configured");
            return;
        }
        
        var blocks = new List<Block>();
        if (!string.IsNullOrEmpty(buttonText) && !string.IsNullOrEmpty(buttonUrl))
        {
            blocks.Add(new MarkdownBlock
            {
                Text = message
            });
            blocks.Add(new ActionsBlock
            {
                Elements = [new Button
                {
                    Text = new PlainText(buttonText),
                    Url = buttonUrl
                }]
            });
        }

        await slackClient.Chat.PostMessage(new Message
        {
            Channel = GetSlackChannelId(channel),
            Text = message,
            Parse = ParseMode.Full,
            Blocks = blocks
        });
    }

    private string GetSlackChannelId(SlackChannel channel)
    {
        var val = configuration
            .GetRequiredSection("Slack")
            .GetRequiredSection("Channels")
            .GetValue<string>(channel.ToString());

        if (string.IsNullOrEmpty(val))
            throw new ApplicationException(
                $"Tried to get channel ID for {channel} but it wasn't found in configuration");

        return val;
    }
}

public enum SlackChannel
{
    // Public channel for alerts related specifically to AV
    AvAlerts,
    // Private channel for communications between FiM AV leads and HQ Webcast staff
    AvHqChannel,
    // Public channel for alerts related to the running of an event
    EventAlerts,
}