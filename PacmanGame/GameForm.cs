// GameForm.cs — 디자이너 미사용, C# 7.3 호환
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;            // (선택)
using System.Windows.Forms;
using Shared; // WelcomeMsg, SnapshotMsg, GhostState, CoinState
using MoveDir = Shared.Dir;   // 로컬 Dir → 네트워크 MoveDir 별칭

namespace PacmanGame
{
    public class GameForm : Form
    {
        // -----------------------------
        // 상수
        // -----------------------------
        private const int SPRITE = 44;
        private const int HUD_COIN_SCORE = 10; // 서버의 COIN_SCORE와 일치 (보정용)

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
        // ★ HUD 보정용(서버 점수 안 들어올 때 대비)
        private int _initCoinCount = -1;    // 라운드 시작 시 남은 코인 개수
        private int _prevCoinCount = -1;    // 이전 프레임 남은 코인 개수
        private int _hudScore = 0;          // 화면 표기 점수(누적)
        private int _lastServerScore = -1;  // 최근 서버 점수
        private int _lastRound = -1;        // 마지막으로 표기한 라운드
        private int _roundBaseScore = 0;    // ★ 라운드 시작 시점의 누적 점수(합산 베이스)

        private int _myId = -1;                 // ★ Welcome에서 확정
        private bool _gameStarted = false;

        // 게임오버 오버레이
        private bool _isGameOver = false;
        private Panel panelGameOver;
        private Label lblFinalScore;
        private Label btnRestart;
        private Label lblOver;

        // 유령(-) ID (서버와 동일)
        private const int GHOST_TL = -1;
        private const int GHOST_TR = -2;
        private const int GHOST_BL = -3;
        private const int GHOST_BR = -4;

        // ▶ 메뉴 전용 데모 스프라이트(게임 시작 시 제거)
        private PictureBox _menuPacman;

        // ▶ 모든 플레이어(내 것 포함)는 캐시로만 렌더
        private readonly Dictionary<int, PictureBox> _playerSprites = new Dictionary<int, PictureBox>();
        private PictureBox picGhostTL, picGhostTR, picGhostBL, picGhostBR;

        // 서버 권위 코인 렌더
        private readonly Dictionary<int, PictureBox> _coinsById = new Dictionary<int, PictureBox>();
        private bool _coinSeeded = false;

        // 애니메이션 / 방향
        private Timer pacmanAnimTimer;
        private bool pacmanMouthOpen = true;
        private enum Dir { Right, Left, Up, Down }
        private Dir pacmanDir = Dir.Right;
        private bool _sentFirstInput = false; // 첫 입력 보장

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

            if (_client != null)
            {
                _client.OnWelcome += Client_OnWelcome;      // ★ Welcome 연결
                _client.OnSnapshot += Client_OnSnapshot;
            }

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
            Text = "PacmanClient - Connecting...";
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
                Location = new Point(ClientSize.Width - 276, 12),
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

            // 유령 4개
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

            // 시작 오버레이
            panelStart = new Panel
            {
                Name = "panelStart",
                BackColor = Color.Transparent,
                Size = panelBoard.Size,
                Location = new Point(0, 0)
            };

            // 메뉴용 팩맨(게임 시작 시 제거)
            _menuPacman = new PictureBox
            {
                Name = "menuPacman",
                Size = new Size(SPRITE, SPRITE),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent
            };
            panelStart.Controls.Add(_menuPacman);

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
            lblPlay.BringToFront();                       // 버튼을 최상단
            panelStart.Controls.SetChildIndex(lblPlay, 0); // 확실히 맨 위

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

            AlignMenuPacmanOverPlay();
            panelStart.Layout += (s, e) => AlignMenuPacmanOverPlay();
            panelStart.Resize += (s, e) => AlignMenuPacmanOverPlay();

            panelBoard.Controls.Add(panelStart);
            panelStart.BringToFront();

            StartPacmanAnimation(); // 메뉴 데모 애니메이션

            // GameOver 오버레이
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
            if (IsDisposed || !IsHandleCreated) return;
            // ★ 여기서 _myId 확정 + 제목/정보 갱신
            BeginInvoke(new Action(() =>
            {
                _myId = w.YourId; // 확정
                lblInfo.Text = $"ME: {_myId}  |  Server: {_serverIp}"; // 아이디 표시
                Text = "PacmanClient - Connected"; // 디버깅용 제목 변경
            }));
        }

        private void Client_OnSnapshot(SnapshotMsg s)
        {
            if (IsDisposed) return;

            int count = 0;
            if (s.Players != null)
                foreach (var ps in s.Players) if (ps.Id > 0) count++;

            Action ui = () =>
            {
                // ---------- HUD (점수 + 라운드) ----------
                // A) 라운드 계산
                int roundNow = (s.Round != 0) ? s.Round : (s.Tick >> 20);

                // B) 서버 점수 읽기(있으면 채택 후보)
                int serverScore = -1;
                if (s.Players != null && _myId > 0)
                {
                    foreach (var p in s.Players)
                        if (p.Id == _myId) { serverScore = p.Score; break; }
                }
                if (serverScore >= 0) _lastServerScore = serverScore;

                // C) 현재 남은 코인 수(안 먹힌 것만)
                int coinsNow = _prevCoinCount;
                if (s.Coins != null)
                {
                    coinsNow = 0;
                    for (int i = 0; i < s.Coins.Count; i++) if (!s.Coins[i].Eaten) coinsNow++;
                }

                // D) 라운드 전환 감지 → 베이스/초기 코인 재설정
                if (_lastRound != roundNow)
                {
                    _lastRound = roundNow;
                    if (coinsNow >= 0)
                    {
                        _initCoinCount = coinsNow;   // 새 라운드의 총 코인 수(초기에는 모두 안 먹힘)
                        _prevCoinCount = coinsNow;
                    }
                    _roundBaseScore = _hudScore;     // ★ 이전 누적을 베이스로 저장
                }

                // E) 코인 기반 보정(이번 라운드 획득분) + 베이스 합산
                int fallbackTotal = _roundBaseScore;
                if (_initCoinCount >= 0 && coinsNow >= 0)
                {
                    int eaten = _initCoinCount - coinsNow;
                    if (eaten < 0) eaten = 0;
                    fallbackTotal = _roundBaseScore + eaten * HUD_COIN_SCORE;
                }

                // F) 최종 점수: (이전 HUD, 서버 점수, 보정 합산) 중 최댓값
                int finalScore = _hudScore;
                if (serverScore >= 0) finalScore = Math.Max(finalScore, serverScore);
                finalScore = Math.Max(finalScore, fallbackTotal);
                _hudScore = finalScore;
                _score = finalScore;
                _prevCoinCount = coinsNow;

                // G) HUD 출력
                lblScore.Text = $"SCORE: {_hudScore}  (R:{roundNow})";
                // ---------- 이하 기존 Info/스프라이트/코인 갱신 ----------

                lblInfo.Text = $"ME: {(_myId > 0 ? _myId.ToString() : _nickname)}  |  Server: {_serverIp}  |  Players: {count}";

                var alive = new HashSet<int>();

                // 플레이어
                if (s.Players != null)
                {
                    foreach (var ps in s.Players)
                    {
                        var sprite = GetOrCreateSprite(ps.Id);

                        int x = (int)ps.X, y = (int)ps.Y;
                        x = Math.Max(0, Math.Min(panelBoard.Width - sprite.Width, x));
                        y = Math.Max(0, Math.Min(panelBoard.Height - sprite.Height, y));
                        sprite.Location = new Point(x, y);

                        // 스냅샷 방향 반영
                        var dir = Dir.Right;
                        switch (ps.Dir)
                        {
                            case MoveDir.Left: dir = Dir.Left; break;
                            case MoveDir.Right: dir = Dir.Right; break;
                            case MoveDir.Up: dir = Dir.Up; break;
                            case MoveDir.Down: dir = Dir.Down; break;
                        }
                        if (!(sprite.Tag is Dir prev) || prev != dir)
                        {
                            if (sprite.Image != null) sprite.Image.Dispose();
                            sprite.Image = DrawPacmanFrame(pacmanMouthOpen, dir);
                            sprite.Tag = dir;
                        }

                        if (!panelGameOver.Visible) sprite.BringToFront();
                        alive.Add(ps.Id);
                    }
                }

                // 스냅샷에 없는 플레이어 제거
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

                // 코인
                if (s.Coins != null && s.Coins.Count > 0)
                {
                    if (!_coinSeeded)
                    {
                        // 남아있던 로컬 코인 제거
                        var removeList = new List<Control>();
                        foreach (Control c in panelBoard.Controls)
                            if (c is PictureBox pb && (pb.Tag as string) == "coin") removeList.Add(pb);
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
                        _coinSeeded = true;
                    }
                    else
                    {
                        foreach (var c in s.Coins)
                            if (_coinsById.TryGetValue(c.Id, out var pb))
                                pb.Visible = !c.Eaten;
                    }
                }

                // 유령
                if (s.Ghosts != null)
                    UpdateGhostsFromSnapshot(s.Ghosts);

                // 게임오버
                if (s.GameOver && !_isGameOver)
                {
                    _isGameOver = true;
                    ShowGameOver(s.Round);
                }
                else if (!s.GameOver && _isGameOver)
                {
                    _isGameOver = false;
                    HideGameOver();
                }
            };

            if (InvokeRequired) BeginInvoke(ui); else ui();

            // ★ 코인/스프라이트 Add 후에도 플레이 버튼이 가려지지 않도록 프레임마다 Z-Order 복구
            if (InvokeRequired) BeginInvoke(new Action(KeepStartOverlayOnTop));
            else KeepStartOverlayOnTop();

            // 기존 오버레이 최상단(게임오버) 유지 루틴
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

        // ★ 플레이 오버레이 항상 최상단 유지
        private void KeepStartOverlayOnTop()
        {
            if (panelStart != null && panelStart.Visible)
            {
                panelStart.BringToFront();
                if (lblPlay != null) lblPlay.BringToFront();
            }
        }

        // === 중앙 정렬 전용 레이아웃 ===
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

        private void TryRestart() => _client?.SendRestart();

        // -----------------------------
        // UI 이벤트
        // -----------------------------
        private void StartOverlay_Click(object sender, EventArgs e)
        {
            // 오버레이 닫기
            if (panelStart != null && !panelStart.IsDisposed)
            {
                panelStart.Hide();
                panelStart.Dispose();
                panelStart = null;
            }

            // 메뉴 데모 스프라이트 제거
            if (_menuPacman != null)
            {
                panelBoard.Controls.Remove(_menuPacman);
                try { _menuPacman.Dispose(); } catch { }
                _menuPacman = null;
            }

            panelBoard.Invalidate();
            panelBoard.Update();
            panelBoard.Focus();

            _gameStarted = true;
            _sentFirstInput = false;
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

            // 첫 키는 무조건 1회 전송
            if (!_sentFirstInput || prev != pacmanDir)
            {
                // 내 캐시 스프라이트 즉시 방향 반영(체감 개선)
                if (_playerSprites.TryGetValue(_myId, out var mePb))
                {
                    if (mePb.Image != null) mePb.Image.Dispose();
                    mePb.Image = DrawPacmanFrame(pacmanMouthOpen, pacmanDir);
                    mePb.Tag = pacmanDir;
                }
                _client?.SendInput(MapDir(pacmanDir));
                _sentFirstInput = true;
            }
        }

        // -----------------------------
        // 애니메이션
        // -----------------------------
        private void StartPacmanAnimation()
        {
            if (pacmanAnimTimer == null)
            {
                pacmanAnimTimer = new Timer { Interval = 140 };
                pacmanAnimTimer.Tick += (s, ev) =>
                {
                    pacmanMouthOpen = !pacmanMouthOpen;

                    // 메뉴 데모
                    if (_menuPacman != null)
                    {
                        if (_menuPacman.Image != null) _menuPacman.Image.Dispose();
                        _menuPacman.Image = DrawPacmanFrame(pacmanMouthOpen, pacmanDir);
                    }
                    else
                    {
                        // 게임 중: 모든 플레이어 캐시 갱신
                        foreach (var pb in _playerSprites.Values)
                        {
                            if (pb == null || pb.IsDisposed) continue;
                            var dirObj = pb.Tag;
                            Dir d = (dirObj is Dir dd) ? dd : Dir.Right;
                            if (pb.Image != null) pb.Image.Dispose();
                            pb.Image = DrawPacmanFrame(pacmanMouthOpen, d);
                        }
                    }
                };
            }

            pacmanMouthOpen = true;
            if (_menuPacman != null)
            {
                if (_menuPacman.Image != null) _menuPacman.Image.Dispose();
                _menuPacman.Image = DrawPacmanFrame(pacmanMouthOpen, Dir.Right);
            }
            pacmanAnimTimer.Start();
        }

        // -----------------------------
        // 렌더 유틸
        // -----------------------------
        private Image DrawPacmanFrame(bool mouthOpen, Dir dir)
        {
            Bitmap bmp = new Bitmap(SPRITE, SPRITE);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using (var br = new SolidBrush(Color.Gold))
                    g.FillEllipse(br, 2, 2, SPRITE - 4, SPRITE - 4);

                int sweep = mouthOpen ? 60 : 12;
                int center = 0;
                switch (dir)
                {
                    case Dir.Left: center = 180; break;
                    case Dir.Up: center = 270; break;
                    case Dir.Down: center = 90; break;
                    case Dir.Right: center = 0; break;
                }
                int start = center - sweep / 2;

                using (var bg = new SolidBrush(Color.Black))
                    g.FillPie(bg, 2, 2, SPRITE - 4, SPRITE - 4, start, sweep);
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
                Tag = bodyColor // 눈 갱신용 바디 컬러
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

        // 시작 오버레이 배치 헬퍼(버튼 위쪽에 위치만 조정, Z-Order 불변 + 클릭차단 해제)
        private void AlignMenuPacmanOverPlay()
        {
            if (panelStart == null || _menuPacman == null || lblPlay == null) return;

            int x = (panelStart.Width - _menuPacman.Width) / 2;
            int y = lblPlay.Top - _menuPacman.Height - 12;
            if (y < 0) y = 0;

            _menuPacman.Location = new Point(x, y);
            _menuPacman.Enabled = false; // 버튼 클릭 가로채지 않도록
            _menuPacman.TabStop = false;
            // Z-Order는 변경하지 않음: lblPlay가 최상단 유지
        }

        // 공용
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

            if (fallbackPacman) return DrawPacmanFrame(true, Dir.Right);

            var empty = new Bitmap(1, 1);
            using (Graphics g = Graphics.FromImage(empty)) g.Clear(Color.Transparent);
            return empty;
        }
        private bool IsFallback(Image img) => img != null && img.Width == 1 && img.Height == 1;

        private void GameForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (_client != null)
            {
                _client.OnWelcome -= Client_OnWelcome;
                _client.OnSnapshot -= Client_OnSnapshot;
            }
        }

        // 스프라이트 캐시
        private PictureBox GetOrCreateSprite(int id)
        {
            if (_playerSprites.TryGetValue(id, out var pb) && pb != null && !pb.IsDisposed)
                return pb;

            pb = new PictureBox
            {
                Size = new Size(SPRITE, SPRITE),
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
    }
}