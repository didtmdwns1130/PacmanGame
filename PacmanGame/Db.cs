// PacmanGame/Db.cs
// NuGet: MySql.Data (필수)

using System;
using System.Configuration;
using System.Threading.Tasks;
using System.Windows.Forms;
using MySql.Data.MySqlClient;

namespace PacmanGame
{
    public static class Db
    {
        // App.config의 <connectionStrings>에 "PacmanDb"가 있어야 함
        private static readonly string Cs =
            ConfigurationManager.ConnectionStrings["PacmanDb"]?.ConnectionString;

        // 동일 세션에서 같은 에러 메시지를 반복 표시하지 않기 위함
        private static bool _errorShown = false;

        public static async Task SaveNicknameOnceAsync(string nickname)
        {
            if (string.IsNullOrWhiteSpace(nickname))
                return;

            // 연결문자열 누락 시 1회만 경고
            if (string.IsNullOrWhiteSpace(Cs))
            {
                if (!_errorShown)
                {
                    _errorShown = true;
                    MessageBox.Show(
                        "DB 연결문자열(PacmanDb)을 못 찾았습니다. App.config 확인!",
                        "DB 설정 오류",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning
                    );
                }
                return;
            }

            // UNIQUE 제약이 걸려있어도 조용히 무시되도록 INSERT IGNORE 사용
            const string sql = @"INSERT IGNORE INTO nicknames(nickname) VALUES (@nick);";

            try
            {
                // ★ App.config가 반영 안 되어도 여기서 강제
                var builder = new MySqlConnectionStringBuilder(Cs ?? string.Empty)
                {
                    SslMode = MySqlSslMode.None,
                    AllowPublicKeyRetrieval = true,   // 서버 공개키 조회 허용 (8.x)
                    CharacterSet = "utf8mb4"
                };

                using (var conn = new MySqlConnection(builder.ConnectionString))
                {
                    await conn.OpenAsync().ConfigureAwait(false);
                    using (var cmd = new MySqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@nick", nickname.Trim());
                        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                // DB 오류도 1회만 팝업
                if (!_errorShown)
                {
                    _errorShown = true;
                    MessageBox.Show(
                        "닉네임 저장 실패: " + ex.Message,
                        "DB 오류",
                        MessageBoxButtons.OK, MessageBoxIcon.Error
                    );
                }

#if DEBUG
                System.Diagnostics.Debug.WriteLine("[DB] SaveNickname failed: " + ex);
#endif
            }
        }
    }
}
