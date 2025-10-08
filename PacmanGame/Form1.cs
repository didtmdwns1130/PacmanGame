using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

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

        // 👇 여기에 추가
        Panel gameOverPanel;
        Label gameOverLabel;
        Button btnRetry, btnExit;

        // ← 여기에 한 줄 추가
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


        private MoveDir GetPacDir()
        {
            if (goleft) return MoveDir.Left;
            if (goright) return MoveDir.Right;
            if (goup) return MoveDir.Up;
            if (godown) return MoveDir.Down;
            return MoveDir.None;
        }


        public Form1()
        {
            InitializeComponent();

            pacmanStart = pacman.Location;   // 팩맨 시작 위치 저장
            PrepareCenterPopup();            // 중앙 팝업 준비
            PrepareGameOverUI();             // 👈 이 줄 추가


            SetUp();                         // 벽/코인 수집 (한 번만)

            this.DoubleBuffered = true;  // ← 깜빡임 감소
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

        private void Form1_Load(object sender, EventArgs e)
        {

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
            if (isRoundTransition) return; // 라운드 전환 중 입력 무시

            if (e.KeyCode == Keys.Left && !noleft)
            {
                goright = godown = goup = false;
                noright = nodown = noup = false;
                goleft = true;
                pacman.Image = Properties.Resources.pacman_left;
            }

            if (e.KeyCode == Keys.Right && !noright)
            {
                goleft = goup = godown = false;
                noleft = noup = nodown = false;
                goright = true;
                pacman.Image = Properties.Resources.pacman_right;
            }

            if (e.KeyCode == Keys.Up && !noup)
            {
                goleft = goright = godown = false;
                noleft = noright = nodown = false;
                goup = true;
                pacman.Image = Properties.Resources.pacman_up;
            }

            if (e.KeyCode == Keys.Down && !nodown)
            {
                goleft = goright = goup = false;
                noleft = noright = noup = false;
                godown = true;
                pacman.Image = Properties.Resources.pacman_down;
            }
        }



        private void GameTimerEvent(object sender, EventArgs e)
        {
            noleft = noright = noup = nodown = false;

            PlayerMovements();

            foreach (Control wall in walls)
                CheckBoundaries(pacman, wall);

            // 안 보이는 코인은 건너뜀
            foreach (var coin in coins)
            {
                if (!coin.Visible) continue;
                CollectingCoins(pacman, coin);
            }
            
            var pacDir = GetPacDir();
            // 👇 팩맨이 멈춰 있을 때는 고스트도 멈춤
            if (!isRoundTransition)
            {// 팩맨 현재 진행 방향
                foreach (var g in ghosts)                 // 고스트 이동 호출 (벽, 방향 전달)
                {
                    g.GhostMovement(pacman, walls, pacDir);

                    // 추가: 고스트를 자기 부모 컨테이너의 경계 안으로 강제 보정
                    var parentSize = (g.image.Parent ?? this).ClientSize;
                    ClampInto(parentSize, g.image);

                    // ★ 추가: 팩맨-고스트 충돌 시 게임오버
                    GhostCollision(pacman, g.image);
                }
            }
        }

        // 시작버튼 누르면 => 라운드 1시작!
        private void StartButtonClick(object sender, EventArgs e)
        {
            // 메뉴 숨기기
            panelMenu.Enabled = false;
            panelMenu.Visible = false;

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
            if (isRoundTransition) return; // 라운드 전환 중 이동 멈춤

            if (goleft) pacman.Left -= speed;
            if (goright) pacman.Left += speed;
            if (goup) pacman.Top -= speed;
            if (godown) pacman.Top += speed;

            // 화면 좌우/상하 순간이동
            if (pacman.Left < -30)
            {
                pacman.Left = this.ClientSize.Width - pacman.Width;
            }
            if (pacman.Left + pacman.Width > this.ClientSize.Width)
            {
                pacman.Left = -10;
            }
            if (pacman.Top < -30)
            {
                pacman.Top = this.ClientSize.Height - pacman.Height;
            }
            if (pacman.Top + pacman.Height > this.ClientSize.Height)
            {
                pacman.Top = -10;
            }
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

