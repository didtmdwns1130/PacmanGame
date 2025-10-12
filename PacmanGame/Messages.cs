// Messages.cs  (서버/클라 둘 다 동일 파일)
using System.Text;
using Newtonsoft.Json;

namespace Shared   // ← 같은 네임스페이스로 두 프로젝트에 동일하게
{
    public enum MoveDir { None, Up, Down, Left, Right }

    // 클라 -> 서버 : 입력
    public class InputCommand
    {
        public MoveDir Dir { get; set; }
        public InputCommand() { }
        public InputCommand(MoveDir dir) { Dir = dir; }
    }

    // 서버 -> 클라 : 좌표 스냅샷 (MVP)
    public class Snapshot
    {
        public int X { get; set; }
        public int Y { get; set; }
        public Snapshot() { }
        public Snapshot(int x, int y) { X = x; Y = y; }
    }

    public static class Msg
    {
        public static byte[] ToBytes<T>(T obj)
            => Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(obj));

        public static T FromBytes<T>(byte[] bytes, int len)
            => JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(bytes, 0, len));
    }
}
