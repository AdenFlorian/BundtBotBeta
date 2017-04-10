using System.Threading.Tasks;
using BundtBot.Discord.Models;

namespace BundtCord.Discord
{
    class VoiceChannel : IVoiceChannel
    {
        public ulong Id { get; }
        public string Name { get; }
        public ulong ServerId { get; }

        DiscordClient _client;

        public VoiceChannel(GuildChannel guildChannel, DiscordClient client)
        {
            Id = guildChannel.Id;
            Name = guildChannel.Name;
            ServerId = guildChannel.GuildID;
            _client = client;
        }

        public async Task JoinAsync()
        {
            await _client.JoinVoiceChannel(this);
        }

        public async Task LeaveAsync()
        {
            await _client.LeaveVoiceChannelInServer(ServerId);
        }
    }
}