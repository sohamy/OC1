using System.Collections.Generic;

namespace OC.Core
{
    public enum PlayerSlot { Player1 = 0, Player2 = 1 }

    /// <summary>
    /// 한 플레이어(자캐)의 런타임 커스텀 정보. 이름 + 표정/포즈 이미지 인덱스 목록.
    /// 실제 이미지 바이트는 CharacterImageStore 가 보관하고, 여기서는 이름만 다룬다.
    /// </summary>
    public class PlayerProfile
    {
        public PlayerSlot Slot;
        public string DisplayName = "이름 없음";
        /// <summary>등록된 표정/포즈 이미지 개수(0이면 미설정).</summary>
        public int PortraitCount;
    }

    /// <summary>
    /// 두 플레이어 이름 보관 + 시나리오 텍스트의 {P1}/{P2} 토큰 치환.
    /// </summary>
    public static class PlayerNames
    {
        public static string P1 = "{P1}";
        public static string P2 = "{P2}";

        public static void Set(PlayerSlot slot, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            if (slot == PlayerSlot.Player1) P1 = name;
            else P2 = name;
        }

        public static string Of(PlayerSlot slot) => slot == PlayerSlot.Player1 ? P1 : P2;

        /// <summary>{P1}/{P2} 토큰을 현재 이름으로 치환한다.</summary>
        public static string Substitute(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return text.Replace("{P1}", P1).Replace("{P2}", P2);
        }

        public static void Reset()
        {
            P1 = "{P1}";
            P2 = "{P2}";
        }
    }
}
