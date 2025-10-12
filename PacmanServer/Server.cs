using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Shared;   // 추가 (Messages.cs 안 클래스들을 쓰기 위함)


namespace PacmanServer
{
    public class Server
    {
        private TcpListener listener;

        public async Task StartAsync(int port = 7777)
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            Console.WriteLine($"[SERVER] Listening on port {port}...");

            while (true)
            {
                var client = await listener.AcceptTcpClientAsync();
                _ = HandleClientAsync(client);
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            Console.WriteLine("[SERVER] Client connected");

            using (var stream = client.GetStream())
            {
                var buffer = new byte[1024];
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                var msg = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                Console.WriteLine($"[SERVER] Received: {msg}");

                var reply = Encoding.UTF8.GetBytes("Hello Client!");
                await stream.WriteAsync(reply, 0, reply.Length);
            }

            Console.WriteLine("[SERVER] Client disconnected");
        }
    }
}
