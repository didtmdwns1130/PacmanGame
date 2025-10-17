using System;
using System.Windows.Forms;

namespace PacmanGame
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            // 예외를 다이얼로그로 표시해 “조용한 종료” 방지
            Application.ThreadException += (s, e) =>
                MessageBox.Show(e.Exception.ToString(), "ThreadException",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                MessageBox.Show(((Exception)e.ExceptionObject).ToString(), "Fatal",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new StartForm());   // StartForm부터 시작
        }
    }
}
