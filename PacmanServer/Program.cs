using System;

namespace PacmanServer
{
    internal static class Program
    {
        static void Main(string[] args)
        {
            var server = new Server();
            server.Run();
        }
    }
}
