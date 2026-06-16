using System.Collections.Generic;
using OC.Core;
using UnityEngine;

namespace OC.Characters
{
    /// <summary>
    /// 두 플레이어 자캐의 런타임 커스텀 보관소(이름 + 이름표 있는 표정 사진들).
    /// 표정은 1/2/3 숫자가 아니라 시나리오에 어울리는 이름(평온·긴장·분노…)으로 고른다.
    /// </summary>
    public static class CharacterStore
    {
        /// <summary>시나리오 톤에 맞춘 표정 프리셋(커스텀에서 사진을 지정).</summary>
        public static readonly string[] Expressions = { "평온", "미소", "긴장", "불안", "분노", "슬픔", "의심", "단호" };

        private class Entry { public string name; public Sprite sprite; }

        private static readonly Dictionary<PlayerSlot, List<Entry>> _list = new Dictionary<PlayerSlot, List<Entry>>
        {
            { PlayerSlot.Player1, new List<Entry>() },
            { PlayerSlot.Player2, new List<Entry>() },
        };
        private static readonly Dictionary<PlayerSlot, int> _cur = new Dictionary<PlayerSlot, int>
        {
            { PlayerSlot.Player1, 0 },
            { PlayerSlot.Player2, 0 },
        };

        public static void SetName(PlayerSlot slot, string name) => PlayerNames.Set(slot, name);

        /// <summary>표정 이름으로 사진 지정. 같은 표정이 이미 있으면 교체.</summary>
        public static void SetPortrait(PlayerSlot slot, string expression, Sprite sprite)
        {
            if (sprite == null) return;
            var list = _list[slot];
            var existing = list.Find(e => e.name == expression);
            if (existing != null) existing.sprite = sprite;
            else list.Add(new Entry { name = expression, sprite = sprite });
        }

        public static void ClearPortraits(PlayerSlot slot) { _list[slot].Clear(); _cur[slot] = 0; }

        public static int Count(PlayerSlot slot) => _list[slot].Count;
        public static Sprite At(PlayerSlot slot, int index) =>
            (index >= 0 && index < _list[slot].Count) ? _list[slot][index].sprite : null;
        public static string NameAt(PlayerSlot slot, int index) =>
            (index >= 0 && index < _list[slot].Count) ? _list[slot][index].name : null;

        public static bool HasExpression(PlayerSlot slot, string expression) =>
            _list[slot].Exists(e => e.name == expression);

        public static void SetExpression(PlayerSlot slot, int index)
        {
            if (_list[slot].Count == 0) { _cur[slot] = 0; return; }
            _cur[slot] = Mathf.Clamp(index, 0, _list[slot].Count - 1);
        }

        public static void SetExpressionByName(PlayerSlot slot, string expression)
        {
            int i = _list[slot].FindIndex(e => e.name == expression);
            if (i >= 0) _cur[slot] = i;
        }

        public static int CurrentExpression(PlayerSlot slot) => _cur[slot];

        public static Sprite Current(PlayerSlot slot)
        {
            var list = _list[slot];
            if (list.Count == 0) return null;
            return list[Mathf.Clamp(_cur[slot], 0, list.Count - 1)].sprite;
        }

        public static void ResetAll()
        {
            ClearPortraits(PlayerSlot.Player1);
            ClearPortraits(PlayerSlot.Player2);
        }
    }
}
