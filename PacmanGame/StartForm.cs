using System;
using System.Drawing;
using System.Windows.Forms;
using System.Threading.Tasks;   // + 비동기 연결용

namespace PacmanGame
{
    public class StartForm : Form
    {
        private TextBox txtName;
        private TextBox txtIp;
        private Button btnConnect;

        public StartForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            SuspendLayout();

            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(420, 240);
            StartPosition = FormStartPosition.CenterScreen;
            Text = "PacmanClient - Start";
            BackColor = Color.FromArgb(30, 30, 30);

            var lblTitle = new Label
            {
                Text = "Pacman — Connect",
                ForeColor = Color.Gold,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                AutoSize = false,
                Size = new Size(380, 36),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(20, 18)
            };

            var lblName = new Label
            {
                Text = "Nickname",
                ForeColor = Color.Gainsboro,
                AutoSize = true,
                Location = new Point(40, 76)
            };
            txtName = new TextBox
            {
                Text = "",
                ForeColor = Color.White,
                BackColor = Color.FromArgb(45, 45, 45),
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(150, 72),
                Size = new Size(220, 26)
            };

            var lblIp = new Label
            {
                Text = "Server IP",
                ForeColor = Color.Gainsboro,
                AutoSize = true,
                Location = new Point(40, 116)
            };
            txtIp = new TextBox
            {
                Text = "127.0.0.1",
                ForeColor = Color.White,
                BackColor = Color.FromArgb(45, 45, 45),
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(150, 112),
                Size = new Size(220, 26)
            };

            btnConnect = new Button
            {
                Text = "Connect",
                ForeColor = Color.Black,
                BackColor = Color.Gold,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(140, 40),
                Location = new Point((ClientSize.Width - 140) / 2, 160)
            };
            btnConnect.FlatAppearance.BorderSize = 0;
            btnConnect.Click += BtnConnect_Click;

            AcceptButton = btnConnect;

            Controls.Add(lblTitle);
            Controls.Add(lblName);
            Controls.Add(txtName);
            Controls.Add(lblIp);
            Controls.Add(txtIp);
            Controls.Add(btnConnect);

            ResumeLayout(false);
        }

        // StartForm.cs 내부
        private async void BtnConnect_Click(object sender, EventArgs e)
        {
            string nickname = (txtName.Text ?? "").Trim();
            string serverIp = (txtIp.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(nickname))
            {
                MessageBox.Show("닉네임을 입력해 주세요.", "입력 필요", MessageBoxButtons.OK, MessageBoxIcon.Information);
                txtName.Focus();
                return;
            }
            if (string.IsNullOrWhiteSpace(serverIp))
                serverIp = "127.0.0.1";

            // 1) 먼저 GameClient 만들기
            var client = new GameClient();

            // 2) GameForm를 먼저 생성/표시해서(= 생성자에서 이벤트 구독 끝난 상태) 렌더 준비
            var game = new GameForm(nickname, serverIp, client);
            game.FormClosed += (s, _) =>
            {
                try { client.Dispose(); } catch { }
                this.Close();  // 게임창 닫히면 앱 종료
                               // 또는 Application.Exit(); 를 써도 됨
            };


            this.Hide();
            game.Show();   // ★ Show가 먼저!

            // 3) 그 다음에 서버 연결 시작
            try
            {
                // StartAsync가 없으므로 Connect를 백그라운드에서 실행
                await Task.Run(() => client.Connect(serverIp, 9000, nickname));
            }

            catch (Exception ex)
            {
                // 연결 실패 시 롤백 처리
                MessageBox.Show("서버에 연결할 수 없습니다.\n" + ex.Message, "연결 실패",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);

                if (!game.IsDisposed) game.Close();
                this.Show();
                try { client.Dispose(); } catch { }
            }
        }

    }
}
