using Newtonsoft.Json;
using System.Collections.Generic;

namespace Shared
{
    public enum MoveDir { None, Up, Down, Left, Right }

    public class InputMsg
    {
        public string Type { get; set; } = "INPUT";
        public int PlayerId { get; set; }
        public MoveDir Dir { get; set; }
    }

    public class PlayerState
    {
        public int Id { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Score { get; set; }
        public bool IsAlive { get; set; }
    }

    public class Snapshot
    {
        public string Type { get; set; } = "SNAPSHOT";
        public List<PlayerState> Players { get; set; } = new List<PlayerState>();
        public int Round { get; set; }
        public int Tick { get; set; }
    }

    public class WelcomeMsg
    {
        public string Type { get; set; } = "WELCOME";
        public int PlayerId { get; set; }
    }
}
