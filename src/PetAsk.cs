using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;

namespace Deskmate
{
    // The pet as an assistant: type above it, it thinks, it answers in its bubble.
    // With Ingatan on, the answer can draw on what the user did; without it, the pet still answers through the main AI.
    partial class Island
    {
        PetChatInput petAskInput;
        readonly List<KeyValuePair<string, string>> petAskTurns = new List<KeyValuePair<string, string>>();
        DateTime petAskLastUtc = DateTime.MinValue;
        bool petAskBusy;

        // The pet's own name: the custom pet's folder, else the animal (pet.link.name is the user's own name in rooms)
        string PetAskName
        {
            get
            {
                string kind = settings.Get("pet.kind", "cat");
                return kind == "dog" ? Lang.T("Anjing") : kind == "cat" ? Lang.T("Kucing") : kind.StartsWith("custom:", StringComparison.Ordinal) ? kind.Substring(7) : "Pet";
            }
        }

        internal void OpenPetAsk()
        {
            if (pet == null || pet.IsDisposed || !pet.CanShowBubble) { AskIngat(""); return; }
            if (petAskBusy) { pet.ShowChatBubble(PetAskName, Lang.T("Hmm, sebentar…"), false, null, 30); return; }
            ClosePetChatUi();
            if (petAskInput != null && !petAskInput.IsDisposed) petAskInput.Close();
            pet.CloseChatBubble();
            string cue = IngatOn ? Lang.T("Tanya apa saja, termasuk yang tadi kamu kerjakan…") : Lang.T("Tanya apa saja…");
            petAskInput = new PetChatInput(pet.UiScale, pet.WorkArea.Width, (text, em) => PetAskSend(text), null, cue);
            petAskInput.FormClosed += delegate { petAskInput = null; };
            if (!petAskInput.MoveAbove(pet.WorkArea, new Point(pet.DrawnCenterX, pet.BubbleAnchorY)))
            {
                petAskInput.Dispose();
                petAskInput = null;
                return;
            }
            petAskInput.Show(pet);
            petAskInput.FocusInput();
        }

        void PetAskSend(string question)
        {
            question = (question ?? "").Trim();
            if (question.Length == 0 || pet == null || pet.IsDisposed) return;
            // A long pause starts a fresh conversation
            if ((DateTime.UtcNow - petAskLastUtc).TotalMinutes > 10) petAskTurns.Clear();
            petAskLastUtc = DateTime.UtcNow;
            petAskBusy = true;
            string name = PetAskName, kind = settings.Get("pet.kind", "cat");
            var history = new List<KeyValuePair<string, string>>(petAskTurns);
            bool memory = IngatOn;
            pet.PopEmote(PetEmote.Note);
            pet.ShowChatBubble(name, Lang.T("Hmm, sebentar…"), false, null, 120);

            ThreadPool.QueueUserWorkItem(delegate
            {
                string answer = null;
                try { answer = PetAskThink(question, history, name, kind, memory); }
                catch (Exception ex) { Log.Write("pet tanya: " + ex.Message); }
                if (string.IsNullOrEmpty(answer)) answer = Lang.T("Aduh, aku lagi tidak bisa berpikir. Coba lagi sebentar ya.");
                Post(delegate
                {
                    petAskBusy = false;
                    petAskTurns.Add(new KeyValuePair<string, string>("user", question));
                    petAskTurns.Add(new KeyValuePair<string, string>("assistant", answer));
                    if (petAskTurns.Count > 12) petAskTurns.RemoveRange(0, petAskTurns.Count - 12);
                    petAskLastUtc = DateTime.UtcNow;
                    if (pet == null || pet.IsDisposed) return;
                    pet.PopEmote(PetEmote.Spark);
                    double seconds = Math.Max(10, Math.Min(45, 6 + answer.Length * 0.08));
                    // Click the answer to ask a follow-up
                    pet.ShowChatBubble(name, answer, false, delegate { OpenPetAsk(); }, seconds);
                    Announce(name + ": " + answer);
                });
            });
        }

        // Worker: Ingatan's service when it runs (its own AI choice decides where memory may go), else the main AI without memory
        static string PetAskThink(string question, List<KeyValuePair<string, string>> history, string name, string kind, bool memory)
        {
            if (memory && IngatClient.Running())
            {
                var turns = new List<object>();
                foreach (var t in history) turns.Add(new Dictionary<string, object> { { "role", t.Key }, { "text", t.Value } });
                var r = IngatClient.Call("POST", "/chat", new Dictionary<string, object>
                {
                    { "q", question }, { "history", turns.ToArray() }, { "name", name }, { "kind", kind },
                }, 120000);
                // Ingatan's own AI answered; when it is not set up or failed, the main AI answers without memory
                if (r != null && Json.Str(r, "answer").Length > 0 && !r.ContainsKey("error")) return Fmt.OneLine(Json.Str(r, "answer"), 700);
            }
            string animal = kind == "dog" ? "dog" : kind == "cat" ? "cat" : "pet";
            string system = "You are " + name + ", the user's little " + animal + " living on their Windows desktop dock, and their personal assistant. " +
                "Be warm, playful, and brief: 1-3 short sentences, plain text, no Markdown, at most one emoji. Reply in the user's language. " +
                "You cannot take actions on the computer. " +
                (memory ? "Your memory of the user's activity is not available right now because the AI chosen for Ingatan is not ready; if asked about past activity, say so and suggest Settings › Ingatan › Layanan AI. "
                        : "You do not remember the user's activity right now (Ingatan is off); if asked, say it can be turned on in Settings › Ingatan. ") +
                "Local time now: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm dddd", System.Globalization.CultureInfo.InvariantCulture) + ".";
            var all = new List<KeyValuePair<string, string>>(history);
            all.Add(new KeyValuePair<string, string>("user", question));
            string error;
            var reply = Ai.Ask(system, all, null, out error);
            if (reply == null) return error != null ? Lang.F("Aku belum bisa berpikir: {0}", error) : null;
            return Fmt.OneLine(reply.Text.Trim(), 700);
        }
    }
}
