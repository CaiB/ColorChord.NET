using System;

namespace ColorChord.NET.API
{
    public static class Log
    {
        public static bool EnableDebug { get; set; } = false;

        public static void Debug(string message)
        {
            if (!EnableDebug) { return; }
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("[DBG] " + message);
            Console.ForegroundColor = ConsoleColor.White;
        }

        public static void Info(string message)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("[INF] " + message);
        }

        public static void Warn(string message)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[WRN] " + message);
            Console.ForegroundColor = ConsoleColor.White;
        }

        public static void Error(string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[ERR] " + message);
            Console.ForegroundColor = ConsoleColor.White;
        }

    }
}
