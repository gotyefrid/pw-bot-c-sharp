using BotCH.MemoryHelpers.Offsets;

namespace BotCH.MemoryHelpers
{
    public class Offset
    {
        public const string COMEBACK136 = "comeback136";

        public static string ServerName;
        public static OffsetTemplate Get
        {
            get {
                if (ServerName == COMEBACK136)
                {
                    return new Comeback136();
                }

                return null;
            }
        }
    }
}
