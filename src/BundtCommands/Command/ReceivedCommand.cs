using System.Collections.Generic;
using System.Linq;

namespace BundtBot
{
    public class ReceivedCommand
    {
        public readonly string Name;
        public readonly string ArgumentsString;
        public readonly List<string> Args = new List<string>();

        public ReceivedCommand(string commandString)
        {
            var splitCommand = commandString.Split(' ');

            Name = splitCommand[0];

            if (splitCommand.Length > 1)
            {
                ArgumentsString = commandString.Substring(Name.Length + 1);
                var argsSplit = ArgumentsString.Split(' ');
                argsSplit.ToList().ForEach(x => Args.Add(x));
            }
        }
    }
}