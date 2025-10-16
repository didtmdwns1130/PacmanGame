// PacmanServer/Program.cs  — 교체본
using System;

namespace PacmanServer
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.Title = "PacmanServer";
            // ★ Server.cs 안의 서버 구현을 구동 (33ms 틱, lastDir, SNAPSHOT 브로드캐스트)
            var server = new Server();
            server.Run(9000);   // ★ 클라이언트가 접속하는 포트와 맞추세요(지금 9000)
        }
    }
}
