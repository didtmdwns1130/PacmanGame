using Shared;

namespace PacmanServer
{
    public class GameState
    {
        public int X { get; private set; } = 100; // 초기 팩맨 X
        public int Y { get; private set; } = 100; // 초기 팩맨 Y
        private const int Speed = 5;

        public Snapshot Update(InputCommand cmd)
        {
            switch (cmd.Dir)
            {
                case MoveDir.Up: Y -= Speed; break;
                case MoveDir.Down: Y += Speed; break;
                case MoveDir.Left: X -= Speed; break;
                case MoveDir.Right: X += Speed; break;
            }

            // 새 위치를 Snapshot으로 반환
            return new Snapshot(X, Y);
        }
    }
}
