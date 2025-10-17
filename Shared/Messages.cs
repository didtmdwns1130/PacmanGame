// Shared/Messages.cs  — C# 7.3
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace Shared
{
    // 메시지 타입
    public enum MsgType : int { JOIN = 1, WELCOME = 2, INPUT = 3, SNAPSHOT = 4, RESTART = 5 }

    public interface INetMessage { MsgType Type { get; } }

    // -------- 모델 --------
    public enum Dir : int { Right = 0, Left = 1, Up = 2, Down = 3 }

    // 유령 AI 타입
    public enum GhostAI : int { Random = 0, Chase = 1, Flee = 2, Patrol = 3 }

    public struct PlayerState
    {
        public int Id;
        public float X;
        public float Y;
        public Dir Dir;
        public int ColorIndex;
        public int Score;   // [+] 반드시 존재
        public string Nick; // 표시용(선택)
    }

    // 서버가 관리하는 코인 하나의 상태
    public struct CoinState
    {
        public int Id;     // 고유 ID(0..N-1)
        public int X;      // 좌상단 픽셀 좌표
        public int Y;
        public bool Eaten; // 먹혔는지 여부
    }

    // 유령 상태(음수 Id 사용)
    public struct GhostState
    {
        public int Id;     // -1, -2, -3, -4
        public float X;    // 픽셀 좌표
        public float Y;
        public Dir Dir;    // 눈 방향 표시용
        public GhostAI AI; // 서버 내부 AI 식별용
    }

    // -------- 메시지 --------
    public struct JoinMsg : INetMessage
    {
        public MsgType Type => MsgType.JOIN;
        public string Nickname;
        public string ClientVersion;
    }

    public struct WelcomeMsg : INetMessage
    {
        public MsgType Type => MsgType.WELCOME;
        public int YourId;
        public int ColorIndex;
    }

    // 입력 메시지
    public struct InputMsg : INetMessage
    {
        public MsgType Type => MsgType.INPUT;
        public Dir Dir;
        public bool IsPressed;
    }

    public struct SnapshotMsg : INetMessage
    {
        public MsgType Type => MsgType.SNAPSHOT;
        public int Tick;
        public List<PlayerState> Players; // Score 포함

        // 전체 코인 상태
        public List<CoinState> Coins;
        public int Round; // 현재 라운드

        // 유령/게임오버
        public List<GhostState> Ghosts; // 없으면 0
        public bool GameOver;
    }

    // 다시 시작 요청
    public struct RestartMsg : INetMessage
    {
        public MsgType Type => MsgType.RESTART;
    }

    // -------- 직렬화 유틸(길이 프레임 + 바이너리) --------
    // [frame] = int32 length | payload(length bytes)
    // payload = int32 type | type별 필드들
    public static class NetProto
    {
        // Write
        public static void WriteFrame(Stream stream, INetMessage msg)
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms, Encoding.UTF8, true))
            {
                bw.Write((int)msg.Type);
                switch (msg.Type)
                {
                    case MsgType.JOIN:
                        {
                            var m = (JoinMsg)msg;
                            bw.Write(m.Nickname ?? "");
                            bw.Write(m.ClientVersion ?? "1.0");
                            break;
                        }
                    case MsgType.WELCOME:
                        {
                            var m = (WelcomeMsg)msg;
                            bw.Write(m.YourId);
                            bw.Write(m.ColorIndex);
                            break;
                        }
                    case MsgType.INPUT:
                        {
                            var m = (InputMsg)msg;
                            bw.Write((int)m.Dir);
                            bw.Write(m.IsPressed);
                            break;
                        }
                    case MsgType.SNAPSHOT:
                        {
                            var m = (SnapshotMsg)msg;
                            // --- SNAPSHOT WRITE (서버) ---
                            bw.Write(m.Tick);

                            // Players (ColorIndex → Score → Nick)
                            bw.Write(m.Players?.Count ?? 0);
                            if (m.Players != null)
                            {
                                foreach (var p in m.Players)
                                {
                                    bw.Write(p.Id);
                                    bw.Write(p.X);
                                    bw.Write(p.Y);
                                    bw.Write((int)p.Dir);
                                    bw.Write(p.ColorIndex);
                                    bw.Write(p.Score);        // ★ Score를 Nick보다 먼저!
                                    bw.Write(p.Nick ?? "");
                                }
                            }

                            // Coins
                            bw.Write(m.Coins?.Count ?? 0);
                            if (m.Coins != null)
                            {
                                foreach (var c in m.Coins)
                                {
                                    bw.Write(c.Id);
                                    bw.Write(c.X);
                                    bw.Write(c.Y);
                                    bw.Write(c.Eaten);
                                }
                            }

                            // Ghosts
                            bw.Write(m.Ghosts?.Count ?? 0);
                            if (m.Ghosts != null)
                            {
                                foreach (var g in m.Ghosts)
                                {
                                    bw.Write(g.Id);
                                    bw.Write(g.X);
                                    bw.Write(g.Y);
                                    bw.Write((int)g.Dir);
                                    bw.Write((int)g.AI);
                                }
                            }

                            // 마지막: Round, GameOver
                            bw.Write(m.Round);     // ★ 반드시 전송
                            bw.Write(m.GameOver);
                            break;
                        }
                    case MsgType.RESTART:
                        {
                            // payload 없음
                            break;
                        }
                }
                bw.Flush();
                var payload = ms.ToArray();

                using (var outWriter = new BinaryWriter(stream, Encoding.UTF8, true))
                {
                    outWriter.Write(payload.Length);
                    outWriter.Write(payload);
                    outWriter.Flush();
                }
            }
        }

        // Read (blocking; returns null if stream closed)
        public static INetMessage ReadFrame(Stream stream)
        {
            byte[] lenBuf = ReadExactly(stream, 4);
            if (lenBuf == null) return null;
            int len = BitConverter.ToInt32(lenBuf, 0);
            if (len <= 0 || len > 1024 * 1024) return null; // sanity

            byte[] payload = ReadExactly(stream, len);
            if (payload == null) return null;

            using (var ms = new MemoryStream(payload))
            using (var br = new BinaryReader(ms, Encoding.UTF8, true))
            {
                var type = (MsgType)br.ReadInt32();
                switch (type)
                {
                    case MsgType.JOIN:
                        return new JoinMsg
                        {
                            Nickname = br.ReadString(),
                            ClientVersion = br.ReadString()
                        };

                    case MsgType.WELCOME:
                        return new WelcomeMsg
                        {
                            YourId = br.ReadInt32(),
                            ColorIndex = br.ReadInt32()
                        };

                    case MsgType.INPUT:
                        return new InputMsg
                        {
                            Dir = (Dir)br.ReadInt32(),
                            IsPressed = br.ReadBoolean()
                        };

                    case MsgType.SNAPSHOT:
                        {
                            // --- SNAPSHOT READ (클라) ---
                            var m = new SnapshotMsg();
                            m.Tick = br.ReadInt32();

                            // Players
                            int pc = br.ReadInt32();
                            var players = new List<PlayerState>(pc);
                            for (int i = 0; i < pc; i++)
                            {
                                PlayerState p = default;
                                p.Id = br.ReadInt32();
                                p.X = br.ReadSingle();
                                p.Y = br.ReadSingle();
                                p.Dir = (Dir)br.ReadInt32();
                                p.ColorIndex = br.ReadInt32();
                                p.Score = br.ReadInt32();   // ★ 쓰기와 같은 위치
                                p.Nick = br.ReadString();
                                players.Add(p);
                            }
                            m.Players = players;

                            // Coins
                            int cc = br.ReadInt32();
                            var coins = new List<CoinState>(cc);
                            for (int i = 0; i < cc; i++)
                            {
                                CoinState c = default;
                                c.Id = br.ReadInt32();
                                c.X = br.ReadInt32();
                                c.Y = br.ReadInt32();
                                c.Eaten = br.ReadBoolean();
                                coins.Add(c);
                            }
                            m.Coins = coins;

                            // Ghosts
                            int gc = br.ReadInt32();
                            var ghosts = new List<GhostState>(gc);
                            for (int i = 0; i < gc; i++)
                            {
                                GhostState g = default;
                                g.Id = br.ReadInt32();
                                g.X = br.ReadSingle();
                                g.Y = br.ReadSingle();
                                g.Dir = (Dir)br.ReadInt32();
                                g.AI = (GhostAI)br.ReadInt32();
                                ghosts.Add(g);
                            }
                            m.Ghosts = ghosts;

                            m.Round = br.ReadInt32();  // ★ 반드시 수신
                            m.GameOver = br.ReadBoolean();

                            return m;
                        }

                    case MsgType.RESTART:
                        return new RestartMsg();

                    default:
                        return null;
                }
            }
        }

        private static byte[] ReadExactly(Stream s, int n)
        {
            var buf = new byte[n];
            int off = 0;
            while (off < n)
            {
                int r = s.Read(buf, off, n - off);
                if (r <= 0) return null;
                off += r;
            }
            return buf;
        }
    }

    // -------- 간단한 확장 --------
    public static class TcpExtensions
    {
        public static NetworkStream SafeStream(this TcpClient c)
            => c.GetStream();

        public static void Send(this TcpClient c, INetMessage msg)
            => NetProto.WriteFrame(c.SafeStream(), msg);
    }
}