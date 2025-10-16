// PacmanServer/Server.cs — C# 7.3
// 요구사항: 시작 중앙 정지, Play 눌러도 정지, '사용자 방향키(키다운)' 입력부터 이동 시작
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Shared; // NetProto, JoinMsg, WelcomeMsg, InputMsg, SnapshotMsg, PlayerState, Dir, CoinState

namespace PacmanServer
{
    public class Server : IDisposable
    {
        private TcpListener _listener;
        private readonly Dictionary<int, TcpClient> _clients = new Dictionary<int, TcpClient>();

        // 서버 권위 상태
        private readonly Dictionary<int, PlayerState> _players = new Dictionary<int, PlayerState>();
        private readonly Dictionary<int, Dir> _lastDir = new Dictionary<int, Dir>();
        private readonly Dictionary<int, bool> _moving = new Dictionary<int, bool>(); // 이동 중 여부
        private int _nextId = 1;
        private readonly object _lock = new object();

        private CancellationTokenSource _loopCts;

        // 맵/속도/틱
        private const int MAP_W = 600;
        private const int MAP_H = 600;
        private const int SPRITE = 44;

        // (A) 속도: 라운드 보너스 기반
        private const int BASE_SPEED = 10; // ★ 기본 속도(체감 ↑)
        private const int MAX_BONUS = 4;  // ★ 라운드 가산 최대치

        private const int TICK_MS = 33;    // ~30fps
        private int _tick;

        // ★ 현재 라운드
        private int _round = 1;            // 이미 있으면 중복 선언 금지

        // ★ 코인/점수 관련
        private const int COIN_W = 20;
        private const int COIN_H = 20;
        private const int COIN_SCORE = 10;
        private readonly List<CoinState> _coins = new List<CoinState>();

        // --- 기존 Run() 호출부 호환용 ---
        public void Run() => Run(7777);
        public void Run(int port)
        {
            Start(port);
            Console.WriteLine($"[SERVER] Running on *:{port}. Press ENTER to stop.");
            Console.ReadLine();
            Stop();
        }

        // --- 서버 수명주기 ---
        public void Start(int port)
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            Console.WriteLine($"[SERVER] Started on *:{port}");

            SeedCoins();   // ★ 코인 한 번만 생성

            _loopCts = new CancellationTokenSource();
            _ = RunLoopAsync(_loopCts.Token); // 틱 루프
            _ = AcceptLoopAsync();            // 접속 수락
        }

        public void Stop()
        {
            try { _loopCts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
        }

        // --- 접속 수락 루프 ---
        private async Task AcceptLoopAsync()
        {
            while (true)
            {
                TcpClient tcp = null;
                try
                {
                    tcp = await _listener.AcceptTcpClientAsync();
                }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SERVER] Accept error: {ex.Message}");
                    continue;
                }

                _ = HandleClientAsync(tcp);
            }
        }

        // --- 클라이언트 핸들러 ---
        private async Task HandleClientAsync(TcpClient tcp)
        {
            try { tcp.NoDelay = true; } catch { }

            int id;
            var rand = new Random(Environment.TickCount ^ (int)DateTime.UtcNow.Ticks);

            // 등록 + 스폰 (항상 정중앙, 정지 상태)
            lock (_lock)
            {
                id = _nextId++;
                _clients[id] = tcp;

                var ps = new PlayerState
                {
                    Id = id,
                    X = (MAP_W - SPRITE) / 2f,
                    Y = (MAP_H - SPRITE) / 2f,
                    Dir = Dir.Right,
                    ColorIndex = rand.Next(0, 8),
                    Nick = "",
                    // Score는 struct 기본값 0
                };
                _players[id] = ps;
                _lastDir[id] = Dir.Right; // 기본 시각화 방향만
                _moving[id] = false;      // 시작은 정지
            }

            Console.WriteLine($"[SERVER] Client #{id} connected.");
            var stream = tcp.GetStream();

            // WELCOME 전송
            try
            {
                PlayerState me;
                lock (_lock) me = _players[id];
                var welcome = new WelcomeMsg { YourId = id, ColorIndex = me.ColorIndex };
                NetProto.WriteFrame(stream, welcome);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SERVER] Send WELCOME failed: {ex.Message}");
            }

            // 수신 루프
            try
            {
                while (tcp.Connected)
                {
                    var msg = NetProto.ReadFrame(stream);
                    if (msg == null) break;

                    switch (msg.Type)
                    {
                        case MsgType.JOIN:
                            {
                                var m = (JoinMsg)msg;
                                lock (_lock)
                                {
                                    if (_players.TryGetValue(id, out var ps))
                                    {
                                        ps.Nick = m.Nickname ?? "";
                                        _players[id] = ps;
                                    }
                                }
                                Console.WriteLine($"[SERVER] #{id} JOIN nick='{m.Nickname}', ver={m.ClientVersion}");
                                break;
                            }

                        case MsgType.INPUT:
                            {
                                var m = (InputMsg)msg;

                                // 키 '다운' 만 의미 있게 처리 (키업/반복 입력은 무시)
                                if (!m.IsPressed) break;

                                lock (_lock)
                                {
                                    bool wasMoving = _moving.TryGetValue(id, out var mv) && mv;

                                    if (!wasMoving)
                                    {
                                        // 첫 사용자 입력으로 이동 시작
                                        _moving[id] = true;
                                        Console.WriteLine($"[INPUT] id={id} START MOVING dir={m.Dir}");
                                    }
                                    else
                                    {
                                        // 이미 이동 중이면 방향만 갱신
                                        // Console.WriteLine($"[INPUT] id={id} CHANGE dir={m.Dir}");
                                    }

                                    _lastDir[id] = m.Dir;

                                    if (_players.TryGetValue(id, out var ps))
                                    {
                                        ps.Dir = m.Dir; // 시각화용
                                        _players[id] = ps;
                                    }
                                }
                                break;
                            }
                    }
                }
            }
            catch { /* 끊김/예외 시 종료 */ }

            // 정리
            try { tcp.Close(); } catch { }
            lock (_lock)
            {
                _clients.Remove(id);
                _players.Remove(id);
                _lastDir.Remove(id);
                _moving.Remove(id);
            }
            Console.WriteLine($"[SERVER] Client #{id} disconnected.");
        }

        // --- 33ms 틱 루프 ---
        private async Task RunLoopAsync(CancellationToken ct)
        {
            var sw = new System.Diagnostics.Stopwatch();
            while (!ct.IsCancellationRequested)
            {
                sw.Restart();
                try
                {
                    StepWorld();
                    BroadcastSnapshot();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[TICK-ERR] " + ex.Message);
                }

                var delay = TICK_MS - (int)sw.ElapsedMilliseconds;
                if (delay < 1) delay = 1;
                try { await Task.Delay(delay, ct); } catch { }
            }
        }

        private void StepWorld()
        {
            lock (_lock)
            {
                var ids = new List<int>(_players.Keys);
                foreach (var id in ids)
                {
                    var ps = _players[id];

                    if (!_moving.TryGetValue(id, out var mv) || !mv)
                        continue; // 이동 시작 전에는 그대로 정지

                    var d = _lastDir.TryGetValue(id, out var dir) ? dir : Dir.Right;

                    int dx = 0, dy = 0;

                    // (B) 라운드 보너스 포함 속도 계산
                    int speed = BASE_SPEED + Math.Min(_round - 1, MAX_BONUS); // ★ 라운드 보너스
                    if (d == Dir.Left) dx = -speed;
                    else if (d == Dir.Right) dx = +speed;
                    else if (d == Dir.Up) dy = -speed;
                    else if (d == Dir.Down) dy = +speed;

                    float nx = ps.X + dx;
                    float ny = ps.Y + dy;

                    // 경계 Clamp + 위치 확정
                    ps.X = Clamp(nx, 0, MAP_W - SPRITE);
                    ps.Y = Clamp(ny, 0, MAP_H - SPRITE);
                    ps.Dir = d;

                    // ★ 코인 충돌 판정(서버 권위)
                    int pacX = (int)ps.X;
                    int pacY = (int)ps.Y;
                    for (int i = 0; i < _coins.Count; i++)
                    {
                        var coin = _coins[i];
                        if (coin.Eaten) continue;
                        if (Intersects(pacX + 6, pacY + 6, SPRITE - 12, SPRITE - 12,
                                       coin.X + 4, coin.Y + 4, COIN_W - 8, COIN_H - 8))
                        {
                            coin.Eaten = true;
                            _coins[i] = coin;
                            ps.Score += COIN_SCORE;
                        }
                    }

                    _players[id] = ps;
                }

                _tick++;

                // (D) 라운드 완료 체크: 코인 모두 먹혔으면 다음 라운드, 코인 원복
                bool allEaten = true;
                for (int i = 0; i < _coins.Count; i++)
                {
                    if (!_coins[i].Eaten) { allEaten = false; break; }
                }
                if (allEaten && _coins.Count > 0)
                {
                    _round++;
                    for (int i = 0; i < _coins.Count; i++)
                    {
                        var c = _coins[i];
                        c.Eaten = false;       // 원상복구
                        _coins[i] = c;
                    }
                    Console.WriteLine($"[SERVER] ROUND UP => #{_round}");
                }

                // 1초마다 샘플 로그(첫 번째 플레이어만)
                if ((_tick % 30) == 0 && _players.Count > 0)
                {
                    foreach (var p in _players.Values)
                    {
                        bool mv = _moving.ContainsKey(p.Id) && _moving[p.Id];
                        Console.WriteLine($"[TICK] id={p.Id} pos=({p.X:F0},{p.Y:F0}) dir={p.Dir} moving={mv} score={p.Score} round={_round}");
                        break;
                    }
                }
            }
        }

        private static float Clamp(float v, float lo, float hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        // --- 스냅샷 브로드캐스트 ---
        private void BroadcastSnapshot()
        {
            SnapshotMsg snap;
            List<TcpClient> clients;
            lock (_lock)
            {
                snap = new SnapshotMsg
                {
                    Tick = _tick,
                    Players = new List<PlayerState>(_players.Values),
                    Coins = new List<CoinState>(_coins),
                    Round = _round, // (C) 현재 라운드도 전송
                };
                clients = new List<TcpClient>(_clients.Values);
            }

            foreach (var tcp in clients)
            {
                if (tcp == null || !tcp.Connected) continue;
                try
                {
                    NetProto.WriteFrame(tcp.GetStream(), snap);
                }
                catch { /* 실패는 다음 틱에서 정리됨 */ }
            }
        }

        // ====== 코인 배치(클라와 동일한 규칙) ======
        private void SeedCoins()
        {
            if (_coins.Count > 0) return;
            int id = 0;

            // 바깥 테두리
            int margin = 26;
            int step = 30;
            for (int x = margin; x <= MAP_W - margin - COIN_W; x += step)
            {
                _coins.Add(new CoinState { Id = id++, X = x, Y = margin, Eaten = false });
                _coins.Add(new CoinState { Id = id++, X = x, Y = MAP_H - margin - COIN_H, Eaten = false });
            }
            for (int y = margin + step; y <= MAP_H - margin - COIN_H - step; y += step)
            {
                _coins.Add(new CoinState { Id = id++, X = margin, Y = y, Eaten = false });
                _coins.Add(new CoinState { Id = id++, X = MAP_W - margin - COIN_W, Y = y, Eaten = false });
            }

            // 내부 4개 블럭(4x4 그리드)
            int blockW = 100, blockH = 100;
            int topY = 150;
            int bottomY = MAP_H - 150 - blockH;
            int leftX = 140;
            int rightX = MAP_W - 140 - blockW;

            AddGrid(leftX, topY, blockW, blockH, 4, 4, ref id);
            AddGrid(rightX, topY, blockW, blockH, 4, 4, ref id);
            AddGrid(leftX, bottomY, blockW, blockH, 4, 4, ref id);
            AddGrid(rightX, bottomY, blockW, blockH, 4, 4, ref id);

            Console.WriteLine($"[SERVER] Seeded coins: {_coins.Count}");
        }

        private void AddGrid(int left, int top, int w, int h, int cols, int rows, ref int id)
        {
            if (cols < 1 || rows < 1) return;
            float gapX = (w - COIN_W) / (float)(cols - 1);
            float gapY = (h - COIN_H) / (float)(rows - 1);

            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    int x = (int)Math.Round(left + c * gapX);
                    int y = (int)Math.Round(top + r * gapY);
                    _coins.Add(new CoinState { Id = id++, X = x, Y = y, Eaten = false });
                }
        }

        private static bool Intersects(int ax, int ay, int aw, int ah, int bx, int by, int bw, int bh)
        {
            return (ax < bx + bw) && (bx < ax + aw) && (ay < by + bh) && (by < ay + ah);
        }

        public void Dispose()
        {
            Stop();
            lock (_lock)
            {
                foreach (var c in _clients.Values)
                {
                    try { c.Close(); } catch { }
                }
                _clients.Clear();
                _players.Clear();
                _lastDir.Clear();
                _moving.Clear();
                _coins.Clear();
            }
        }
    }
}
