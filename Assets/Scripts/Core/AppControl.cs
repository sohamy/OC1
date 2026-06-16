using UnityEngine;

namespace OC.Core
{
    /// <summary>앱 제어 공용 유틸. 종료 버튼 등에서 사용.</summary>
    public static class AppControl
    {
        /// <summary>게임 종료. 에디터에서는 플레이 모드를 정지한다.</summary>
        public static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
