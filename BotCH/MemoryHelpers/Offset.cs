using BotCH.MemoryHelpers.Offsets;

namespace BotCH.MemoryHelpers
{
    public class Offset
    {
        public const string COMEBACK136 = "comeback136";
        public const string COMEBACK146X = "comeback146X";

        public static string ServerName;
        public static OffsetTemplate Get
        {
            get {
                if (ServerName == COMEBACK136)
                {
                    return new Comeback136();
                }

                if (ServerName == COMEBACK146X)
                {
                    return new Comeback146X();
                }

                return null;
            }
        }
    }
}
