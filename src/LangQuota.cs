using System.Collections.Generic;

namespace Deskmate
{
    static partial class Lang
    {
        static void AddQuota(Dictionary<string, string> en)
        {
            // Live quota from signed-in AI accounts
            AddOnce(en, "Claude, Codex, Antigravity, dan Copilot yang sudah masuk, langsung dari akunnya", "Signed-in Claude, Codex, Antigravity, and Copilot, read from their accounts");
            AddOnce(en, "kunci API", "API key");
            AddOnce(en, "Terhubung", "Connected");
        }
    }
}
