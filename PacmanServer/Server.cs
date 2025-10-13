using Shared;   // 추가 (Messages.cs 안 클래스들을 쓰기 위함)
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;   // ← 추가
using System.Drawing;   // ← 이거 추가

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
                int x = Shared.GameConsts.SpawnX;
                int y = Shared.GameConsts.SpawnY;
                int vx = 0, vy = 0;
                int speed = 10;          // 속도 튜닝
                long tick = 0;

                // --- 게임 상태 / 히트박스 / 벽 목록 ---
                bool isAlive = true;   // 게임오버 시 false

                int pw = 24;           // 플레이어 폭 (픽셀 단위)
                int ph = 24;           // 플레이어 높이

                System.Drawing.Rectangle[] walls = new System.Drawing.Rectangle[]
                {
                    // 스폰 위치 바로 오른쪽에 가로벽 하나 (테스트용)
                    new System.Drawing.Rectangle(Shared.GameConsts.SpawnX + 40, Shared.GameConsts.SpawnY, 200, ph)
                };



                // 👇👇👇 [추가] 유령 히트박스 (임시 예시)
                System.Drawing.Rectangle[] ghosts = new System.Drawing.Rectangle[]
                {
                    new System.Drawing.Rectangle(250, 150, 24, 24)
                };

                // 충돌 헬퍼
                bool Collides(System.Drawing.Rectangle r, System.Drawing.Rectangle[] blockers)
                {
                    for (int i = 0; i < blockers.Length; i++)
                        if (r.IntersectsWith(blockers[i])) return true;
                    return false;
                }




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
                            
                            // 👇👇👇 [추가] 사망 시에는 Reset만 받기
                            if (!isAlive && !cmd.Reset)
                                continue;
                            
                            // ★ Reset 처리: 방향 처리 전에 즉시 초기화
                            if (cmd.Reset)
                            {
                                x = Shared.GameConsts.SpawnX;
                                y = Shared.GameConsts.SpawnY;
                                vx = 0;
                                vy = 0;
                                tick = 0;   // 틱 리셋이 필요 없으면 이 줄은 빼도 된다.
                                isAlive = true; // 👈 리스폰 시 부활
                                continue;   // 아래 방향 처리 스킵
                            }


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
                    await Task.Delay(33);

                    // 👇 사망 시 강제 정지 보장
                    if (!isAlive) { vx = 0; vy = 0; }

                    // 이동 시도 (벽 통과 방지)
                    int nx = x + vx;
                    int ny = y + vy;
                    var nextRect = new Rectangle(nx, ny, pw, ph);

                    // 벽 충돌 시 이동 취소 + 로그
                    if (!Collides(nextRect, walls))
                    {
                        x = nx;
                        y = ny;
                    }
                    else
                    {
                        Console.WriteLine("[SERVER] blocked by wall"); // ← 이 로그가 찍혀야 ‘서버가 막고’ 있다는 증거
                    }
                    tick++;




                    // 👇👇👇 [추가] 이동 직후 유령과 충돌 체크
                    var pacRect = new System.Drawing.Rectangle(x, y, pw, ph);
                    foreach (var g in ghosts)
                    {
                        if (pacRect.IntersectsWith(g))
                        {
                            isAlive = false;   // 💀 사망 처리
                            vx = vy = 0;       // 즉시 정지
                            Console.WriteLine("[SERVER] Player died!");
                            break;
                        }
                    }

                    var snap = new Snapshot
                    {
                        Tick = tick,
                        X = x,           // ← 호환용
                        Y = y,           // ← 호환용
                        Players = new[]
                        {
                            new PlayerState { Id = 1, X = x, Y = y, Score = 0 }
                        },
                        IsAlive = isAlive
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
