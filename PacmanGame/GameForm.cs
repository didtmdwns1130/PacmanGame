// GameForm.cs — 디자이너 미사용, C# 7.3 호환
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Shared; // WelcomeMsg, SnapshotMsg, GhostState
using MoveDir = Shared.Dir;   // 로컬 Dir → 네트워크 MoveDir 별칭

namespace PacmanGame
{
    public class GameForm : Form
    {
        // -----------------------------
        // 필드
        // -----------------------------
        private readonly GameClient _client;
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
        private int _myId = -1;              // 내 PlayerId
        private bool _gameStarted = false;   // Play 클릭 후부터 키 입력/스냅샷 반영

        // (옵션) 게임오버 표시용 플래그
        private bool _isGameOver = false;
        private Panel panelGameOver;
        private Label lblFinalScore;
        private Label btnRestart;
        private Label lblOver;

        // 유령(-) ID 상수 (서버와 합의)
        private const int GHOST_TL = -1; // RED
        private const int GHOST_TR = -2; // BLUE
        private const int GHOST_BL = -3; // ORANGE
        private const int GHOST_BR = -4; // PINK

        private PictureBox picPacman;
        private readonly Dictionary<int, PictureBox> _playerSprites = new Dictionary<int, PictureBox>();
        private PictureBox picGhostTL, picGhostTR, picGhostBL, picGhostBR;

        // (이전 로컬 코인 컬렉션: 더 이상 사용하지 않아도 무방)
        private readonly List<PictureBox> coins = new List<PictureBox>();
        private readonly HashSet<Point> coinPoints = new HashSet<Point>();

        // ★ 서버 권위 코인 렌더용
        private readonly Dictionary<int, PictureBox> _coinsById = new Dictionary<int, PictureBox>();
        private bool _coinSeeded = false;

        // 애니메이션 / 방향
        private Timer pacmanAnimTimer;
        private bool pacmanMouthOpen = true;
        private enum Dir { Right, Left, Up, Down }
        private Dir pacmanDir = Dir.Right;
        private bool _sentFirstInput = false; // ★ 첫 입력 보장

        private enum GhostEye { Left, Right, Up, Down }

        private readonly string assetDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets");

        // -----------------------------
        // 생성자
        // -----------------------------
        public GameForm(string nickname, string serverIp, GameClient client)
        {
            _nickname = string.IsNullOrWhiteSpace(nickname) ? "Player" : nickname;
            _serverIp = string.IsNullOrWhiteSpace(serverIp) ? "127.0.0.1" : serverIp;
            _client = client;

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            UpdateStyles();
            InitializeComponent();

            // 네트워크 이벤트 바인딩
            if (_client != null)
            {
                _client.OnWelcome += Client_OnWelcome;   // Action<WelcomeMsg>
                _client.OnSnapshot += Client_OnSnapshot; // Action<SnapshotMsg>
            }

            // 폼 닫힐 때 구독 해제
            this.FormClosed += GameForm_FormClosed;
        }

        // -----------------------------
        // InitializeComponent
        // -----------------------------
        private void InitializeComponent()
        {
            SuspendLayout();

            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(720, 720);
            StartPosition = FormStartPosition.CenterScreen;
            Text = "PacmanClient - Connecting..."; // ★ 연결 전 상태
            BackColor = Color.FromArgb(24, 24, 24);
            KeyPreview = true;
            KeyDown += GameForm_KeyDown;

            // HUD
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
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(ClientSize.Width - 16 - 260, 12),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            Controls.Add(lblScore);
            Controls.Add(lblInfo);

            lblInfo.SizeChanged += (s, e) =>
                lblInfo.Location = new Point(ClientSize.Width - 16 - lblInfo.Width, lblInfo.Location.Y);
            this.Resize += (s, e) =>
                lblInfo.Location = new Point(ClientSize.Width - 16 - lblInfo.Width, lblInfo.Location.Y);

            // 보드
            panelBoard = new Panel
            {
                Name = "panelBoard",
                BackColor = Color.Black,
                Size = new Size(600, 600),
                Location = new Point((ClientSize.Width - 600) / 2, (ClientSize.Height - 600) / 2 + 10)
            };
            var db = panelBoard.GetType().GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (db != null) db.SetValue(panelBoard, true, null);
            Controls.Add(panelBoard);

            AddBlueBlockFrameTiled();

            // 고스트 4개
            picGhostTL = MakeGhost("picGhostTL", Color.IndianRed, GhostEye.Right);  // -1
            picGhostTR = MakeGhost("picGhostTR", Color.DeepSkyBlue, GhostEye.Left); // -2
            picGhostBL = MakeGhost("picGhostBL", Color.Orange, GhostEye.Up);        // -3
            picGhostBR = MakeGhost("picGhostBR", Color.HotPink, GhostEye.Down);     // -4

            int g = 40, m = 90;
            picGhostTL.Location = new Point(m, m);
            picGhostTR.Location = new Point(panelBoard.Width - m - g, m);
            picGhostBL.Location = new Point(m, panelBoard.Height - m - g);
            picGhostBR.Location = new Point(panelBoard.Width - m - g, panelBoard.Height - m - g);

            panelBoard.Controls.Add(picGhostTL);
            panelBoard.Controls.Add(picGhostTR);
            panelBoard.Controls.Add(picGhostBL);
            panelBoard.Controls.Add(picGhostBR);

            // 시작 오버레이
            panelStart = new Panel
            {
                Name = "panelStart",
                BackColor = Color.Transparent,
                Size = panelBoard.Size,
                Location = new Point(0, 0)
            };

            // 팩맨
            picPacman = new PictureBox
            {
                Name = "picPacman",
                Size = new Size(44, 44),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
            panelStart.Controls.Add(picPacman);
            picPacman.BringToFront();

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

            // play 라벨
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

            AlignPacmanOverPlay();
            panelStart.Layout += (s, e) => AlignPacmanOverPlay();
            panelStart.Resize += (s, e) => AlignPacmanOverPlay();

            panelBoard.Controls.Add(panelStart);
            panelStart.BringToFront();

            StartPacmanAnimation();

            // === GameOver 오버레이 ===
            panelGameOver = new Panel
            {
                Name = "panelGameOver",
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(200, 0, 0, 0),
                Visible = false
            };
            this.Controls.Add(panelGameOver);
            panelGameOver.Resize += (s, e) => LayoutGameOver();

            lblOver = new Label
            {
                Text = "GAME OVER",
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 36F, FontStyle.Bold),
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleCenter
            };
            panelGameOver.Controls.Add(lblOver);

            lblFinalScore = new Label
            {
                Text = "0점 / Round 1",
                ForeColor = Color.Gold,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleCenter
            };
            panelGameOver.Controls.Add(lblFinalScore);

            btnRestart = new Label
            {
                Text = "다시 시작",
                ForeColor = Color.Black,
                BackColor = Color.Gold,
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(240, 56),
                Cursor = Cursors.Hand
            };
            btnRestart.Click += (s, e) => { TryRestart(); };
            panelGameOver.Controls.Add(btnRestart);
            panelGameOver.BringToFront();
            LayoutGameOver();

            ResumeLayout(false);
        }

        // -----------------------------
        // 네트워크 이벤트
        // -----------------------------
        private void Client_OnWelcome(WelcomeMsg w)
        {
            if (IsDisposed) return;
            if (InvokeRequired) BeginInvoke(new Action(() => ApplyWelcome(w)));
            else ApplyWelcome(w);
        }

        private void ApplyWelcome(WelcomeMsg w)
        {
            _myId = w.YourId;
            lblInfo.Text = $"ME: {_nickname}  |  Server: {_serverIp}";
            Text = "PacmanClient - Connected"; // ★ 연결 완료 시점에 타이틀 갱신
        }

        private void Client_OnSnapshot(SnapshotMsg s)
        {
            if (IsDisposed) return;

            const int ROUND_SHIFT = 20;
            int round = s.Tick >> ROUND_SHIFT;

            int count = 0;
            if (s.Players != null)
                foreach (var ps in s.Players) if (ps.Id > 0) count++;

            Action ui = () =>
            {
                lblInfo.Text = $"ME: {_nickname}  |  Server: {_serverIp}  |  Players: {count}";

                var alive = new HashSet<int>();
                bool onStartOverlay = panelStart != null && !panelStart.IsDisposed && picPacman.Parent == panelStart;

                if (s.Players != null)
                {
                    foreach (var ps in s.Players)
                    {
                        if (ps.Id == _myId && onStartOverlay)
                        {
                            alive.Add(ps.Id);
                            _score = ps.Score;
                            lblScore.Text = $"SCORE: {_score}  (R:{round})";
                            continue;
                        }

                        var sprite = GetOrCreateSprite(ps.Id);

                        int x = (int)ps.X, y = (int)ps.Y;
                        if (x < 0) x = 0; if (y < 0) y = 0;
                        if (x > panelBoard.Width - sprite.Width) x = panelBoard.Width - sprite.Width;
                        if (y > panelBoard.Height - sprite.Height) y = panelBoard.Height - sprite.Height;

                        sprite.Location = new Point(x, y);
                        if (!panelGameOver.Visible) sprite.BringToFront();
                        alive.Add(ps.Id);

                        if (ps.Id == _myId)
                        {
                            _score = ps.Score;
                            lblScore.Text = $"SCORE: {_score}  (R:{round})";
                        }
                    }
                }

                var toRemove = new List<int>();
                foreach (var kv in _playerSprites) if (!alive.Contains(kv.Key)) toRemove.Add(kv.Key);
                foreach (var id in toRemove)
                {
                    if (_playerSprites.TryGetValue(id, out var pb) && pb != null && !pb.IsDisposed)
                    {
                        panelBoard.Controls.Remove(pb);
                        pb.Dispose();
                    }
                    _playerSprites.Remove(id);
                }

                if (panelStart != null) panelStart.BringToFront();

                if (s.Coins != null && s.Coins.Count > 0)
                {
                    if (!_coinSeeded)
                    {
                        var removeList = new List<Control>();
                        foreach (Control c in panelBoard.Controls)
                        {
                            if (c is PictureBox pb && (pb.Tag as string) == "coin")
                                removeList.Add(pb);
                        }
                        foreach (var c in removeList) panelBoard.Controls.Remove(c);

                        _coinsById.Clear();

                        Image coinImg = LoadImage("coin.png", false);
                        if (IsFallback(coinImg)) coinImg = DrawCoinBitmap(20, 20);

                        foreach (var c in s.Coins)
                        {
                            var pb = new PictureBox
                            {
                                Size = new Size(20, 20),
                                Location = new Point(c.X, c.Y),
                                SizeMode = PictureBoxSizeMode.Zoom,
                                BackColor = Color.Transparent,
                                Image = coinImg,
                                Visible = !c.Eaten,
                                Tag = "coin"
                            };
                            _coinsById[c.Id] = pb;
                            panelBoard.Controls.Add(pb);
                            pb.SendToBack();
                        }
                        if (panelStart != null && !panelStart.IsDisposed)
                            panelStart.BringToFront();
                        _coinSeeded = true;
                    }
                    else
                    {
                        foreach (var c in s.Coins)
                        {
                            if (_coinsById.TryGetValue(c.Id, out var pb))
                                pb.Visible = !c.Eaten;
                        }
                    }
                }

                if (s.Ghosts != null)
                    UpdateGhostsFromSnapshot(s.Ghosts);

                if (s.GameOver && !_isGameOver)
                {
                    _isGameOver = true;
                    ShowGameOver(round);
                }
                else if (!s.GameOver && _isGameOver)
                {
                    _isGameOver = false;
                    HideGameOver();
                }
            };

            if (InvokeRequired) BeginInvoke(ui); else ui();
            if (InvokeRequired) BeginInvoke((Action)(() => AfterSpritesUpdatedKeepOverlayOnTop()));
            else AfterSpritesUpdatedKeepOverlayOnTop();
        }

        private void ShowGameOver(int round)
        {
            try
            {
                lblFinalScore.Text = $"{_score}점 / Round {round}";
                LayoutGameOver();
                panelGameOver.Visible = true;
                panelGameOver.BringToFront();
            }
            catch { }
        }

        private void HideGameOver()
        {
            try { panelGameOver.Visible = false; } catch { }
        }

        private void AfterSpritesUpdatedKeepOverlayOnTop()
        {
            if (_isGameOver && panelGameOver.Visible)
                panelGameOver.BringToFront();
        }

        // === 중앙 정렬 전용 레이아웃 함수 ===
        private void LayoutGameOver()
        {
            if (panelGameOver == null || panelGameOver.IsDisposed) return;
            int gap1 = 18, gap2 = 22;
            int totalH = lblOver.Height + gap1 + lblFinalScore.Height + gap2 + btnRestart.Height;
            int cx = panelGameOver.ClientSize.Width / 2;
            int cy = panelGameOver.ClientSize.Height / 2;
            int top = cy - totalH / 2;

            lblOver.Left = cx - (lblOver.Width / 2);
            lblOver.Top = top;

            lblFinalScore.Left = cx - (lblFinalScore.Width / 2);
            lblFinalScore.Top = lblOver.Bottom + gap1;

            btnRestart.Left = cx - (btnRestart.Width / 2);
            btnRestart.Top = lblFinalScore.Bottom + gap2;
        }

        private void TryRestart()
        {
            _client?.SendRestart();
        }

        private void UpdateGhostsFromSnapshot(List<GhostState> ghosts)
        {
            foreach (var g in ghosts)
            {
                var pb = GetGhostSpriteById(g.Id);
                if (pb == null) continue;
                int x = Math.Max(0, Math.Min(panelBoard.Width - pb.Width, (int)g.X));
                int y = Math.Max(0, Math.Min(panelBoard.Height - pb.Height, (int)g.Y));
                pb.Location = new Point(x, y);
                pb.Visible = true;
                switch (g.Dir)
                {
                    case MoveDir.Left: SetGhostEyes(pb, GhostEye.Left); break;
                    case MoveDir.Right: SetGhostEyes(pb, GhostEye.Right); break;
                    case MoveDir.Up: SetGhostEyes(pb, GhostEye.Up); break;
                    case MoveDir.Down: SetGhostEyes(pb, GhostEye.Down); break;
                }
                if (!panelGameOver.Visible) pb.BringToFront();
            }
        }

        private PictureBox GetGhostSpriteById(int id)
        {
            switch (id)
            {
                case GHOST_TL: return picGhostTL;
                case GHOST_TR: return picGhostTR;
                case GHOST_BL: return picGhostBL;
                case GHOST_BR: return picGhostBR;
                default: return null;
            }
        }

        private void SetGhostEyes(PictureBox pb, GhostEye eyes)
        {
            if (!(pb?.Tag is Color body)) return;
            if (pb.Image != null) pb.Image.Dispose();
            pb.Image = DrawGhostBitmap(body, eyes, pb.Width, pb.Height);
        }

        private void GameForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (_client != null)
            {
                _client.OnWelcome -= Client_OnWelcome;
                _client.OnSnapshot -= Client_OnSnapshot;
            }
        }

        // -----------------------------
        // UI 이벤트
        // -----------------------------
        private void StartOverlay_Click(object sender, EventArgs e)
        {
            // 오버레이 닫기
            if (picPacman.Parent == panelStart)
            {
                panelStart.Controls.Remove(picPacman);
                panelBoard.Controls.Add(picPacman);
            }
            picPacman.Location = new Point(panelBoard.Width / 2 - picPacman.Width / 2,
                                           panelBoard.Height / 2 - picPacman.Height / 2);
            picPacman.BringToFront();

            if (panelStart != null && !panelStart.IsDisposed)
            {
                panelStart.Hide();
                panelStart.Dispose();
                panelStart = null;
            }

            panelBoard.Invalidate();
            panelBoard.Update();
            panelBoard.Focus();

            _gameStarted = true;      // 이제부터 키 입력 허용
            _sentFirstInput = false;  // ★ 시작마다 리셋
            StartPacmanAnimation();
        }

        private void GameForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (!_gameStarted) return;
            if (_isGameOver) return;

            var prev = pacmanDir;
            if (e.KeyCode == Keys.Right) pacmanDir = Dir.Right;
            else if (e.KeyCode == Keys.Left) pacmanDir = Dir.Left;
            else if (e.KeyCode == Keys.Up) pacmanDir = Dir.Up;
            else if (e.KeyCode == Keys.Down) pacmanDir = Dir.Down;

            // ★ 첫 키는 동일 방향이어도 무조건 1회 전송 → 서버 이동 시작 트리거
            if (!_sentFirstInput || prev != pacmanDir)
            {
                if (picPacman != null)
                {
                    if (picPacman.Image != null) picPacman.Image.Dispose();
                    picPacman.Image = DrawPacmanFrame(pacmanMouthOpen, pacmanDir);
                }
                _client?.SendInput(MapDir(pacmanDir));
                _sentFirstInput = true;
            }
        }

        private MoveDir MapDir(Dir d)
        {
            switch (d)
            {
                case Dir.Up: return MoveDir.Up;
                case Dir.Down: return MoveDir.Down;
                case Dir.Left: return MoveDir.Left;
                case Dir.Right: return MoveDir.Right;
            }
            return MoveDir.Right;
        }

        // -----------------------------
        // HUD 보조
        // -----------------------------
        private void AddScore(int delta)
        {
            _score = Math.Max(0, _score + delta);
            lblScore.Text = $"SCORE: {_score}";
        }

        // -----------------------------
        // 팩맨 애니메이션
        // -----------------------------
        private void StartPacmanAnimation()
        {
            if (pacmanAnimTimer == null)
            {
                pacmanAnimTimer = new Timer { Interval = 140 };
                pacmanAnimTimer.Tick += (s, ev) =>
                {
                    if (picPacman == null) return;
                    if (picPacman.Image != null) picPacman.Image.Dispose();
                    picPacman.Image = DrawPacmanFrame(pacmanMouthOpen, pacmanDir);
                    pacmanMouthOpen = !pacmanMouthOpen;
                };
            }

            pacmanMouthOpen = true;
            if (picPacman.Image != null) picPacman.Image.Dispose();
            picPacman.Image = DrawPacmanFrame(pacmanMouthOpen, pacmanDir);
            pacmanAnimTimer.Start();
        }

        // -----------------------------
        // 코인/고스트/프레임 유틸
        // -----------------------------
        private Image DrawPacmanFrame(bool mouthOpen, Dir dir)
        {
            Bitmap bmp = new Bitmap(44, 44);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using (var br = new SolidBrush(Color.Gold))
                    g.FillEllipse(br, 2, 2, 40, 40);

                int sweep = mouthOpen ? 60 : 12;
                int center = 0;
                switch (dir)
                {
                    case Dir.Left: center = 180; break;
                    case Dir.Up: center = 270; break;
                    case Dir.Down: center = 90; break;
                }
                int start = center - sweep / 2;

                using (var bg = new SolidBrush(Color.Black))
                    g.FillPie(bg, 2, 2, 40, 40, start, sweep);
            }
            return bmp;
        }

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

        private void AddBlueBlockFrameTiled()
        {
            Color teal = Color.FromArgb(0, 190, 190);
            int tile = 34, gap = 16, margin = 18;

            int usableW = panelBoard.Width + (tile + gap) - 1;
            int countTop = Math.Max(1, usableW / (tile + gap));
            int startX = panelBoard.Left - ((countTop * (tile + gap) - panelBoard.Width) / 2);

            for (int i = 0; i < countTop; i++)
            {
                int x = startX + i * (tile + gap);
                var top = new Panel { BackColor = teal, Size = new Size(tile, tile), Location = new Point(x, panelBoard.Top - margin - tile) };
                var bottom = new Panel { BackColor = teal, Size = new Size(tile, tile), Location = new Point(x, panelBoard.Bottom + margin) };
                Controls.Add(top); top.SendToBack();
                Controls.Add(bottom); bottom.SendToBack();
            }

            int usableH = panelBoard.Height + (tile + gap) - 1;
            int countSide = Math.Max(1, usableH / (tile + gap));
            int startY = panelBoard.Top - ((countSide * (tile + gap) - panelBoard.Height) / 2);

            for (int i = 0; i < countSide; i++)
            {
                int y = startY + i * (tile + gap);
                var left = new Panel { BackColor = teal, Size = new Size(tile, tile), Location = new Point(panelBoard.Left - margin - tile, y) };
                var right = new Panel { BackColor = teal, Size = new Size(tile, tile), Location = new Point(panelBoard.Right + margin, y) };
                Controls.Add(left); left.SendToBack();
                Controls.Add(right); right.SendToBack();
            }
        }

        // ===== 아래 로컬 코인 레이아웃 유틸은 호출하지 않지만 남겨둠 =====
        private void BuildCleanCoinLayout()
        {
            foreach (var c in coins)
            {
                if (c != null && !c.IsDisposed) panelBoard.Controls.Remove(c);
                c?.Dispose();
            }
            coins.Clear();
            coinPoints.Clear();

            var coinSize = new Size(20, 20);
            int step = 30;
            int margin = 26;

            PlaceBorderCoins(margin, step, coinSize);

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

        private void AddCoin(Point location, Size size, Image img)
        {
            if (coinPoints.Contains(location)) return;
            coinPoints.Add(location);

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
            pb.BringToFront();
        }

        private PictureBox MakeGhost(string name, Color bodyColor, GhostEye eyes)
        {
            string fn = "";
            if (bodyColor == Color.IndianRed) fn = "ghost_red.png";
            if (bodyColor == Color.DeepSkyBlue) fn = "ghost_blue.png";
            if (bodyColor == Color.Orange) fn = "ghost_orange.png";
            if (bodyColor == Color.HotPink) fn = "ghost_pink.png";

            Image img = LoadImage(fn, false);
            if (IsFallback(img))
                img = DrawGhostBitmap(bodyColor, eyes, 40, 40);

            return new PictureBox
            {
                Name = name,
                Size = new Size(40, 40),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Image = img,
                Tag = bodyColor // 눈 갱신용 바디 컬러 보관
            };
        }

        private Image DrawGhostBitmap(Color body, GhostEye eyes, int w, int h)
        {
            Bitmap bmp = new Bitmap(w, h);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

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

        private void AlignPacmanOverPlay()
        {
            if (panelStart == null || picPacman == null || lblPlay == null || lblCenterTitle == null) return;

            int x = panelStart.Width / 2 - picPacman.Width / 2;

            int y = lblPlay.Top - picPacman.Height - 12;
            int minY = lblCenterTitle.Bottom + 6;
            if (y < minY) y = minY;

            picPacman.Location = new Point(x, y);
            picPacman.BringToFront();
        }

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
            catch { }

            if (fallbackPacman)
                return DrawPacmanFrame(true, Dir.Right);

            var empty = new Bitmap(1, 1);
            using (Graphics g = Graphics.FromImage(empty)) g.Clear(Color.Transparent);
            return empty;
        }

        private bool IsFallback(Image img) => img != null && img.Width == 1 && img.Height == 1;

        // =============================
        // 서버 권위 렌더: 스프라이트 캐시
        // =============================
        private PictureBox GetOrCreateSprite(int id)
        {
            if (id == _myId && picPacman != null)
            {
                if (_playerSprites.TryGetValue(id, out var dup) && dup != picPacman && dup != null && !dup.IsDisposed)
                {
                    if (dup.Parent != null) dup.Parent.Controls.Remove(dup);
                    dup.Dispose();
                    _playerSprites.Remove(id);
                }

                _playerSprites[id] = picPacman;
                return picPacman;
            }

            if (_playerSprites.TryGetValue(id, out var pb) && pb != null && !pb.IsDisposed)
                return pb;

            pb = new PictureBox
            {
                Size = new Size(44, 44),
                BackColor = Color.Transparent,
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = DrawPacmanFrame(true, Dir.Right),
                Location = new Point(panelBoard.Width / 2, panelBoard.Height / 2)
            };
            _playerSprites[id] = pb;
            panelBoard.Controls.Add(pb);
            if (!panelGameOver.Visible) pb.BringToFront();
            return pb;
        }
    }
}
