// PacmanServer/Server.cs — C# 7.3
// 요구사항: 시작 중앙 정지, Play 눌러도 정지, 그 다음(사용자 방향키) 입력부터 이동 시작
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Shared; // NetProto, JoinMsg, WelcomeMsg, InputMsg, SnapshotMsg, PlayerState, Dir

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
        private readonly Dictionary<int, int> _inputsToIgnore = new Dictionary<int, int>(); // ★ 초기 자동 입력 흡수(기본 2개)
        private int _nextId = 1;
        private readonly object _lock = new object();

        private CancellationTokenSource _loopCts;

        // 맵/속도/틱
        private const int MAP_W = 600;
        private const int MAP_H = 600;
        private const int SPRITE = 44;   // 클라 팩맨 이미지 크기
        private const int SPEED = 6;     // 한 틱당 이동 픽셀
        private const int TICK_MS = 33;  // ~30fps
        private int _tick;

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
            try { tcp.NoDelay = true; } catch { } // 레이턴시 감소

            int id;
            var rand = new Random(Environment.TickCount ^ (int)DateTime.UtcNow.Ticks);

            // 등록 + 스폰
            lock (_lock)
            {
                id = _nextId++;
                _clients[id] = tcp;

                var ps = new PlayerState
                {
                    Id = id,
                    // 항상 정중앙(좌상단 기준, 스프라이트 크기 고려)
                    X = (MAP_W - SPRITE) / 2f,
                    Y = (MAP_H - SPRITE) / 2f,
                    Dir = Dir.Right,
                    ColorIndex = rand.Next(0, 8),
                    Nick = ""
                };
                _players[id] = ps;
                _lastDir[id] = Dir.Right; // 기본 방향만 세팅
                _moving[id] = false;     // 시작은 정지

                // ★ 대부분의 클라는 접속 직후 자동으로 1~2개의 입력(킥스타트/Play)을 쏘므로, 초기 2개는 흡수
                _inputsToIgnore[id] = 2;
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
                                lock (_lock)
                                {
                                    // ★ 초기 자동 입력 흡수 로직
                                    int left = _inputsToIgnore.TryGetValue(id, out var remain) ? remain : 0;
                                    if (left > 0)
                                    {
                                        _inputsToIgnore[id] = left - 1;  // 이번 입력은 소모만 하고
                                        _lastDir[id] = m.Dir;            // 방향은 기억(시각화용)
                                        if (_players.TryGetValue(id, out var psArm))
                                        {
                                            psArm.Dir = m.Dir;
                                            _players[id] = psArm;
                                        }
                                        Console.WriteLine($"[INPUT] id={id} absorbed({left}/2) dir={m.Dir} (still stopped)");
                                        break; // 이동 시작 안 함
                                    }

                                    // ★ 여기부터가 사용자의 '진짜 첫 방향키' 입력
                                    _moving[id] = true;
                                    _lastDir[id] = m.Dir;

                                    if (_players.TryGetValue(id, out var ps))
                                    {
                                        ps.Dir = m.Dir; // 시각화용
                                        _players[id] = ps;
                                    }
                                    Console.WriteLine($"[INPUT] id={id} START MOVING dir={m.Dir}");
                                }
                                break;
                            }
                    }
                }
            }
            catch
            {
                // 끊김/예외 시 종료
            }

            // 정리
            try { tcp.Close(); } catch { }
            lock (_lock)
            {
                _clients.Remove(id);
                _players.Remove(id);
                _lastDir.Remove(id);
                _moving.Remove(id);
                _inputsToIgnore.Remove(id);
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
                // 열거-수정 충돌 방지: 키 스냅샷을 돌면서 값 갱신
                var ids = new List<int>(_players.Keys);
                foreach (var id in ids)
                {
                    var ps = _players[id];

                    // Play/초기 자동 입력 흡수 구간에는 멈춤
                    if (!_moving.TryGetValue(id, out var mv) || !mv)
                    {
                        _players[id] = ps; // 그대로 유지
                        continue;
                    }

                    var d = _lastDir.TryGetValue(id, out var dir) ? dir : Dir.Right;

                    int dx = 0, dy = 0;
                    if (d == Dir.Left) dx = -SPEED;
                    else if (d == Dir.Right) dx = +SPEED;
                    else if (d == Dir.Up) dy = -SPEED;
                    else if (d == Dir.Down) dy = +SPEED;

                    float nx = ps.X + dx;
                    float ny = ps.Y + dy;

                    // 경계 Clamp (스프라이트 크기 고려)
                    ps.X = Clamp(nx, 0, MAP_W - SPRITE);
                    ps.Y = Clamp(ny, 0, MAP_H - SPRITE);
                    ps.Dir = d;

                    _players[id] = ps; // 안전: 지금은 딕셔너리를 열거 중이 아님
                }

                _tick++;

                // 1초마다 샘플 로그
                if ((_tick % 30) == 0 && _players.Count > 0)
                {
                    foreach (var p in _players.Values)
                    {
                        bool mv = _moving.ContainsKey(p.Id) && _moving[p.Id];
                        Console.WriteLine($"[TICK] id={p.Id} pos=({p.X:F0},{p.Y:F0}) dir={p.Dir} moving={mv}");
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
                    Players = new List<PlayerState>(_players.Values)
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
                catch
                {
                    // 실패는 다음 틱에서 정리됨
                }
            }
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
                _inputsToIgnore.Clear();
            }
        }
    }
}
