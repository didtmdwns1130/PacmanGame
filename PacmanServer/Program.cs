using System;
using Shared;   // 추가 (Messages.cs 안 클래스들을 쓰기 위함)

namespace PacmanServer
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            try
            {
                var server = new Server();
                // 7777 포트에서 대기 (원하면 숫자 바꿔도 됨)
                server.StartAsync(7777).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SERVER] Fatal: " + ex);
                Console.ReadLine();
            }
        }
    }
}
