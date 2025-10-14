// PacmanClient/GameClient.cs
using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Shared; // MoveDir, InputMsg, Snapshot 

namespace PacmanGame
{
    public class GameClient : IDisposable
    {
        private TcpClient _tcp;
        private StreamReader _reader;
        private StreamWriter _writer;

        // 서버에서 내 PlayerId를 알려줄 때 사용
        public int MyPlayerId { get; private set; } = -1;
        public event Action<int> WelcomeReceived;

        private CancellationTokenSource _cts;

        private Shared.MoveDir _currentDir = Shared.MoveDir.None; // ← 충돌 방지 위해 완전수식
        private System.Timers.Timer _inputTimer;

        public event Action<Shared.Snapshot> SnapshotReceived; // ← 타입 명확화
        public bool IsConnected => _tcp?.Connected == true;

        public async Task StartAsync(string host = "127.0.0.1", int port = 7777)
        {
            _cts = new CancellationTokenSource();
            _tcp = new TcpClient();
            await _tcp.ConnectAsync(host, port);

            var stream = _tcp.GetStream();
            var utf8NoBom = new UTF8Encoding(false);
            _reader = new StreamReader(stream, utf8NoBom);
            _writer = new StreamWriter(stream, utf8NoBom) { AutoFlush = true };


            // 수신 루프
            _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));

            // 입력 펌프: 50ms마다 현재 방향을 서버에 전송
            _inputTimer = new System.Timers.Timer(50);
            _inputTimer.Elapsed += (_, __) => SendInput();
            _inputTimer.AutoReset = true;
            _inputTimer.Start();
        }

        public void SetCurrentDir(Shared.MoveDir dir) => _currentDir = dir; // ← 외부에서 방향 설정

        private void SendInput()
        {
            if (!IsConnected) return;

            try
            {
                // ✅ PlayerId 포함하여 전송
                var json = JsonConvert.SerializeObject(new Shared.InputMsg
                {
                    PlayerId = MyPlayerId,   // ← 핵심
                    Dir = _currentDir
                });

                _writer.WriteLine(json);
            }
            catch
            {
                // 끊김 등은 수신 루프/Dispose에서 처리
            }
        }


        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var line = await _reader.ReadLineAsync();
                    if (line == null) break; // 서버가 정말로 연결을 닫았을 때만 종료

                    try
                    {
                        // === 1) WELCOME 메시지 확인 ===
                        if (line.Contains("\"WELCOME\""))
                        {
                            var w = JsonConvert.DeserializeObject<Shared.WelcomeMsg>(line);
                            if (w != null && w.Type == "WELCOME")
                            {
                                MyPlayerId = w.PlayerId;
                                WelcomeReceived?.Invoke(MyPlayerId);
                                continue; // 다음 줄로
                            }
                        }

                        // === 2) SNAPSHOT 메시지 ===
                        if (line.Contains("\"SNAPSHOT\""))
                        {
                            var snap = JsonConvert.DeserializeObject<Shared.Snapshot>(line);
                            if (snap != null)
                                SnapshotReceived?.Invoke(snap);
                        }
                    }
                    catch
                    {
                        // 잘못된 라인(비JSON)은 무시하고 다음 줄 대기 → 연결 유지
                    }

                }
            }
            finally
            {
                Dispose();
            }
        }


        public void Dispose()
        {
            try
            {
                _inputTimer?.Stop();
                _inputTimer?.Dispose();
                _cts?.Cancel();
                _reader?.Dispose();
                _writer?.Dispose();
                _tcp?.Close();
            }
            catch { }
        }
    }
}
