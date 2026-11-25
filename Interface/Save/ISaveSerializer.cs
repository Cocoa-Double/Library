namespace Cocoa.Lib.Save
{
    /// <summary>
    /// 값과 문자열 사이의 변환을 맡는 직렬화 계약입니다. 어디에 저장할지는 ISaveStorage 가 맡습니다.
    /// </summary>
    /// <remarks>
    /// 기본 구현은 외부 의존이 없는 JsonUtilitySerializer 입니다.
    /// Dictionary, DateTime, 다형성이 필요하면 CocoaLib.Save.Newtonsoft 어셈블리의 NewtonsoftSerializer 를 넘깁니다.
    /// </remarks>
    public interface ISaveSerializer
    {
        /// <summary>값을 문자열로 직렬화합니다.</summary>
        string Serialize<T>(T value);

        /// <summary>문자열을 값으로 역직렬화합니다. 비어 있으면 default 를 반환합니다.</summary>
        T Deserialize<T>(string raw);
    }
}
