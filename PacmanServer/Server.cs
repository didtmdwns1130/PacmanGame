using Shared;   // 추가 (Messages.cs 안 클래스들을 쓰기 위함)
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;   // ← 추가

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

        // ==== 여기 메서드 본문을 전부 교체 ====
        private async Task HandleClientAsync(TcpClient client)
        {
            Console.WriteLine("[SERVER] Client connected");

            var utf8NoBom = new UTF8Encoding(false);
            using (var stream = client.GetStream())
            using (var rd = new StreamReader(stream, utf8NoBom))
            using (var wr = new StreamWriter(stream, utf8NoBom) { AutoFlush = true })
            {
                // ---- 아주 단순한 플레이어 상태 ----
                int x = 300, y = 300;
                int vx = 0, vy = 0;
                int speed = 4;
                long tick = 0;

                // 입력 읽기 루프(백그라운드): {"Dir": n} 수신 → 속도 벡터만 갱신
                var readTask = Task.Run(async () =>
                {
                    string line;
                    while ((line = await rd.ReadLineAsync()) != null)
                    {
                        Console.WriteLine($"[SERVER] Received: {line}");
                        try
                        {
                            var cmd = JsonConvert.DeserializeObject<InputCommand>(line);
                            if (cmd == null) continue;

                            vx = vy = 0;
                            switch (cmd.Dir)
                            {
                                case MoveDir.Up: vy = -speed; break;
                                case MoveDir.Down: vy = speed; break;
                                case MoveDir.Left: vx = -speed; break;
                                case MoveDir.Right: vx = speed; break;
                                case MoveDir.None:
                                default:
                                    break;
                            }
                        }
                        catch
                        {
                            // 잘못된 라인은 무시
                        }
                    }
                });

                // 전송 루프(메인): 50ms마다 위치 갱신 후 Snapshot 전송
                while (client.Connected)
                {
                    await Task.Delay(50);

                    x += vx;
                    y += vy;
                    tick++;

                    var snap = new Snapshot
                    {
                        Tick = tick,
                        X = x,           // ← 호환용
                        Y = y,           // ← 호환용
                        Players = new[]
                        {
                            new PlayerState { Id = 1, X = x, Y = y, Score = 0 }
                        }
                    };


                    string json = JsonConvert.SerializeObject(snap);
                    await wr.WriteLineAsync(json);

                    // 선택: 서버 전송 로그
                    // Console.WriteLine($"[SERVER] Sent tick={tick} x={x} y={y}");
                }

                // 입력 루프 종료 대기
                await readTask;
            }

            Console.WriteLine("[SERVER] Client disconnected");
        }
        // ==== 교체 끝 ====
    }
}
