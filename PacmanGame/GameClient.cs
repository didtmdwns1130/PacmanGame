using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Shared;

namespace PacmanGame
{
    public class GameClient
    {
        private TcpClient tcpClient;

        public async Task ConnectAsync(string host = "127.0.0.1", int port = 7777)
        {
            try
            {
                tcpClient = new TcpClient();
                await tcpClient.ConnectAsync(host, port);
                Console.WriteLine("[CLIENT] Connected to server!");

                using (var stream = tcpClient.GetStream())
                {
                    // 서버에게 메시지 전송
                    var msg = Encoding.UTF8.GetBytes("Hello Server!");
                    await stream.WriteAsync(msg, 0, msg.Length);

                    // 서버 응답 수신
                    var buffer = new byte[1024];
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    var reply = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    Console.WriteLine("[CLIENT] Received: " + reply);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[CLIENT] Connection failed: " + ex.Message);
            }
        }
    }
}
