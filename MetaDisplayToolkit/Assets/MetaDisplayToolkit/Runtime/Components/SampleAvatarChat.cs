using UnityEngine;

namespace MetaDisplay
{
    /// <summary>
    /// Example "brain" for the avatar: reacts to what the wearer says (Glasses3DInteraction.onHeard) with an
    /// action and a reply below the avatar. Replace <see cref="Reply"/> with your own logic (e.g. an AI model).
    /// </summary>
    [AddComponentMenu("Meta Display/Example - Avatar Chat")]
    public class SampleAvatarChat : MonoBehaviour
    {
        public Glasses3DView view;
        [Tooltip("Seconds the reply stays below the avatar.")]
        public float replySeconds = 4f;

        void Reset() => view = GetComponent<Glasses3DView>();

        void Awake()
        {
            if (view == null) view = GetComponent<Glasses3DView>();
        }

        /// <summary>Hook to Glasses3DInteraction.onHeard (the sample scene does it).</summary>
        public void OnHeard(string text)
        {
            if (view == null || string.IsNullOrEmpty(text)) return;
            var (action, reply) = Reply(text.ToLowerInvariant());
            if (action == "talk") view.PlayFor("talk", 2f);
            else view.Play(action);
            view.Say(reply, replySeconds);
        }

        /// <summary>Very small keyword matcher, only to show the flow.</summary>
        protected virtual (string action, string reply) Reply(string text)
        {
            if (Has(text, "hello", "hi", "hey", "olá", "oi")) return ("wave", "Hi! Nice to see you.");
            if (Has(text, "dance", "jump", "happy", "dança", "pula")) return ("happy", "Woo-hoo!");
            if (Has(text, "spin", "turn", "gira", "vira")) return ("spin", "Look at me spin.");
            if (Has(text, "yes", "agree", "sim")) return ("nod", "Yes, yes.");
            if (Has(text, "think", "why", "how", "pensa", "por que", "como")) return ("think", "Hmm, let me think…");
            return ("talk", "You said: " + text);
        }

        /// <summary>Whole-word match ("hi" does not match "think").</summary>
        static bool Has(string text, params string[] words)
        {
            var padded = " " + System.Text.RegularExpressions.Regex.Replace(text, @"[^\p{L}\p{N}]+", " ") + " ";
            foreach (var w in words)
                if (padded.Contains(" " + w + " ")) return true;
            return false;
        }
    }
}
