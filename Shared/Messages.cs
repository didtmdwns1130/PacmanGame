// Shared/NetMessages.cs  — C# 7.3
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace Shared
{
    // 메시지 타입
    public enum MsgType : int { JOIN = 1, WELCOME = 2, INPUT = 3, SNAPSHOT = 4 }

    public interface INetMessage { MsgType Type { get; } }

    // -------- 모델 --------
    public enum Dir : int { Right = 0, Left = 1, Up = 2, Down = 3 }

    public struct PlayerState
    {
        public int Id;
        public float X;
        public float Y;
        public Dir Dir;
        public int ColorIndex;
        public string Nick; // 표시용(선택)
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

    // (다음 단계에서 사용할 예정)
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
        public List<PlayerState> Players;
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
                            bw.Write(m.Tick);
                            int count = m.Players == null ? 0 : m.Players.Count;
                            bw.Write(count);
                            if (count > 0)
                            {
                                for (int i = 0; i < count; i++)
                                {
                                    var p = m.Players[i];
                                    bw.Write(p.Id);
                                    bw.Write(p.X);
                                    bw.Write(p.Y);
                                    bw.Write((int)p.Dir);
                                    bw.Write(p.ColorIndex);
                                    bw.Write(p.Nick ?? "");
                                }
                            }
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
                        {
                            return new JoinMsg
                            {
                                Nickname = br.ReadString(),
                                ClientVersion = br.ReadString()
                            };
                        }
                    case MsgType.WELCOME:
                        {
                            return new WelcomeMsg
                            {
                                YourId = br.ReadInt32(),
                                ColorIndex = br.ReadInt32()
                            };
                        }
                    case MsgType.INPUT:
                        {
                            return new InputMsg
                            {
                                Dir = (Dir)br.ReadInt32(),
                                IsPressed = br.ReadBoolean()
                            };
                        }
                    case MsgType.SNAPSHOT:
                        {
                            int tick = br.ReadInt32();
                            int count = br.ReadInt32();
                            var list = new List<PlayerState>(count);
                            for (int i = 0; i < count; i++)
                            {
                                PlayerState p;
                                p.Id = br.ReadInt32();
                                p.X = br.ReadSingle();
                                p.Y = br.ReadSingle();
                                p.Dir = (Dir)br.ReadInt32();
                                p.ColorIndex = br.ReadInt32();
                                p.Nick = br.ReadString();
                                list.Add(p);
                            }
                            return new SnapshotMsg { Tick = tick, Players = list };
                        }
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
