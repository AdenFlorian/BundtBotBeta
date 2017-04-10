using System.Threading.Tasks;

namespace BundtCord.Discord
{
    public interface ITextChannel
	{
        ulong Id { get; }
        string Name { get; }
        ulong ServerId { get; }

        Task<ITextChannelMessage> SendMessageAsync(string content);
    }
}
