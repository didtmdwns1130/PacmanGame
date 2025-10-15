using System;
using System.Windows.Forms;

namespace PacmanGame
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new StartForm());   // StartForm부터 시작
        }
    }
}
