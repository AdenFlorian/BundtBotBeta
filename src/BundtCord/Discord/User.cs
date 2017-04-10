using BundtBot.Discord.Models;

namespace BundtCord.Discord
{
    public class User : IUser
    {
        public ulong Id { get; }

        DiscordClient _client;

        public User(DiscordUser discordUser, DiscordClient client)
        {
            Id = discordUser.Id;
            _client = client;
        }
    }
}
