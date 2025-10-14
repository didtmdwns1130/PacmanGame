using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Shared;           // MoveDir, Snapshot
using System.Diagnostics;
using SMoveDir = Shared.MoveDir;


namespace PacmanGame
{

    public partial class Form1 : Form
    {
        bool goup, godown, goleft, goright;
        bool noup, nodown, noleft, noright;
        List<Control> walls = new List<Control>();
        List<PictureBox> coins = new List<PictureBox>();
        int speed = 12;
        int score = 0;

        private GameClient _client; // 서버 호출 추가
        private bool _isAlive = true;   // ← 여기 추가


        // 서버 완성 전 임시로 클라 판정 사용
        private readonly bool _serverAuthoritative = true;
        private Snapshot _lastSnapshot;

        // 🔥 서버 스냅샷으로 그릴 플레이어 스프라이트 캐시 추가
        private readonly Dictionary<int, PictureBox> playerSprites = new Dictionary<int, PictureBox>();

        // 👇 여기에 추가
        Panel gameOverPanel;
        Label gameOverLabel;
        Button btnRetry, btnExit;


        // 여기에 한 줄 추가
        FlowLayoutPanel panelButtons;

        // Form1 클래스 필드들 위쪽에 추가
        int round = 1;
        Point pacmanStart;            // 시작 위치 기억
        bool isRoundTransition = false;  // 라운드 전환 중 입력/이동 잠깐 막기

        Label centerPopup;            // 중앙 팝업 라벨
        Timer popupTimer;             // 팝업 자동 숨김


        Ghost red, yellow, blue, pink;  // 빨강, 노랑, 파랑, 분홍 고스트 객체 선언
        List<Ghost> ghosts = new List<Ghost>();  // 여러 고스트(몬스터) 객체를 저장하기 위한 리스트 생성

        // 🔽🔽🔽 여기 추가 (메서드 안 X, 클래스 필드 구역 O)
        List<Point> ghostStartPositions = new List<Point>();


        private SMoveDir GetPacDir()
        {
            if (goleft) return SMoveDir.Left;
            if (goright) return SMoveDir.Right;
            if (goup) return SMoveDir.Up;
            if (godown) return SMoveDir.Down;
            return SMoveDir.None;
        }



        public Form1()
        {
            InitializeComponent();

            // 추가
            this.Shown += (_, __) =>
            {
                System.Diagnostics.Debug.WriteLine(
                    $"PACMAN_START={pacman.Location.X},{pacman.Location.Y}"
                );
            };

            pacmanStart = pacman.Location;   // 팩맨 시작 위치 저장
            PrepareCenterPopup();            // 중앙 팝업 준비
            PrepareGameOverUI();             // 추가


            SetUp();                         // 벽/코인 수집 (한 번만)

            this.DoubleBuffered = true;  // ← 깜빡임 감소
            this.KeyPreview = true;          // ← 키 먼저 받기

        }


        private void ShowCenterPopup(string text)
        {
            centerPopup.Text = text;
            // 중앙 배치
            centerPopup.Left = (this.ClientSize.Width - centerPopup.Width) / 2;
            centerPopup.Top = (this.ClientSize.Height - centerPopup.Height) / 2;
            centerPopup.Visible = true;
            centerPopup.BringToFront();
            popupTimer.Stop();
            popupTimer.Start();
        }

        private void PrepareCenterPopup()
        {
            centerPopup = new Label();
            centerPopup.AutoSize = true;
            centerPopup.BackColor = Color.FromArgb(200, 0, 0, 0); // 반투명 검정
            centerPopup.ForeColor = Color.Yellow;
            centerPopup.Font = new Font(FontFamily.GenericSansSerif, 24, FontStyle.Bold);
            centerPopup.Visible = false;
            centerPopup.Padding = new Padding(16, 10, 16, 10);
            centerPopup.Name = "centerPopup";
            this.Controls.Add(centerPopup);
            centerPopup.BringToFront();

            popupTimer = new Timer();
            popupTimer.Interval = 1200; // 1.2초 보여주기
            popupTimer.Tick += (s, e) =>
            {
                popupTimer.Stop();
                centerPopup.Visible = false;
                isRoundTransition = false;   // 전환 종료 → 다시 조작 가능
            };
        }

        // 👇👇 여기에 두 함수 붙여넣기 👇👇
        private void PrepareGameOverUI()
        {
            gameOverPanel = new Panel
            {
                Size = new Size(360, 200),
                BackColor = Color.FromArgb(240, 50, 70, 150),
                Visible = false
            };
            this.Controls.Add(gameOverPanel);
            gameOverPanel.BringToFront();

            gameOverLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 110,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.Gold,
                Font = new Font(FontFamily.GenericSansSerif, 16, FontStyle.Bold)
            };
            
            gameOverPanel.Controls.Add(gameOverLabel);

            panelButtons = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            gameOverPanel.Controls.Add(panelButtons);



            // 1) 버튼 생성
            btnRetry = new Button
            {
                Text = "다시 시작",
                Width = 140,
                Height = 40
            };

            btnExit = new Button
            {
                Text = "종료",
                Width = 100,
                Height = 40
            };

            // 2) 생성 이후에 마진 지정
            btnRetry.Margin = new Padding(10, 10, 10, 10);
            btnExit.Margin = new Padding(10, 10, 10, 10);

            // 3) 버튼을 패널에 추가
            panelButtons.Controls.Add(btnRetry);
            panelButtons.Controls.Add(btnExit);

            // 4) 중앙 정렬 유지
            CenterButtons();
            gameOverPanel.SizeChanged += (s, e) => CenterButtons();




            btnRetry.Click += (s, e) =>
            {
                gameOverPanel.Visible = false;

                // 👇 추가: 모든 고스트를 시작 위치로 되돌림
                for (int i = 0; i < ghosts.Count && i < ghostStartPositions.Count; i++)
                    ghosts[i].image.Location = ghostStartPositions[i];

                score = 0;
                round = 1;
                UpdateScoreUI();
                NextRound();
                GameTimer.Start();

                // ← 포커스 보장
                this.KeyPreview = true;
                this.ActiveControl = null;
                this.Focus();
            };

            btnExit.Click += (s, e) =>
            {
                gameOverPanel.Visible = false;
                panelMenu.Enabled = true;
                panelMenu.Visible = true;
            };
        }

        private void ShowGameOverOverlay()
        {
            string text = $"GAME OVER\n\nScore: {score}\nRound: {round}";
            gameOverLabel.Text = text;

            gameOverPanel.Left = (this.ClientSize.Width - gameOverPanel.Width) / 2;
            gameOverPanel.Top = (this.ClientSize.Height - gameOverPanel.Height) / 2;

            gameOverPanel.Visible = true;
            gameOverPanel.BringToFront();
            CenterButtons(); // 👈 여기에 추가

        }

        private void CenterButtons()
        {
            if (panelButtons == null || gameOverPanel == null) return;

            // 버튼 컨테이너의 총 폭 계산 (AutoSize=true 이므로 PreferredSize 사용)
            int contentWidth = panelButtons.PreferredSize.Width;

            // 패널 가운데 정렬 위치 계산
            int x = (gameOverPanel.Width - contentWidth) / 2;
            int y = gameOverPanel.Height - panelButtons.PreferredSize.Height - 16; // 아래 여백 약간

            // 위치 적용
            panelButtons.Location = new Point(Math.Max(0, x), Math.Max(0, y));
        }

        private void UpdateScoreUI()
        {
            this.Text = $"Pacman - Score: {score} | 남은 코인: {coins.Count(c => c.Visible)} | Round: {round}"; // 남은 코인 표기

            var found = this.Controls.Find("lblScore", true);
            if (found.Length > 0 && found[0] is Label lab)
                lab.Text = score.ToString();
        }




        private void label1_Click(object sender, EventArgs e)
        {

        }
          
        private async void Form1_Shown(object sender, EventArgs e)
        {
            this.Text = "PacmanClient - Connecting...";   // 연결 시도 중 표시
            _client = new GameClient();
            _client.SnapshotReceived += OnSnapshot;

            try
            {
                await _client.StartAsync("127.0.0.1", 7777);
                this.Text = "PacmanClient - Connected";   // 성공 시 표시
                // 서버 권위면 디자이너 pacman 숨기기 (스냅샷 스프라이트만 보이게)
                if (_serverAuthoritative) pacman.Visible = false;
            }
            catch (Exception ex)
            {
                this.Text = "Connect fail: " + ex.Message; // 실패 시 바로 원인 확인
            }
        }

        private void ClearPlayerSprites()
        {
            foreach (var kv in playerSprites)
            {
                if (kv.Value != null)
                {
                    this.Controls.Remove(kv.Value);
                    kv.Value.Dispose();
                }
            }
            playerSprites.Clear();
        }

        private void OnSnapshot(Shared.Snapshot snap)
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                BeginInvoke(new Action(() => OnSnapshot(snap)));
                return;
            }

            // 서버가 보낸 전체 상태를 즉시 반영
            UpdatePlayersFromSnapshot(snap);
            _lastSnapshot = snap; // 필요하면 유지 (지금은 안 써도 됨)
        }

        private void UpdatePlayersFromSnapshot(Shared.Snapshot snap)
        {
            // 1) 스냅샷에 있는 플레이어 전부 그리기/갱신
            foreach (var ps in snap.Players)
            {
                if (!playerSprites.TryGetValue(ps.Id, out var sprite))
                {
                    sprite = new PictureBox
                    {
                        Size = new Size(32, 32),
                        BackColor = (ps.Id == 1 ? Color.Yellow :
                                     ps.Id == 2 ? Color.Red :
                                     ps.Id == 3 ? Color.Blue : Color.Green)
                    };
                    this.Controls.Add(sprite);
                    playerSprites[ps.Id] = sprite;
                }

                sprite.Left = ps.X;
                sprite.Top = ps.Y;
                sprite.Visible = ps.IsAlive;
                sprite.BringToFront();
            }

            // 2) 스냅샷에 없는(=끊긴) 플레이어 정리
            var liveIds = new HashSet<int>(snap.Players.Select(p => p.Id));
            var toRemove = new List<int>();
            foreach (var kv in playerSprites)
                if (!liveIds.Contains(kv.Key))
                    toRemove.Add(kv.Key);

            foreach (var id in toRemove)
            {
                var pb = playerSprites[id];
                if (pb != null)
                {
                    this.Controls.Remove(pb);
                    pb.Dispose();
                }
                playerSprites.Remove(id);
            }
        }





        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { _client?.Dispose(); } catch { }
            base.OnFormClosed(e);
        }
        
        private void pictureBox1_Click(object sender, EventArgs e)
        {

        }

        private void pictureBox2_Click(object sender, EventArgs e)
        {

        }

        private void pictureBox7_Click(object sender, EventArgs e)
        {

        }

        private void pictureBox6_Click(object sender, EventArgs e)
        {

        }

        private void pictureBox11_Click(object sender, EventArgs e)
        {

        }

        private void label9_Click(object sender, EventArgs e)
        {

        }

        private void label9_Click_1(object sender, EventArgs e)
        {

        }

        private void label10_Click(object sender, EventArgs e)
        {

        }

        private void KeyIsDown(object sender, KeyEventArgs e)
        {
            if (isRoundTransition) return;

            if (e.KeyCode == Keys.Left && !noleft)
            {
                goright = godown = goup = false;
                noright = nodown = noup = false;
                goleft = true;
                pacman.Image = Properties.Resources.pacman_left;
                _client?.SetCurrentDir(SMoveDir.Left);
            }
            else if (e.KeyCode == Keys.Right && !noright)
            {
                goleft = goup = godown = false;
                noleft = noup = nodown = false;
                goright = true;
                pacman.Image = Properties.Resources.pacman_right;
                _client?.SetCurrentDir(SMoveDir.Right);
            }
            else if (e.KeyCode == Keys.Up && !noup)
            {
                goleft = goright = godown = false;
                noleft = noright = nodown = false;
                goup = true;
                pacman.Image = Properties.Resources.pacman_up;
                _client?.SetCurrentDir(SMoveDir.Up);
            }
            else if (e.KeyCode == Keys.Down && !nodown)
            {
                goleft = goright = goup = false;
                noleft = noright = noup = false;
                godown = true;
                pacman.Image = Properties.Resources.pacman_down;
                _client?.SetCurrentDir(SMoveDir.Down);
            }
        }


        //// 🔽🔽🔽 여기에 바로 추가 🔽🔽🔽
        private SMoveDir ComputeHeldDir()
        {
            if (goleft && !noleft) return SMoveDir.Left;
            if (goright && !noright) return SMoveDir.Right;
            if (goup && !noup) return SMoveDir.Up;
            if (godown && !nodown) return SMoveDir.Down;
            return SMoveDir.None;
        }

        //// 🔼🔼🔼 여기 추가 끝 🔼🔼🔼

        private void Form1_KeyUp(object sender, KeyEventArgs e)
        {
            // 떼어진 키만 false로
            if (e.KeyCode == Keys.Left) goleft = false;
            if (e.KeyCode == Keys.Right) goright = false;
            if (e.KeyCode == Keys.Up) goup = false;
            if (e.KeyCode == Keys.Down) godown = false;

            // 아직 눌린 다른 방향이 있으면 그쪽으로 즉시 전환 (None으로 잠깐 멈추지 않음)
            var dir = ComputeHeldDir();      // 타입: SMoveDir
            _client?.SetCurrentDir(dir);
        }





        private void GameTimerEvent(object sender, EventArgs e)
        {
            // 서버 권위일 때는 클라 쪽 물리/충돌/코인/유령 판정 전부 스킵
            if (_serverAuthoritative)
            {
                // (그림은 OnSnapshot -> UpdatePlayersFromSnapshot에서만 수행)
                return;
            }

            noleft = noright = noup = nodown = false;

            // 이동 (클라 물리)
            PlayerMovements();

            // ★ 클라에서 벽충돌/코인수집 항상 수행 (서버 완성 전 임시)
            foreach (Control wall in walls)
                CheckBoundaries(pacman, wall);

            foreach (var coin in coins)
            {
                if (!coin.Visible) continue;
                CollectingCoins(pacman, coin);
            }

            // 유령 이동/충돌
            var pacDir = GetPacDir();
            if (!isRoundTransition)
            {
                foreach (var g in ghosts)
                {
                    g.GhostMovement(pacman, walls, (MoveDir)pacDir);
                    var parentSize = (g.image.Parent ?? this).ClientSize;
                    ClampInto(parentSize, g.image);

                    // ★ 게임오버 판정도 클라에서 수행 (임시)
                    GhostCollision(pacman, g.image);
                }
            }

            // 🔕 서버 좌표 강제보정 코드는 주석 유지
            // if (_serverAuthoritative && _lastSnapshot != null)
            //     pacman.Location = new Point(_lastSnapshot.X, _lastSnapshot.Y);
        }




        // 시작버튼 누르면 => 라운드 1시작!
        private void StartButtonClick(object sender, EventArgs e)
        {
            // 메뉴 숨기기
            panelMenu.Enabled = false;
            panelMenu.Visible = false;

            // 서버 권위 스프라이트 싹 정리 (서버 모드일 때 기존 잔상 제거)
            if (_serverAuthoritative) ClearPlayerSprites();

            // 입력/상태 초기화
            goleft = goright = goup = godown = false;
            noleft = noright = noup = nodown = false;

            // 스코어/라운드 초기화
            score = 0;
            round = 1;

            // 폼이 키 이벤트를 먼저 받도록
            this.KeyPreview = true;
            // 포커스 강제 (간혹 패널/버튼이 키 입력을 먹는 문제 방지)
            this.ActiveControl = null;
            this.Focus();

            // 다음 라운드 세팅
            NextRound();

            // 게임 타이머 시작
            GameTimer.Start();
        }



        private void NextRound()
        {
            isRoundTransition = true;

            // 이동/막힘 플래그 리셋
            goleft = goright = goup = godown = false;
            noleft = noright = noup = nodown = false;

            pacman.Location = pacmanStart;

            // 👇 추가: 팩맨 방향 초기화
            pacman.Image = Properties.Resources.pacman_right;

            ShowCoins();
            UpdateScoreUI();
            ShowCenterPopup($"Round {round} Start!");
        }




        private void SetUp()
        {
            // 이미 고스트가 생성되어 있다면 다시 만들지 않음
            if (ghosts.Count > 0)
                return;

            // 모든 하위 컨트롤(패널 내부까지) 싹 긁어오기
            var all = GetAllChildren(this).ToList();

            walls = all.Where(c =>
                string.Equals(c.Tag as string, "wall", StringComparison.OrdinalIgnoreCase)
            ).ToList();

            coins = all.OfType<PictureBox>().Where(pb =>
                string.Equals(pb.Tag as string, "coin", StringComparison.OrdinalIgnoreCase)
            ).ToList();

            // 필요시 일시 확인
            // this.Text = $"walls:{walls.Count} coins:{coins.Count}";

            // pacman과 같은 컨테이너(Parent)를 기준으로 생성

            // pacman과 같은 컨테이너
            var canvas = pacman.Parent ?? this;

            // 배치 파라미터
            int w = 50, h = 50;
            int mx = 120, my = 120;

            // 👻 고스트 생성 + 고유 패턴 지정
            red = new Ghost(canvas, Properties.Resources.red, mx, my, GhostAI.Chase);       // 레드: 추적
            ghosts.Add(red);

            blue = new Ghost(canvas, Properties.Resources.blue, canvas.ClientSize.Width - mx - w, my, GhostAI.Random);     // 블루: 랜덤
            ghosts.Add(blue);

            yellow = new Ghost(canvas, Properties.Resources.yellow, mx, canvas.ClientSize.Height - my - h, GhostAI.AvoidWalls); // 옐로: 벽회피
            ghosts.Add(yellow);

            pink = new Ghost(canvas, Properties.Resources.pink,
                canvas.ClientSize.Width - mx - w - 80,
                canvas.ClientSize.Height - my - h - 80,
                GhostAI.Predict);
            ghosts.Add(pink);

            // 👇 여기에 추가
            ghostStartPositions = ghosts.Select(g => g.image.Location).ToList();



            // 안전: 화면 안으로 보정 + 맨 앞으로
            var bounds = canvas.ClientSize;
            foreach (var g in ghosts)
            {
                ClampInto(bounds, g.image);
                g.image.BringToFront();
            }
        }

        // 화면 밖으로 나가지 않도록 강제로 위치 보정하는 함수
        private void ClampInto(Size cs, Control c)
        {
            if (c.Left < 0) c.Left = 0;
            if (c.Top < 0) c.Top = 0;
            if (c.Right > cs.Width) c.Left = cs.Width - c.Width;
            if (c.Bottom > cs.Height) c.Top = cs.Height - c.Height;
        }


        private void panelMenu_Paint(object sender, PaintEventArgs e)
        {

        }

        private void PlayerMovements()
        {
            if (isRoundTransition) return;        // 라운드 전환 중 이동 멈춤
            if (_serverAuthoritative) return;     // 서버 권위면 로컬 이동 금지 (지금은 false라 통과)

            // === 실제 로컬 이동 ===
            if (goleft && !noleft) pacman.Left -= speed;
            if (goright && !noright) pacman.Left += speed;
            if (goup && !noup) pacman.Top -= speed;
            if (godown && !nodown) pacman.Top += speed;

            // (선택) 화면 워프 이동 유지하고 싶으면 주석 해제
            // if (pacman.Left < -30)                      pacman.Left = this.ClientSize.Width - pacman.Width;
            // if (pacman.Left + pacman.Width > this.ClientSize.Width) pacman.Left = -10;
            // if (pacman.Top < -30)                       pacman.Top = this.ClientSize.Height - pacman.Height;
            // if (pacman.Top + pacman.Height > this.ClientSize.Height) pacman.Top = -10;

            ScreenWrap();   // ✅ 이 한 줄 추가
        }


        // 화면 밖으로 나가면 반대편에서 나오게 하는 전역 래핑
        private void ScreenWrap()
        {
            var canvas = pacman.Parent ?? this;
            int W = canvas.ClientSize.Width;
            int H = canvas.ClientSize.Height;

            // 수평 래핑
            if (pacman.Right < 0)
                pacman.Left = W - 1;
            else if (pacman.Left > W)
                pacman.Left = -pacman.Width + 1;

            // 수직 래핑이 필요하면 켜고, 아니면 아래 두 줄은 지워도 됨
            if (pacman.Bottom < 0)
                pacman.Top = H - 1;
            else if (pacman.Top > H)
                pacman.Top = -pacman.Height + 1;
        }



        private void ShowCoins()
        {
            foreach (var c in coins)
            {
                c.Visible = true;
                // 필요하면 위치 리셋도 여기서 가능
                // c.Location = new Point(c.Location.X, c.Location.Y);
            }
        }

        private void CheckBoundaries(PictureBox pacman, Control wall)
        {
            if (!pacman.Bounds.IntersectsWith(wall.Bounds))
                return;

            // 왼쪽으로 이동 중 → 벽의 오른쪽에 딱 붙여서 멈춤
            if (goleft)
            {
                noleft = true;
                goleft = false;
                pacman.Left = wall.Right + 2;
            }

            // 오른쪽으로 이동 중 → 벽의 왼쪽에 딱 붙여서 멈춤
            if (goright)
            {
                noright = true;
                goright = false;
                pacman.Left = wall.Left - pacman.Width - 2;
            }

            // ↑ 여기까지가 가로 충돌 

            // ↓ 여기부터 세로 충돌을 추가

            // 위로 이동 중 → 벽의 아래쪽에 딱 붙여서 멈춤
            if (goup)
            {
                noup = true;
                goup = false;
                pacman.Top = wall.Bottom + 2;
            }

            // 아래로 이동 중 → 벽의 위쪽에 딱 붙여서 멈춤
            if (godown)
            {
                nodown = true;
                godown = false;
                pacman.Top = wall.Top - pacman.Height - 2;
            }
        }



        private void CollectingCoins(PictureBox pacman, PictureBox coin)
        {
            if (!coin.Visible || isRoundTransition) return;

            if (pacman.Bounds.IntersectsWith(coin.Bounds))
            {
                coin.Visible = false;
                score += 10;
                UpdateScoreUI();

                if (coins.All(c => !c.Visible))
                {
                    isRoundTransition = true;
                    int bonus = 100 + (round - 1) * 50;
                    score += bonus;
                    UpdateScoreUI();

                    ShowCenterPopup($"똬잇! Round {round} Clear!  +{bonus}");
                    round += 1;

                    this.BeginInvoke(new Action(() =>
                    {
                        NextRound();
                    }));
                }
            }
        }


        private void GhostCollision(PictureBox pacman, PictureBox ghost)
        {
            // 서로 다른 컨테이너여도 안전한 충돌 판정
            Rectangle AbsBounds(Control c) => c.RectangleToScreen(c.Bounds);

            if (AbsBounds(pacman).IntersectsWith(AbsBounds(ghost)))
            {
                GameTimer.Stop();
                ShowGameOverOverlay();
            }
        }



        private void GameOver()
        {
            GameTimer.Stop();
            MessageBox.Show("CLEAR! Score: " + score, "Pacman");
            panelMenu.Enabled = true;
            panelMenu.Visible = true;
        }

        private static IEnumerable<Control> GetAllChildren(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (var grand in GetAllChildren(child))
                    yield return grand;
            }
        }
    }
}
