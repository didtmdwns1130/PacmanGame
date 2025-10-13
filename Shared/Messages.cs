using System.Text;
using Newtonsoft.Json;

namespace Shared
{
    public enum MoveDir { None, Up, Down, Left, Right }

    public class InputCommand
    {
        public MoveDir Dir { get; set; }
        public bool Reset { get; set; }   // ← 추가 (기본값 false)
        public InputCommand() { }
        public InputCommand(MoveDir dir) { Dir = dir; }
    }

    public class PlayerState
    {
        public int Id { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Score { get; set; }
    }

    public class Snapshot
    {
        public long Tick { get; set; }
        public PlayerState[] Players { get; set; } = System.Array.Empty<PlayerState>();

        // 기존 클라 호환용
        public int X { get; set; }
        public int Y { get; set; }

        public bool IsAlive { get; set; }   // 💀 추가된 부분 (서버에서 전송용)

        public Snapshot() { }
        public Snapshot(int x, int y) { X = x; Y = y; }
        public Snapshot(long tick, PlayerState[] players)
        {
            Tick = tick;
            Players = players ?? System.Array.Empty<PlayerState>();
        }
    }

    public static class Msg
    {
        public static byte[] ToBytes<T>(T obj)
            => Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(obj));

        public static T FromBytes<T>(byte[] bytes, int len)
            => JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(bytes, 0, len));
    }
}
