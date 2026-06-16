using SFB;
using UnityEngine;

namespace OC.Characters
{
    /// <summary>
    /// StandaloneFileBrowser 래퍼. 데스크톱에서 이미지 파일 경로를 고른다.
    /// (SFB 미설치 시 이 파일만 컴파일 에러 — Assets/Plugins/StandaloneFileBrowser 확인)
    /// </summary>
    public static class FileDialog
    {
        private static readonly ExtensionFilter[] ImageFilters =
        {
            new ExtensionFilter("이미지", "png", "jpg", "jpeg", "PNG", "JPG", "JPEG"),
            new ExtensionFilter("모든 파일", "*"),
        };

        /// <summary>이미지 파일 경로를 고른다. 취소하면 빈 배열.</summary>
        public static string[] PickImages(bool allowMultiple)
        {
            return StandaloneFileBrowser.OpenFilePanel("자캐 사진 선택", "", ImageFilters, allowMultiple);
        }
    }
}
