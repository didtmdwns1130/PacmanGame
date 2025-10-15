using System;
using System.Drawing;
using System.Windows.Forms;

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

        private void BtnConnect_Click(object sender, EventArgs e)
        {
            string nick = (txtName.Text ?? "").Trim();
            string ip = (txtIp.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(nick))
            {
                MessageBox.Show("닉네임을 입력해 주세요.", "입력 필요", MessageBoxButtons.OK, MessageBoxIcon.Information);
                txtName.Focus();
                return;
            }
            if (string.IsNullOrWhiteSpace(ip)) ip = "127.0.0.1";

            // 네트워크는 다음 단계에서 — 지금은 화면 전환만
            var game = new GameForm(nick, ip);
            game.FormClosed += (s, _) => this.Close();
            Hide();
            game.Show();
        }
    }
}
