using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;

namespace OC.Dialogue
{
    /// <summary>
    /// Resources/Data/scenario(.json) 또는 임의 TextAsset 으로부터 ScenarioData 를 로드한다.
    /// BeatType 등 enum 은 문자열로 읽도록 StringEnumConverter 를 사용 → JSON 가독성 ↑.
    /// </summary>
    public static class ScenarioLoader
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Converters = { new StringEnumConverter() },
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };

        public static ScenarioData FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                Debug.LogError("[ScenarioLoader] 빈 시나리오 JSON.");
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<ScenarioData>(json, Settings);
            }
            catch (JsonException e)
            {
                Debug.LogError($"[ScenarioLoader] 시나리오 파싱 실패: {e.Message}");
                return null;
            }
        }

        /// <summary>Resources/Data/{resourceName}.json 에서 로드 (확장자 제외 경로).</summary>
        public static ScenarioData LoadFromResources(string resourceName = "Data/scenario")
        {
            var asset = Resources.Load<TextAsset>(resourceName);
            if (asset == null)
            {
                Debug.LogError($"[ScenarioLoader] Resources/{resourceName} 를 찾을 수 없음. " +
                               "scenario.json 을 Assets/Resources/Data/ 아래에 두었는지 확인.");
                return null;
            }
            return FromJson(asset.text);
        }

        /// <summary>에디터/디버그용 직렬화(검증·툴링에 사용).</summary>
        public static string ToJson(ScenarioData data)
        {
            return JsonConvert.SerializeObject(data, Formatting.Indented, Settings);
        }
    }
}
