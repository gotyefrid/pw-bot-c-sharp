using BotCH.MemoryHelpers.Offsets;

namespace BotCH.MemoryHelpers
{
    public class Offset
    {
        public const string COMEBACK136 = "comeback136";
        public const string COMEBACK146X = "comeback146X";
        public const string PWCLASSIC136 = "pwclassic136";

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

                if (ServerName == PWCLASSIC136)
                {
                    return new PwClassic136();
                }

                return null;
            }
        }
    }
}
