using Shared;

namespace PacmanServer
{
    static class Program
    {
        static void Main(string[] args)
        {
            var server = new Server();
            server.Run(GameConsts.DEFAULT_PORT); // ★ 공통 포트 상수 사용
        }
    }
}
