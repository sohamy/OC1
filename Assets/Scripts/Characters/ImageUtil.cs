using System.IO;
using UnityEngine;

namespace OC.Characters
{
    /// <summary>사진 로드·리사이즈·스프라이트화·네트워크 전송용 인코딩 유틸.</summary>
    public static class ImageUtil
    {
        public const int DefaultMaxSize = 512;   // 가로/세로 최대 픽셀(네트워크 전송량 제한)

        /// <summary>파일 경로에서 텍스처를 읽어 maxSize 이하로 줄여 반환. 실패 시 null.</summary>
        public static Texture2D LoadResized(string path, int maxSize = DefaultMaxSize)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                byte[] data = File.ReadAllBytes(path);
                return DecodeResized(data, maxSize);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ImageUtil] 사진 로드 실패: {e.Message}");
                return null;
            }
        }

        /// <summary>PNG/JPG 바이트를 텍스처로 디코드 후 maxSize 이하로 리사이즈.</summary>
        public static Texture2D DecodeResized(byte[] data, int maxSize = DefaultMaxSize)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(data)) return null;       // ImageConversion (png/jpg 자동 판별)
            return Resize(tex, maxSize);
        }

        /// <summary>전송용: 리사이즈된 JPG 바이트로 인코딩.</summary>
        public static byte[] EncodeForNetwork(Texture2D tex, int maxSize = DefaultMaxSize, int quality = 80)
        {
            var resized = Resize(tex, maxSize);
            return resized.EncodeToJPG(quality);
        }

        public static Texture2D Resize(Texture2D src, int maxSize)
        {
            if (src == null) return null;
            int w = src.width, h = src.height;
            int longest = Mathf.Max(w, h);
            if (longest <= maxSize) return src;

            float scale = (float)maxSize / longest;
            int nw = Mathf.Max(1, Mathf.RoundToInt(w * scale));
            int nh = Mathf.Max(1, Mathf.RoundToInt(h * scale));

            var rt = RenderTexture.GetTemporary(nw, nh, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var dst = new Texture2D(nw, nh, TextureFormat.RGBA32, false);
            dst.ReadPixels(new Rect(0, 0, nw, nh), 0, 0);
            dst.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return dst;
        }

        public static Sprite ToSprite(Texture2D tex)
        {
            if (tex == null) return null;
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
