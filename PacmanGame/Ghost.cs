using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing; // Image 클래스 사용을 위해 필요


using System.Windows.Forms; // PictureBox를 사용하기 위해 필요

namespace PacmanGame
{
    internal enum GhostAI { Chase, Random, AvoidWalls, Predict }
    
    internal enum MoveDir { None, Left, Right, Up, Down }
    
    internal class Ghost
    {
        int speed = 8;        // 고스트의 기본 이동 속도
        int xSpeed = 4;       // 고스트의 X축 이동 속도
        int ySpeed = 4;       // 고스트의 Y축 이동 속도

        readonly GhostAI ai;  // 👈 추가! 고스트의 고유 패턴을 저장할 필드

        int maxHeight = 635;  // 고스트가 이동할 수 있는 최대 높이
        int maxWidth = 920;   // 고스트가 이동할 수 있는 최대 너비
        int minHeight = 75;   // 고스트가 이동할 수 있는 최소 높이
        int minWidth = 76;    // 고스트가 이동할 수 있는 최소 너비


        int change;           // 방향 전환 시 사용되는 값
        Random random = new Random();  // 무작위 동작을 위한 Random 객체
        string[] directions = { "left", "right", "up", "down" }; // 이동 방향 배열
        string direction = "left";  // 고스트의 초기 이동 방향
        public PictureBox image = new PictureBox();  // 고스트의 이미지를 담을 PictureBox 객체

        public Ghost(Control parent, Image img, int x, int y)
        {
            if (img == null) throw new ArgumentNullException(nameof(img));

            image.Image = img;
            image.SizeMode = PictureBoxSizeMode.StretchImage;
            image.Size = new Size(50, 50);
            image.BackColor = Color.Black;          // 배경 블랙 유지
            image.Location = new Point(x, y);

            parent.Controls.Add(image);             // pacman과 같은 컨테이너
            image.BringToFront();                   // 맨 위로
        }



        // AI 패턴을 반영하는 이동 함수(폼 쪽에서 walls, pacDir 연결 전까지는 Random 처럼 동작)
        public void GhostMovement(PictureBox pacman, IEnumerable<Control> walls = null, MoveDir pacDir = MoveDir.None)
        {
            // 방향 유지 카운터
            if (change > 0) change--;
            else
            {
                change = random.Next(15, 35);
                direction = DecideDirection(pacman, walls, pacDir); // AI에 따라 방향 선택
            }

            // 실제 이동
            switch (direction)
            {
                case "left": image.Left -= speed; break;
                case "right": image.Left += speed; break;
                case "up": image.Top -= speed; break;
                case "down": image.Top += speed; break;
            }

            // 벽이 전달된 경우에만 간단 충돌 처리
            if (walls != null && IsBlocked(image.Bounds, walls))
            {
                // 반대로 되돌리기
                switch (direction)
                {
                    case "left": image.Left += speed; break;
                    case "right": image.Left -= speed; break;
                    case "up": image.Top += speed; break;
                    case "down": image.Top -= speed; break;
                }
                // 다른 방향 선택
                direction = PickRandomExcept(Opposite(direction));
                change = random.Next(10, 25);
            }
        }

        private string DecideDirection(PictureBox pacman, IEnumerable<Control> walls, MoveDir pacDir)
        {
            // 아직 Form1에서 walls, pacDir을 안 넘겨줘도 안전하게 Random으로 동작
            if (ai == GhostAI.Random || walls == null || pacman == null)
                return PickRandomExcept(Opposite(direction));

            switch (ai)
            {
                case GhostAI.Chase:
                    {
                        // 팩맨을 향해 더 먼 축을 우선 추적
                        int dx = (pacman.Left + pacman.Width / 2) - (image.Left + image.Width / 2);
                        int dy = (pacman.Top + pacman.Height / 2) - (image.Top + image.Height / 2);

                        string primary = Math.Abs(dx) >= Math.Abs(dy)
                            ? (dx < 0 ? "left" : "right")
                            : (dy < 0 ? "up" : "down");

                        // 벽 정보가 없으면 primary 그대로
                        if (walls == null) return primary;

                        // 막혀 있으면 보조 방향 시도
                        string secondary = (primary == "left" || primary == "right")
                            ? (dy < 0 ? "up" : "down")
                            : (dx < 0 ? "left" : "right");

                        if (!WillBlock(primary, walls)) return primary;
                        if (!WillBlock(secondary, walls)) return secondary;
                        return PickRandomExcept(Opposite(direction));
                    }

                // 다른 타입(AvoidWalls, Predict)은 다음 단계에서 추가
                default:
                    return PickRandomExcept(Opposite(direction));
            }
        }

        // === 아래 유틸들은 이미 있으면 생략 가능 ===

        private string PickRandomExcept(string except)
        {
            var cand = directions.Where(d => d != except).ToArray();
            return cand.Length == 0 ? direction : cand[random.Next(cand.Length)];
        }

        private string Opposite(string d)
        {
            switch (d)
            {
                case "left": return "right";
                case "right": return "left";
                case "up": return "down";
                case "down": return "up";
            }
            return d;
        }

        private bool WillBlock(string dir, IEnumerable<Control> walls)
        {
            if (walls == null) return false;
            var next = image.Bounds;
            int s = Math.Max(1, speed);
            switch (dir)
            {
                case "left": next.X -= s; break;
                case "right": next.X += s; break;
                case "up": next.Y -= s; break;
                case "down": next.Y += s; break;
            }
            return IsBlocked(next, walls);
        }

        private bool IsBlocked(Rectangle rect, IEnumerable<Control> walls)
        {
            if (walls == null) return false;
            foreach (var w in walls)
                if (rect.IntersectsWith(w.Bounds)) return true;
            return false;
        }


        // 👇 새 생성자 추가
        public Ghost(Control parent, Image img, int x, int y, GhostAI aiType)
        {
            if (img == null) throw new ArgumentNullException(nameof(img));
            ai = aiType;  // 전달받은 패턴 저장

            image.Image = img;
            image.SizeMode = PictureBoxSizeMode.StretchImage;
            image.Size = new Size(50, 50);
            image.BackColor = Color.Black;
            image.Location = new Point(x, y);

            parent.Controls.Add(image);
            image.BringToFront();
        }
    }

}
