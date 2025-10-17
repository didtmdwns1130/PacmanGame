// PacmanServer/Server.cs — C# 7.3
// 요구사항: 시작 중앙 정지, Play 눌러도 정지, '사용자 방향키(키다운)' 입력부터 이동 시작
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Shared; // NetProto, JoinMsg, WelcomeMsg, InputMsg, SnapshotMsg, PlayerState, Dir, CoinState, GhostState, GhostAI, RestartMsg

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

        private const int TICK_MS = 33;    // ~30fps
        private int _tick;

        // ★ 현재 라운드
        private int _round = 1;            // 이미 있으면 중복 선언 금지

        // ★ 4인 하드 제한
        private const int MAX_PLAYERS = 4;

        // ★ 스폰 포인트: 중앙 + 4코너
        private readonly (int x, int y)[] _spawns = new (int x, int y)[]
        {
            ((MAP_W - SPRITE)/2, (MAP_H - SPRITE)/2),                 // 0: Center
            (30, 30),                                                 // 1: Top-Left
            (MAP_W - SPRITE - 30, 30),                                // 2: Top-Right
            (30, MAP_H - SPRITE - 30),                                // 3: Bottom-Left
            (MAP_W - SPRITE - 30, MAP_H - SPRITE - 30)                // 4: Bottom-Right
        };

        // ★ 추가: 게임오버/유령
        private bool _gameOver = false;
        private int _graceTicks = 90; // ★ 시작 그레이스(무적) 타임: 약 3초(= 90틱)
        private readonly Dictionary<int, GhostState> _ghosts = new Dictionary<int, GhostState>(); // key: -1..-4
        // ★ 주황(Patrol) 전용 웨이포인트 인덱스
        private readonly Dictionary<int, int> _patrolIdx = new Dictionary<int, int>();

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

            SeedCoins();    // ★ 코인 한 번만 생성
            SpawnGhosts();  // ★ 유령 4마리 생성
            _graceTicks = 90; // ★ 서버 시작 직후 3초 무적

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

            // ★ 하드 제한: 4인 꽉 차면 즉시 거절
            lock (_lock)
            {
                if (_players.Count >= MAX_PLAYERS)
                {
                    Console.WriteLine("[SERVER] Reject: room full");
                    try { tcp.Close(); } catch { }
                    return;
                }
            }

            // 등록 + 스폰 (첫 접속자는 중앙, 이후 코너 순서)
            lock (_lock)
            {
                id = _nextId++;
                _clients[id] = tcp;

                // 현재 접속자 수(추가 전)로 스폰 선택
                int existing = _players.Count; // 0이면 중앙, 1~4면 코너들
                var spawn = (existing == 0)
                    ? _spawns[0]
                    : _spawns[Math.Min(existing, _spawns.Length - 1)];

                var ps = new PlayerState
                {
                    Id = id,
                    X = spawn.x,
                    Y = spawn.y,
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

                        case MsgType.RESTART:
                            {
                                // ★ 게임오버 상태에서만 전역 리셋 허용
                                lock (_lock)
                                {
                                    if (_gameOver)
                                    {
                                        ResetWorld();
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
                if (_gameOver) return; // 게임오버면 월드 정지(브로드캐스트만)
                if (_graceTicks > 0) _graceTicks--; // 시작/재시작 무적 타임 감소

                var ids = new List<int>(_players.Keys);
                foreach (var id in ids)
                {
                    var ps = _players[id];

                    if (!_moving.TryGetValue(id, out var mv) || !mv)
                        continue; // 이동 시작 전에는 그대로 정지

                    var d = _lastDir.TryGetValue(id, out var dir) ? dir : Dir.Right;

                    int dx = 0, dy = 0;

                    // ★ 라운드 기반 가속 (R1:8, R2:10, R3:12, R4:14, R5+:16)
                    int speed = 8 + Math.Min(4, Math.Max(0, _round - 1)) * 2;

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

                // ★ 그레이스타임 동안엔 유령 정지 + 충돌 비활성
                if (_graceTicks <= 0)
                {
                    StepGhosts();
                    CheckGhostCollisions();
                }

                _tick++;

                // ★ 라운드 완료 체크: 코인 모두 먹혔으면 다음 라운드, 코인 원복
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
                    // 상위 20비트에 라운드, 하위 20비트에 틱(클라 ROUND_SHIFT=20 가정)
                    Tick = (_round << 20) | (_tick & 0xFFFFF),
                    Players = new List<PlayerState>(_players.Values),
                    Coins = new List<CoinState>(_coins),
                    Round = _round, // (C) 현재 라운드도 전송(호환용)
                    Ghosts = new List<GhostState>(_ghosts.Values),
                    GameOver = _gameOver
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

        // ====== 유령 생성/이동/충돌 ======
        private void SpawnGhosts()
        {
            _ghosts.Clear();
            _ghosts[-1] = new GhostState { Id = -1, X = 90, Y = 90, Dir = Dir.Right, AI = GhostAI.Random };
            _ghosts[-2] = new GhostState { Id = -2, X = MAP_W - 90 - 40, Y = 90, Dir = Dir.Left, AI = GhostAI.Chase };
            _ghosts[-3] = new GhostState { Id = -3, X = 90, Y = MAP_H - 90 - 40, Dir = Dir.Up, AI = GhostAI.Flee };
            _ghosts[-4] = new GhostState { Id = -4, X = MAP_W - 90 - 40, Y = MAP_H - 90 - 40, Dir = Dir.Down, AI = GhostAI.Patrol };
            _patrolIdx[-4] = 0;  // ★ 사각형 코스 시작점
        }

        private void StepGhosts()
        {
            // ★ 하향조정: 기본 4~5 (라운드 올라도 과속 방지)
            int baseSpeed = 4 + Math.Min(1, Math.Max(0, _round - 1)); // 4~5

            // AI 별 가중치
            int SpeedFor(GhostAI ai)
            {
                switch (ai)
                {
                    case GhostAI.Chase: return Math.Max(3, baseSpeed - 1); // 파란: 항상 느리게
                    case GhostAI.Random: return Math.Max(3, baseSpeed - 1);
                    case GhostAI.Flee: return Math.Max(3, baseSpeed - 1);
                    case GhostAI.Patrol: return Math.Max(2, baseSpeed - 2); // 주황: 가장 느리게
                    default: return baseSpeed;
                }
            }

            foreach (var id in new List<int>(_ghosts.Keys))
            {
                var g = _ghosts[id];
                int dx = 0, dy = 0;

                switch (g.AI)
                {
                    case GhostAI.Random:
                        {
                            if ((_tick % 15) == 0)
                            {
                                var r = new Random((int)(_tick + id * 997));
                                g.Dir = (Dir)r.Next(0, 4);
                            }
                            (dx, dy) = DirToDelta(g.Dir, SpeedFor(g.AI));
                            break;
                        }
                    case GhostAI.Chase:
                        {
                            // ★ 체감 속도 반으로: 짝수 틱에만 이동
                            if ((_tick & 1) == 1) { _ghosts[id] = g; continue; }

                            if (_players.Count > 0)
                            {
                                int target = NearestPlayerTo(g.X, g.Y);
                                if (target >= 0)
                                {
                                    var p = _players[target];
                                    g.Dir = BestDirToward(g, p.X, p.Y);
                                }
                            }
                            (dx, dy) = DirToDelta(g.Dir, SpeedFor(g.AI));
                            break;
                        }
                    case GhostAI.Flee:
                        {
                            if (_players.Count > 0)
                            {
                                int target = NearestPlayerTo(g.X, g.Y);
                                if (target >= 0)
                                {
                                    var p = _players[target];
                                    var toward = BestDirToward(g, p.X, p.Y);
                                    g.Dir = Opposite(toward);
                                }
                            }
                            (dx, dy) = DirToDelta(g.Dir, SpeedFor(g.AI));
                            break;
                        }
                    case GhostAI.Patrol:
                        {
                            // ★ 사각형 웨이포인트 순찰 (벽 들러붙음 제거)
                            const int M = 72; // 테두리에서 한 칸 안쪽
                            float left = M;
                            float right = MAP_W - M - SPRITE;
                            float top = M;
                            float bottom = MAP_H - M - SPRITE;
                            // 4개 코너(시계방향)
                            float[,] pt = new float[,] {
                                { left,  top    }, { right, top    },
                                { right, bottom }, { left,  bottom }
                            };
                            int idx = _patrolIdx.TryGetValue(id, out var v) ? v : 0;
                            float tx = pt[idx, 0], ty = pt[idx, 1];

                            int sp = SpeedFor(g.AI);
                            (dx, dy) = MoveToward(g.X, g.Y, tx, ty, sp);
                            g.Dir = DirFromDelta(dx, dy);
                            float nx = g.X + dx, ny = g.Y + dy;
                            // 목표점에 거의 도달하면 다음 코너로
                            if (Dist2(nx, ny, tx, ty) <= (sp * sp))
                            {
                                idx = (idx + 1) & 3;
                                _patrolIdx[id] = idx;
                            }
                            g.X = Clamp(nx, left, right);
                            g.Y = Clamp(ny, top, bottom);
                            _ghosts[id] = g;
                            continue; // ★ 아래 공통 클램프는 생략
                        }
                }

                g.X = Clamp(g.X + dx, 0, MAP_W - 40);
                g.Y = Clamp(g.Y + dy, 0, MAP_H - 40);
                _ghosts[id] = g;
            }
        }

        private void CheckGhostCollisions()
        {
            foreach (var ps in _players.Values)
            {
                int ax = (int)ps.X + 4, ay = (int)ps.Y + 4, aw = SPRITE - 8, ah = SPRITE - 8;
                foreach (var g in _ghosts.Values)
                {
                    int bx = (int)g.X + 2, by = (int)g.Y + 2, bw = 36, bh = 36; // 유령 40x40 기준 살짝 작은 히트박스
                    if (Intersects(ax, ay, aw, ah, bx, by, bw, bh))
                    {
                        _gameOver = true;
                        return;
                    }
                }
            }
        }

        private int NearestPlayerTo(float x, float y)
        {
            int best = -1;
            float bestD2 = float.MaxValue;
            foreach (var kv in _players)
            {
                var p = kv.Value;
                float dx = p.X - x, dy = p.Y - y;
                float d2 = dx * dx + dy * dy;
                if (d2 < bestD2) { bestD2 = d2; best = kv.Key; }
            }
            return best >= 0 ? best : -1;
        }

        private (int dx, int dy) DirToDelta(Dir d, int speed)
        {
            switch (d)
            {
                case Dir.Left: return (-speed, 0);
                case Dir.Right: return (speed, 0);
                case Dir.Up: return (0, -speed);
                case Dir.Down: return (0, speed);
                default: return (0, 0);
            }
        }

        private Dir BestDirToward(GhostState g, float tx, float ty)
        {
            float dx = tx - g.X, dy = ty - g.Y;
            if (Math.Abs(dx) > Math.Abs(dy))
                return dx >= 0 ? Dir.Right : Dir.Left;
            else
                return dy >= 0 ? Dir.Down : Dir.Up;
        }

        private Dir Opposite(Dir d)
        {
            switch (d)
            {
                case Dir.Left: return Dir.Right;
                case Dir.Right: return Dir.Left;
                case Dir.Up: return Dir.Down;
                case Dir.Down: return Dir.Up;
                default: return d;
            }
        }

        private Dir NextClockwise(Dir d)
        {
            switch (d)
            {
                case Dir.Up: return Dir.Right;
                case Dir.Right: return Dir.Down;
                case Dir.Down: return Dir.Left;
                case Dir.Left: return Dir.Up;
                default: return Dir.Right;
            }
        }

        private static bool Intersects(int ax, int ay, int aw, int ah, int bx, int by, int bw, int bh)
        {
            return (ax < bx + bw) && (bx < ax + aw) && (ay < by + bh) && (by < ay + ah);
        }

        private void ResetWorld()
        {
            // 라운드/틱/게임오버 초기화
            _round = 1;
            _tick = 0;
            _gameOver = false;
            _graceTicks = 90; // ★ 재시작 후 3초간 충돌 무시

            // 플레이어 위치/이동 초기화
            foreach (var id in new List<int>(_players.Keys))
            {
                var ps = _players[id];
                ps.X = (MAP_W - SPRITE) / 2f;
                ps.Y = (MAP_H - SPRITE) / 2f;
                ps.Dir = Dir.Right;
                ps.Score = 0; // 점수 초기화(필요 없다면 주석)
                _players[id] = ps;
                _moving[id] = false;
                _lastDir[id] = Dir.Right;
            }

            // 코인 초기화
            for (int i = 0; i < _coins.Count; i++)
            {
                var c = _coins[i];
                c.Eaten = false;
                _coins[i] = c;
            }

            // 유령 재생성
            SpawnGhosts();
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
                _ghosts.Clear();
            }
        }

        // === 보조 함수들 (부동소수 → 정수 이동량 산출) ===
        private (int dx, int dy) MoveToward(float x, float y, float tx, float ty, int speed)
        {
            double vx = tx - x, vy = ty - y;
            double d = Math.Sqrt(vx * vx + vy * vy);
            if (d < 1e-6) return (0, 0);
            // 속도가 남아서 목표를 오버슈트하지 않게
            double step = Math.Min(speed, d);
            int dx = (int)Math.Round(vx / d * step);
            int dy = (int)Math.Round(vy / d * step);
            // 최소 한 픽셀이라도 움직이도록 보정
            if (dx == 0 && Math.Abs(vx) > 0.1) dx = (vx > 0 ? 1 : -1);
            if (dy == 0 && Math.Abs(vy) > 0.1) dy = (vy > 0 ? 1 : -1);
            return (dx, dy);
        }

        private Dir DirFromDelta(int dx, int dy)
        {
            if (Math.Abs(dx) >= Math.Abs(dy))
                return dx >= 0 ? Dir.Right : Dir.Left;
            else
                return dy >= 0 ? Dir.Down : Dir.Up;
        }

        private double Dist2(float x1, float y1, float x2, float y2)
        {
            double dx = x2 - x1, dy = y2 - y1;
            return dx * dx + dy * dy;
        }
    }
}
