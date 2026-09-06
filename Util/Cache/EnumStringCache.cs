using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;

namespace Cocoa.Lib.Util
{
    /// <summary>
    /// enum 값의 문자열 이름을 캐싱합니다. Enum 의 ToString 은 박싱과 리플렉션을 거쳐 느리고 GC 를 유발하므로,
    /// 한 번 변환한 결과를 재사용합니다.
    /// </summary>
    /// <remarks>
    /// 메인 스레드 전용입니다. 여러 스레드에서 동시에 호출하지 마세요.
    /// 호출부에서는 확장 메서드 ToCachedString 으로 더 짧게 쓸 수 있습니다.
    /// </remarks>
    public static class EnumStringCache<T> where T : struct, Enum
    {
        #region Nested Types

        //== Dictionary 의 기본 비교자는 enum 에서 Equals(object) 로 떨어져 조회마다 박싱합니다.
        //== 박싱을 피하려고 만든 캐시가 조회에서 다시 박싱하면 의미가 없어 전용 비교자를 둡니다.
        private sealed class ValueComparer : IEqualityComparer<T>
        {
            public static readonly ValueComparer Default = new ValueComparer();

            public bool Equals(T x, T y)
            {
                return UnsafeUtility.EnumEquals(x, y);
            }

            //== 기저 타입이 long 인 enum 은 상위 비트가 잘려 해시가 겹칠 수 있지만, 동등 판정은 위에서 정확히 하므로 조회 결과는 맞습니다.
            public int GetHashCode(T value)
            {
                return UnsafeUtility.EnumToInt(value);
            }
        }

        #endregion

        #region Static Fields

        //== enum 값 -> 문자열 이름
        private static readonly Dictionary<T, string> _stringByValue
            = new Dictionary<T, string>(ValueComparer.Default);

        #endregion

        #region Public API - Lookup

        /// <summary>enum 값의 이름을 반환합니다. 최초 1회만 변환하고 이후에는 캐시를 씁니다.</summary>
        public static string Get(T value)
        {
            if (!_stringByValue.TryGetValue(value, out string cached))
            {
                cached = value.ToString();
                _stringByValue.Add(value, cached);
            }

            return cached;
        }

        /// <summary>해당 값이 이미 캐시에 있는지 여부.</summary>
        public static bool IsCached(T value)
        {
            return _stringByValue.ContainsKey(value);
        }

        #endregion

        #region Public API - Maintenance

        /// <summary>
        /// 모든 enum 값의 이름을 미리 캐싱합니다.
        /// 리플렉션과 박싱이 발생하므로 런타임 핫패스가 아니라 초기화 시점에 호출하세요.
        /// </summary>
        public static void Warmup()
        {
            T[] values = (T[])Enum.GetValues(typeof(T));
            for (int i = 0; i < values.Length; i++)
            {
                if (!_stringByValue.ContainsKey(values[i]))
                {
                    _stringByValue.Add(values[i], values[i].ToString());
                }
            }
        }

        /// <summary>캐시를 비웁니다.</summary>
        public static void Clear()
        {
            _stringByValue.Clear();
        }

        #endregion
    }
}
