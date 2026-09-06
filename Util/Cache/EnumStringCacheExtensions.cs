using System;

namespace Cocoa.Lib.Util
{
    /// <summary><see cref="EnumStringCache{T}"/> 를 짧게 호출하기 위한 확장 메서드입니다.</summary>
    public static class EnumStringCacheExtensions
    {
        /// <summary>
        /// enum 값의 캐싱된 문자열 이름을 반환합니다.
        /// 타입 인자가 값에서 추론되므로 EnumStringCache{T}.Get 대신 value.ToCachedString 형태로 쓸 수 있습니다.
        /// </summary>
        public static string ToCachedString<T>(this T value) where T : struct, Enum
        {
            return EnumStringCache<T>.Get(value);
        }
    }
}
