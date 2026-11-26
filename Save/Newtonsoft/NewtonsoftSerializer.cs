using Newtonsoft.Json;

namespace Cocoa.Lib.Save
{
    /// <summary>
    /// Newtonsoft.Json 을 쓰는 직렬화기입니다. JsonUtilitySerializer 가 다루지 못하는 Dictionary, DateTime, null 이 필요할 때 씁니다.
    /// </summary>
    /// <remarks>
    /// 기본 설정은 UnitySerializationContractResolver 로 Unity 직렬화 규칙을 따릅니다.
    /// 다형성이 필요하면 TypeNameHandling 을 켠 설정을 넘기되, 저장 데이터에 적힌 타입을 그대로 만들게 되므로 신뢰할 수 있는 데이터에만 씁니다.
    /// 이 어셈블리는 com.unity.nuget.newtonsoft-json 패키지가 있어 COCOALIB_NEWTONSOFT 가 정의됐을 때만 컴파일됩니다.
    /// </remarks>
    public sealed class NewtonsoftSerializer : ISaveSerializer
    {
        #region Fields

        private readonly JsonSerializerSettings _settings;

        #endregion

        #region Constructors

        /// <param name="settings">
        /// 직렬화 설정. null 이면 CreateDefaultSettings 를 씁니다.
        /// ContractResolver 가 비어 있으면 넘긴 객체에 UnitySerializationContractResolver 를 채워 넣습니다.
        /// </param>
        public NewtonsoftSerializer(JsonSerializerSettings settings = null)
        {
            _settings = settings ?? CreateDefaultSettings();

            //== resolver 를 비워 두면 Newtonsoft 기본 규칙으로 돌아가 Uni 컨테이너와 Unity 구조체가 다르게 기록됩니다.
            if (_settings.ContractResolver == null)
            {
                _settings.ContractResolver = UnitySerializationContractResolver.Instance;
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// 기본 설정을 만듭니다. PooledJsonSerializer 도 이 설정을 써서 두 직렬화기의 결과가 같습니다.
        /// </summary>
        public static JsonSerializerSettings CreateDefaultSettings()
        {
            return new JsonSerializerSettings
            {
                Formatting = Formatting.None,
                NullValueHandling = NullValueHandling.Include,
                ContractResolver = UnitySerializationContractResolver.Instance
            };
        }

        public string Serialize<T>(T value)
        {
            return JsonConvert.SerializeObject(value, typeof(T), _settings);
        }

        public T Deserialize<T>(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return default;
            }

            return JsonConvert.DeserializeObject<T>(raw, _settings);
        }

        #endregion
    }
}
