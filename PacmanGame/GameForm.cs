// GameForm.cs — 디자이너 미사용, C# 7.3 호환
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace PacmanGame
{
    public class GameForm : Form
    {
        // -----------------------------
        // 필드
        // -----------------------------
        private readonly string _nickname;
        private readonly string _serverIp;

        private Panel panelBoard;
        private Panel panelStart;
        private Label lblCenterTitle;
        private Label lblPlay;
        private Label lblHelp;

        // HUD
        private Label lblScore;
        private Label lblInfo;
        private int _score = 0;

        private PictureBox picPacman;
        private PictureBox picGhostTL, picGhostTR, picGhostBL, picGhostBR;

        private readonly List<PictureBox> coins = new List<PictureBox>();
        private readonly HashSet<Point> coinPoints = new HashSet<Point>(); // 코인 중복 방지

        // 애니메이션 / 방향
        private Timer pacmanAnimTimer;
        private bool pacmanMouthOpen = true;
        private enum Dir { Right, Left, Up, Down }
        private Dir pacmanDir = Dir.Right; // 시작은 오른쪽을 바라봄

        private enum GhostEye { Left, Right, Up, Down }

        private readonly string assetDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets");

        // -----------------------------
        // 생성자
        // -----------------------------
        public GameForm(string nickname, string serverIp)
        {
            _nickname = nickname ?? "Player";
            _serverIp = string.IsNullOrWhiteSpace(serverIp) ? "127.0.0.1" : serverIp;

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            UpdateStyles();
            InitializeComponent();
        }

        // -----------------------------
        // InitializeComponent
        // -----------------------------
        private void InitializeComponent()
        {
            SuspendLayout();

            // Form 기본 설정
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(720, 720);
            StartPosition = FormStartPosition.CenterScreen;
            Text = "PacmanClient - Connected";
            BackColor = Color.FromArgb(24, 24, 24);
            KeyPreview = true;
            KeyDown += GameForm_KeyDown;

            // HUD (상단 고정)
            lblScore = new Label
            {
                Text = "SCORE: 0",
                ForeColor = Color.Gold,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(16, 10)
            };
            lblInfo = new Label
            {
                Text = $"ME: {_nickname}  |  Server: {_serverIp}",
                ForeColor = Color.Gainsboro,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(ClientSize.Width - 16 - 260, 12)
            };
            lblInfo.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            Controls.Add(lblScore);
            Controls.Add(lblInfo);

            // 보드 패널 (정사각)
            panelBoard = new Panel
            {
                Name = "panelBoard",
                BackColor = Color.Black,
                Size = new Size(600, 600),
                Location = new Point((ClientSize.Width - 600) / 2, (ClientSize.Height - 600) / 2 + 10) // HUD 공간 10px 고려
            };
            var db = panelBoard.GetType().GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (db != null) db.SetValue(panelBoard, true, null);
            Controls.Add(panelBoard);

            // 푸른색 블럭 프레임(사면 타일)
            AddBlueBlockFrameTiled();

            // 고스트 4마리 (표정 포함)
            picGhostTL = MakeGhost("picGhostTL", Color.IndianRed, GhostEye.Right);
            picGhostTR = MakeGhost("picGhostTR", Color.DeepSkyBlue, GhostEye.Left);
            picGhostBL = MakeGhost("picGhostBL", Color.Orange, GhostEye.Up);
            picGhostBR = MakeGhost("picGhostBR", Color.HotPink, GhostEye.Down);

            int g = 40, m = 90;
            picGhostTL.Location = new Point(m, m);
            picGhostTR.Location = new Point(panelBoard.Width - m - g, m);
            picGhostBL.Location = new Point(m, panelBoard.Height - m - g);
            picGhostBR.Location = new Point(panelBoard.Width - m - g, panelBoard.Height - m - g);

            panelBoard.Controls.Add(picGhostTL);
            panelBoard.Controls.Add(picGhostTR);
            panelBoard.Controls.Add(picGhostBL);
            panelBoard.Controls.Add(picGhostBR);

            // 코인(테두리 유지 + 내부 4개 소형 블록)
            BuildCleanCoinLayout();

            // 시작 오버레이 (팩맨 뒤, play 라벨 최상단)
            panelStart = new Panel
            {
                Name = "panelStart",
                BackColor = Color.Transparent,
                Size = panelBoard.Size,
                Location = new Point(0, 0)
            };

            // 팩맨(오버레이에 먼저 올림)
            picPacman = new PictureBox
            {
                Name = "picPacman",
                Size = new Size(44, 44),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Location = new Point(panelStart.Width / 2 - 22, panelStart.Height / 2 - 56)
            };
            panelStart.Controls.Add(picPacman);
            picPacman.SendToBack();

            // 중앙 타이틀
            lblCenterTitle = new Label
            {
                Name = "lblCenterTitle",
                AutoSize = false,
                Text = "Pac Man! - YSJ",
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.Gold,
                BackColor = Color.Black,
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                Size = new Size(420, 40),
                Location = new Point(panelStart.Width / 2 - 210, panelStart.Height / 2 - 118)
            };
            panelStart.Controls.Add(lblCenterTitle);

            // 하얀 "play" 라벨
            lblPlay = new Label
            {
                Name = "lblPlay",
                AutoSize = false,
                Text = "play",
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.Black,
                BackColor = Color.White,
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                Size = new Size(160, 50),
                Location = new Point(panelStart.Width / 2 - 80, panelStart.Height / 2 - 8),
                Cursor = Cursors.Hand
            };
            lblPlay.Click += StartOverlay_Click;
            panelStart.Controls.Add(lblPlay);
            lblPlay.BringToFront();

            // 도움말
            lblHelp = new Label
            {
                Name = "lblHelp",
                AutoSize = false,
                Text = "UP/DOWN/LEFT/RIGHT",
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.Gold,
                BackColor = Color.Black,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Size = new Size(360, 26),
                Location = new Point(panelStart.Width / 2 - 180, panelStart.Height / 2 + 64)
            };
            panelStart.Controls.Add(lblHelp);

            panelBoard.Controls.Add(panelStart);
            panelStart.BringToFront();

            // 시작 화면에서도 오른쪽 보며 껌뻑 시작
            StartPacmanAnimation();

            ResumeLayout(false);
        }

        // -----------------------------
        // 이벤트 핸들러
        // -----------------------------
        // play 클릭
        private void StartOverlay_Click(object sender, EventArgs e)
        {
            // 팩맨을 보드 중앙에 남기기
            if (picPacman.Parent == panelStart)
            {
                panelStart.Controls.Remove(picPacman);
                panelBoard.Controls.Add(picPacman);
            }
            picPacman.Location = new Point(panelBoard.Width / 2 - picPacman.Width / 2,
                                           panelBoard.Height / 2 - picPacman.Height / 2);
            picPacman.BringToFront();

            // 오버레이 제거
            if (panelStart != null && !panelStart.IsDisposed)
            {
                panelStart.Hide();
                panelStart.Dispose();
                panelStart = null;
            }

            panelBoard.Invalidate();
            panelBoard.Update();
            panelBoard.Focus();

            StartPacmanAnimation();
        }

        // 방향키 입력 → 보는 방향만 변경
        private void GameForm_KeyDown(object sender, KeyEventArgs e)
        {
            var prev = pacmanDir;
            if (e.KeyCode == Keys.Right) pacmanDir = Dir.Right;
            else if (e.KeyCode == Keys.Left) pacmanDir = Dir.Left;
            else if (e.KeyCode == Keys.Up) pacmanDir = Dir.Up;
            else if (e.KeyCode == Keys.Down) pacmanDir = Dir.Down;

            if (prev != pacmanDir && picPacman != null)
            {
                if (picPacman.Image != null) picPacman.Image.Dispose();
                picPacman.Image = DrawPacmanFrame(pacmanMouthOpen, pacmanDir);
            }
        }

        // -----------------------------
        // HUD 보조
        // -----------------------------
        private void AddScore(int delta)
        {
            _score += delta;
            if (_score < 0) _score = 0;
            lblScore.Text = $"SCORE: {_score}";
        }

        // -----------------------------
        // 팩맨 애니메이션
        // -----------------------------
        private void StartPacmanAnimation()
        {
            if (pacmanAnimTimer == null)
            {
                pacmanAnimTimer = new Timer();
                pacmanAnimTimer.Interval = 140; // 0.14초로 약간 빠르게
                pacmanAnimTimer.Tick += (s, ev) =>
                {
                    if (picPacman == null) return;
                    if (picPacman.Image != null) picPacman.Image.Dispose();
                    picPacman.Image = DrawPacmanFrame(pacmanMouthOpen, pacmanDir);
                    pacmanMouthOpen = !pacmanMouthOpen;
                };
            }

            pacmanMouthOpen = true; // 프레임 초기화
            if (picPacman.Image != null) picPacman.Image.Dispose();
            picPacman.Image = DrawPacmanFrame(pacmanMouthOpen, pacmanDir);
            pacmanAnimTimer.Start();
        }

        // 입 껌뻑 + 방향 정확(상/하/좌/우로만)
        private Image DrawPacmanFrame(bool mouthOpen, Dir dir)
        {
            Bitmap bmp = new Bitmap(44, 44);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using (var br = new SolidBrush(Color.Gold))
                    g.FillEllipse(br, 2, 2, 40, 40);

                // 입 크기(각도)
                int sweep = mouthOpen ? 60 : 12;

                // 방향의 중앙각 (0=Right, 90=Down, 180=Left, 270=Up)
                int center = 0;
                switch (dir)
                {
                    case Dir.Left: center = 180; break;
                    case Dir.Up: center = 270; break;
                    case Dir.Down: center = 90; break;
                }
                // 중앙각을 기준으로 좌우 대칭
                int start = center - sweep / 2;

                using (var bg = new SolidBrush(Color.Black))
                    g.FillPie(bg, 2, 2, 40, 40, start, sweep);
            }
            return bmp;
        }

        // -----------------------------
        // 골드 코인 드로잉
        // -----------------------------
        private Image DrawCoinBitmap(int w, int h)
        {
            Bitmap bmp = new Bitmap(w, h);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using (var br = new SolidBrush(Color.Gold))
                    g.FillEllipse(br, 0, 0, w - 1, h - 1);

                using (var hi = new SolidBrush(Color.FromArgb(230, 255, 235)))
                    g.FillEllipse(hi, (int)(w * 0.18f), (int)(h * 0.18f), (int)(w * 0.38f), (int)(h * 0.38f));

                using (var pen = new Pen(Color.FromArgb(180, 140, 80), 2f))
                    g.DrawEllipse(pen, 1, 1, w - 3, h - 3);

                using (var pen2 = new Pen(Color.FromArgb(240, 210, 90), 2f))
                    g.DrawEllipse(pen2, (int)(w * 0.18f), (int)(h * 0.18f), (int)(w * 0.64f), (int)(h * 0.64f));
            }
            return bmp;
        }

        // -----------------------------
        // 프레임/코인/고스트 유틸
        // -----------------------------
        private void AddBlueBlockFrameTiled()
        {
            Color teal = Color.FromArgb(0, 190, 190);
            int tile = 34, gap = 16, margin = 18;

            // 상/하
            int usableW = panelBoard.Width + (tile + gap) - 1;
            int countTop = Math.Max(1, usableW / (tile + gap));
            int startX = panelBoard.Left - ((countTop * (tile + gap) - panelBoard.Width) / 2);

            for (int i = 0; i < countTop; i++)
            {
                int x = startX + i * (tile + gap);
                var top = new Panel
                {
                    BackColor = teal,
                    Size = new Size(tile, tile),
                    Location = new Point(x, panelBoard.Top - margin - tile)
                };
                var bottom = new Panel
                {
                    BackColor = teal,
                    Size = new Size(tile, tile),
                    Location = new Point(x, panelBoard.Bottom + margin)
                };
                Controls.Add(top); top.SendToBack();
                Controls.Add(bottom); bottom.SendToBack();
            }

            // 좌/우
            int usableH = panelBoard.Height + (tile + gap) - 1;
            int countSide = Math.Max(1, usableH / (tile + gap));
            int startY = panelBoard.Top - ((countSide * (tile + gap) - panelBoard.Height) / 2);

            for (int i = 0; i < countSide; i++)
            {
                int y = startY + i * (tile + gap);
                var left = new Panel
                {
                    BackColor = teal,
                    Size = new Size(tile, tile),
                    Location = new Point(panelBoard.Left - margin - tile, y)
                };
                var right = new Panel
                {
                    BackColor = teal,
                    Size = new Size(tile, tile),
                    Location = new Point(panelBoard.Right + margin, y)
                };
                Controls.Add(left); left.SendToBack();
                Controls.Add(right); right.SendToBack();
            }
        }

        // ====== 정돈된 코인 레이아웃 ======
        // 테두리는 유지, 내부는 작은 사각형 4개(윗쪽 2, 아랫쪽 2)만 배치
        private void BuildCleanCoinLayout()
        {
            // 기존 코인 정리
            foreach (var c in coins)
            {
                if (c != null && !c.IsDisposed) panelBoard.Controls.Remove(c);
                c?.Dispose();
            }
            coins.Clear();
            coinPoints.Clear();

            var coinSize = new Size(20, 20);
            int step = 30;          // 테두리 기본 간격
            int margin = 26;        // 바깥 테두리 여백

            // 1) 바깥 테두리(그대로 유지)
            PlaceBorderCoins(margin, step, coinSize);

            // 2) 내부 작은 사각형 4개 (대칭)
            //    각 블록은 4x4 그리드, 블록 크기 약 100x100, 위치는 상/하 좌우 대칭
            int blockW = 100, blockH = 100;
            int topY = 150;
            int bottomY = panelBoard.Height - 150 - blockH;
            int leftX = 140;
            int rightX = panelBoard.Width - 140 - blockW;

            var rTopLeft = new Rectangle(leftX, topY, blockW, blockH);
            var rTopRight = new Rectangle(rightX, topY, blockW, blockH);
            var rBotLeft = new Rectangle(leftX, bottomY, blockW, blockH);
            var rBotRight = new Rectangle(rightX, bottomY, blockW, blockH);

            AddGridInRect(rTopLeft, 4, 4, coinSize);
            AddGridInRect(rTopRight, 4, 4, coinSize);
            AddGridInRect(rBotLeft, 4, 4, coinSize);
            AddGridInRect(rBotRight, 4, 4, coinSize);
        }

        private void PlaceBorderCoins(int margin, int step, Size coinSz)
        {
            Image img = LoadImage("coin.png", false);

            for (int x = margin; x <= panelBoard.Width - margin - coinSz.Width; x += step)
            {
                AddCoin(new Point(x, margin), coinSz, img);
                AddCoin(new Point(x, panelBoard.Height - margin - coinSz.Height), coinSz, img);
            }
            for (int y = margin + step; y <= panelBoard.Height - margin - coinSz.Height - step; y += step)
            {
                AddCoin(new Point(margin, y), coinSz, img);
                AddCoin(new Point(panelBoard.Width - margin - coinSz.Width, y), coinSz, img);
            }
        }

        private void AddGridInRect(Rectangle r, int cols, int rows, Size coinSz)
        {
            if (cols < 1 || rows < 1) return;

            // 간격은 정사각 블록 기준으로 균등 분배
            float gapX = (r.Width - coinSz.Width) / (float)(cols - 1);
            float gapY = (r.Height - coinSz.Height) / (float)(rows - 1);
            Image img = LoadImage("coin.png", false);

            for (int row = 0; row < rows; row++)
                for (int col = 0; col < cols; col++)
                {
                    int x = (int)Math.Round(r.Left + col * gapX);
                    int y = (int)Math.Round(r.Top + row * gapY);
                    AddCoin(new Point(x, y), coinSz, img);
                }
        }

        // 코인 추가(중복 방지 + 골드 코인 보장)
        private void AddCoin(Point location, Size size, Image img)
        {
            if (coinPoints.Contains(location)) return; // 중복 좌표 스킵
            coinPoints.Add(location);

            // 파일이 없거나 투명 fallback이면 코드로 그린 골드 코인 사용
            if (img == null || IsFallback(img))
                img = DrawCoinBitmap(size.Width, size.Height);

            var pb = new PictureBox
            {
                Size = size,
                Location = location,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Image = img
            };
            coins.Add(pb);
            panelBoard.Controls.Add(pb);
            pb.BringToFront(); // 보드 위에서 항상 보이도록
        }

        // --- 고스트(이미지 우선, 없으면 표정 포함 드로잉) ---
        private PictureBox MakeGhost(string name, Color bodyColor, GhostEye eyes)
        {
            // 파일 있으면 우선 사용
            string fn = "";
            if (bodyColor == Color.IndianRed) fn = "ghost_red.png";
            if (bodyColor == Color.DeepSkyBlue) fn = "ghost_blue.png";
            if (bodyColor == Color.Orange) fn = "ghost_orange.png";
            if (bodyColor == Color.HotPink) fn = "ghost_pink.png";

            Image img = LoadImage(fn, false);
            if (IsFallback(img)) // 파일 없으면 코드 드로잉
                img = DrawGhostBitmap(bodyColor, eyes, 40, 40);

            return new PictureBox
            {
                Name = name,
                Size = new Size(40, 40),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Image = img
            };
        }

        private Image DrawGhostBitmap(Color body, GhostEye eyes, int w, int h)
        {
            Bitmap bmp = new Bitmap(w, h);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                // 몸통(상단 반원 + 하단 물결)
                using (var br = new SolidBrush(body))
                {
                    Rectangle head = new Rectangle(3, 3, w - 6, h - 8);
                    g.FillEllipse(br, new Rectangle(head.X, head.Y, head.Width, head.Height - 10));
                    g.FillRectangle(br, new Rectangle(head.X, head.Y + (head.Height - 18), head.Width, 18));
                    int scallop = 6;
                    for (int i = 0; i < 4; i++)
                    {
                        int sx = head.X + 3 + i * 8;
                        int sy = head.Bottom - 6;
                        g.FillEllipse(br, new Rectangle(sx, sy - 3, scallop, scallop));
                    }
                }

                // 눈(흰자 + 동공)
                using (var white = new SolidBrush(Color.White))
                using (var black = new SolidBrush(Color.Black))
                {
                    Rectangle l = new Rectangle(12, 14, 10, 12);
                    Rectangle r = new Rectangle(22, 14, 10, 12);
                    g.FillEllipse(white, l);
                    g.FillEllipse(white, r);

                    Point off = new Point(0, 0);
                    if (eyes == GhostEye.Left) off = new Point(-2, 2);
                    if (eyes == GhostEye.Right) off = new Point(2, 2);
                    if (eyes == GhostEye.Up) off = new Point(0, -1);
                    if (eyes == GhostEye.Down) off = new Point(0, 3);

                    g.FillEllipse(black, new Rectangle(l.X + 4 + off.X, l.Y + 5 + off.Y, 4, 5));
                    g.FillEllipse(black, new Rectangle(r.X + 4 + off.X, r.Y + 5 + off.Y, 4, 5));
                }
            }
            return bmp;
        }

        // -----------------------------
        // 이미지 로드 & 보조
        // -----------------------------
        private Image LoadImage(string fileName, bool fallbackPacman)
        {
            try
            {
                if (!string.IsNullOrEmpty(fileName))
                {
                    string p1 = Path.Combine(assetDir, fileName);
                    if (File.Exists(p1)) return Image.FromFile(p1);

                    string p2 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
                    if (File.Exists(p2)) return Image.FromFile(p2);
                }
            }
            catch { /* ignore */ }

            // 파일이 없을 때
            if (fallbackPacman)
            {
                // 기본 첫 프레임(오른쪽, 입 열린 상태)
                return DrawPacmanFrame(true, Dir.Right);
            }

            // 1x1 투명 (fallback 표식)
            Bitmap empty = new Bitmap(1, 1);
            using (Graphics g = Graphics.FromImage(empty)) { g.Clear(Color.Transparent); }
            return empty;
        }

        private bool IsFallback(Image img) => img != null && img.Width == 1 && img.Height == 1;
    }
}
