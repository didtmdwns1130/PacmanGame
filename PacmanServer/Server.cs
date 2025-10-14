using Newtonsoft.Json;
using Shared;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace PacmanServer
{
    public class Server
    {
        private TcpListener listener;


        private readonly Dictionary<int, TcpClient> clients = new Dictionary<int, TcpClient>();
        private readonly Dictionary<int, MoveDir> inputs = new Dictionary<int, MoveDir>();

        private readonly Dictionary<int, Point> positions = new Dictionary<int, Point>();
        private readonly Dictionary<int, int> scores = new Dictionary<int, int>();
        private readonly Dictionary<int, bool> alive = new Dictionary<int, bool>();

        private const int speed = 12;
        private int nextId = 1;
        private int tick = 0;
        private int round = 1;

        public async Task StartAsync(int port = 7777)
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            Console.WriteLine($"[SERVER] Listening on port {port}");

            _ = Task.Run(GameLoop);

            while (true)
            {
                var client = await listener.AcceptTcpClientAsync();
                client.NoDelay = true; // ← 입력 전송 지연 감소

                int id;
                lock (clients)
                {
                    // 4명 제한
                    if (clients.Count >= 4)
                    {
                        Console.WriteLine("[SERVER] Rejecting connection: server full (>=4).");
                        try { client.Close(); } catch { }
                        continue;
                    }

                    id = nextId++;
                    clients[id] = client;
                    inputs[id] = MoveDir.None;
                    positions[id] = new Point(100 * id, 100 * id);
                    scores[id] = 0;
                    alive[id] = true;
                }

                Console.WriteLine($"[SERVER] Player {id} connected");

                _ = HandleClientAsync(client, id);
            }
        }


        private async Task HandleClientAsync(TcpClient c, int id)
        {
            using (c)
            using (var stream = c.GetStream())
            using (var rd = new StreamReader(stream, new UTF8Encoding(false)))
            using (var wr = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true })
            {
                // 접속 직후 WELCOME 전송 (클라가 PlayerId 저장)
                var welcome = new WelcomeMsg { PlayerId = id };
                await wr.WriteLineAsync(JsonConvert.SerializeObject(welcome));
                Console.WriteLine($"[SERVER] Sent WELCOME to Player {id}");

                while (true)
                {
                    string line;
                    try
                    {
                        line = await rd.ReadLineAsync();
                    }
                    catch (IOException) { break; }             // 소켓 끊김
                    catch (ObjectDisposedException) { break; }  // 스트림 이미 정리됨
                    if (line == null) break;                    // 정석 종료
                    if (line.Length == 0) continue;             // 빈 줄 무시

                    InputMsg msg = null;
                    try { msg = JsonConvert.DeserializeObject<InputMsg>(line); }
                    catch { continue; }                         // 파싱 실패(쓰레기 라인) 무시

                    if (msg != null && msg.Type == "INPUT")
                    {
                        lock (clients)
                        {
                            if (inputs.ContainsKey(id))         // 이미 제거된 뒤 들어오는 레이스 방지
                                inputs[id] = msg.Dir;
                        }
                    }
                }
            }

            lock (clients)
            {
                if (clients.TryGetValue(id, out var sock))
                {
                    try { sock.Client?.Shutdown(SocketShutdown.Both); } catch { }
                    try { sock.Close(); } catch { }
                }

                clients.Remove(id);
                inputs.Remove(id);
                positions.Remove(id);
                scores.Remove(id);
                alive.Remove(id);
            }


            Console.WriteLine($"[SERVER] Player {id} disconnected");
        }

        private async Task GameLoop()
        {
            while (true)
            {
                tick++;
                lock (clients) // ← 위와 일관되게 하나의 락으로 상태 접근
                {
                    foreach (var id in inputs.Keys)
                    {
                        if (!alive[id]) continue;
                        var dir = inputs[id];
                        var pos = positions[id];
                        int x = pos.X;
                        int y = pos.Y;

                        switch (dir)
                        {
                            case MoveDir.Left: x -= speed; break;
                            case MoveDir.Right: x += speed; break;
                            case MoveDir.Up: y -= speed; break;
                            case MoveDir.Down: y += speed; break;
                        }

                        // 화면 워프
                        if (x < -30) x = 780;
                        if (x > 800) x = -20;
                        if (y < -30) y = 580;
                        if (y > 600) y = -20;

                        positions[id] = new Point(x, y);
                    }
                }

                BroadcastSnapshot();

                await Task.Delay(50); // 20 FPS
            }
        }

        private void BroadcastSnapshot()
        {
            var snap = new Snapshot
            {
                Tick = tick,
                Round = round
            };
            
            lock (clients) // ← 마스터 락 사용 (clients/inputs/positions/scores/alive 일관 접근)
            {
                foreach (var kv in positions)
                {
                    snap.Players.Add(new PlayerState
                    {
                        Id = kv.Key,
                        X = kv.Value.X,
                        Y = kv.Value.Y,
                        Score = scores[kv.Key],
                        IsAlive = alive[kv.Key]
                    });
                }
            }

            string json = JsonConvert.SerializeObject(snap) + "\n";
            byte[] buf = Encoding.UTF8.GetBytes(json);

            // 1) 소켓 스냅샷만 복사하고 락 해제
            KeyValuePair<int, TcpClient>[] sockets;
            lock (clients)
            {
                sockets = clients.ToArray();
            }

            // 2) 락 없이 전송 수행
            var toRemove = new List<int>();
            foreach (var kv in sockets)
            {
                var id = kv.Key;
                var sock = kv.Value;
                try
                {
                    sock.GetStream().Write(buf, 0, buf.Length);
                }
                catch
                {
                    toRemove.Add(id); // 전송 실패 → 제거 후보
                }
            }

            // 3) 실패한 클라이언트만 락 잡고 정리
            if (toRemove.Count > 0)
            {
                lock (clients)
                {
                    foreach (var id in toRemove)
                    {
                        if (!clients.ContainsKey(id)) continue;
                        try { clients[id].Client?.Shutdown(SocketShutdown.Both); } catch { }
                        try { clients[id].Close(); } catch { }
                        clients.Remove(id);
                        inputs.Remove(id);
                        positions.Remove(id);
                        scores.Remove(id);
                        alive.Remove(id);
                        Console.WriteLine($"[SERVER] Player {id} removed due to send failure");
                    }
                }
            }


        }
    }
}
