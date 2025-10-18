// PacmanGame/GameClient.cs — C# 7.3 (WELCOME 수신 고정 + RestartMsg)
using Shared;
using System;
using System.Net.Sockets;
using System.Threading;
using MoveDir = Shared.Dir;

namespace PacmanGame
{
    public class GameClient : IDisposable
    {
        public event Action<WelcomeMsg> OnWelcome;
        public event Action<SnapshotMsg> OnSnapshot;
        public bool IsConnected => _tcp != null && _tcp.Connected;

        private TcpClient _tcp;
        private Thread _recvThread;
        private readonly object _sendLock = new object();

        public int MyPlayerId { get; private set; } = -1;

        public void Connect(string host, int port, string nickname)
        {
            _tcp = new TcpClient();
            _tcp.NoDelay = true;
            _tcp.Connect(host, port);

            var s = _tcp.GetStream();
            NetProto.WriteFrame(s, new JoinMsg { Nickname = nickname ?? "Player", ClientVersion = "1.0" });

            _recvThread = new Thread(RecvLoop) { IsBackground = true };
            _recvThread.Start();
        }

        private void RecvLoop()
        {
            try
            {
                var stream = _tcp.GetStream();
                while (_tcp != null && _tcp.Connected)
                {
                    var msg = NetProto.ReadFrame(stream);
                    if (msg == null) break;

                    switch (msg.Type)
                    {
                        case MsgType.WELCOME:
                            {
                                var w = (WelcomeMsg)msg;
                                MyPlayerId = w.YourId;   // ★ 내 ID 저장
                                OnWelcome?.Invoke(w);    // ★ 반드시 이벤트 발행
                                break;
                            }
                        case MsgType.SNAPSHOT:
                            {
                                var s = (SnapshotMsg)msg;
                                OnSnapshot?.Invoke(s);
                                break;
                            }
                            // 필요 시 다른 타입(JOIN/RESTART 등) 추가 처리 가능
                    }
                }
            }
            catch
            {
                // 연결 종료/예외 무시(필요시 로깅)
            }
            finally
            {
                try { _tcp?.Close(); } catch { }
            }
        }

        // 방향 입력 전송 — 서버가 소켓별로 유저를 식별하므로
        // WELCOME 도착 전이어도 전송 가능(서버에서 정상 처리됨)
        public void SendInput(MoveDir dir)
        {
            if (!IsConnected) return;

            try
            {
                var m = new InputMsg { Dir = dir, IsPressed = true };
                lock (_sendLock)
                {
                    NetProto.WriteFrame(_tcp.GetStream(), m);
                }
            }
            catch
            {
                // 연결 종료 중이면 무시
            }
        }

        // 다시 시작 요청
        public void SendRestart()
        {
            if (!IsConnected) return;
            try
            {
                lock (_sendLock)
                {
                    NetProto.WriteFrame(_tcp.GetStream(), new RestartMsg());
                }
            }
            catch
            {
                // 연결 종료 중이면 무시
            }
        }

        public void Dispose()
        {
            try { _tcp?.Close(); } catch { }
            _tcp = null;
        }
    }
}
