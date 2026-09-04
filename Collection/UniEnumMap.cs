using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Cocoa.Lib.Collection
{
    /// <summary>
    /// 누락된 키에 대한 처리 정책.
    /// </summary>
    public enum MissingKeyPolicy
    {
        /// <summary>default(TValue)를 반환합니다 (가장 안전).</summary>
        ReturnDefault,
        /// <summary>지정된 fallback 값을 반환합니다.</summary>
        ReturnFallback,
        /// <summary>KeyNotFoundException을 발생시킵니다 (엄격 모드).</summary>
        Throw
    }

    [Serializable]
    public class UniEnumMap<TEnum, TValue>
        : IEnumerable<KeyValuePair<TEnum, TValue>>,
          ISerializationCallbackReceiver
        where TEnum : struct, Enum
    {
        #region Fields

        private Dictionary<TEnum, TValue> _entries;

        //== 직렬화 백킹 (JsonUtility/Inspector round-trip). 정책(MissingPolicy/FallbackValue)은 직렬화하지 않음.
        [SerializeField] private List<UniPair<TEnum, TValue>> _serialized = new List<UniPair<TEnum, TValue>>();

        #endregion

        #region Properties

        /// <summary>누락된 키에 대한 처리 정책. 기본값은 ReturnDefault.</summary>
        public MissingKeyPolicy MissingPolicy { get; set; } = MissingKeyPolicy.ReturnDefault;

        /// <summary>MissingPolicy가 ReturnFallback일 때 사용되는 값.</summary>
        public TValue FallbackValue { get; set; } = default;

        /// <summary>현재 매핑된 항목 수.</summary>
        public int Count
        {
            get { return _entries.Count; }
        }

        /// <summary>이 enum 타입의 전체 멤버 수.</summary>
        public int TotalEnumCount
        {
            get { return Enum.GetValues(typeof(TEnum)).Length; }
        }

        /// <summary>모든 enum 멤버가 매핑되어 있는지 여부.</summary>
        public bool IsComplete
        {
            get { return _entries.Count == TotalEnumCount; }
        }

        #endregion

        #region Constructors

        /// <summary>비어 있는 사전을 생성합니다.</summary>
        public UniEnumMap()
        {
            _entries = new Dictionary<TEnum, TValue>();
        }

        /// <summary>기존 사전으로부터 생성합니다.</summary>
        public UniEnumMap(IDictionary<TEnum, TValue> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            _entries = new Dictionary<TEnum, TValue>(source);
        }

        /// <summary>키-값 시퀀스로부터 생성합니다.</summary>
        public UniEnumMap(IEnumerable<KeyValuePair<TEnum, TValue>> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            _entries = new Dictionary<TEnum, TValue>();
            foreach (var pair in source)
            {
                _entries[pair.Key] = pair.Value;
            }
        }

        #endregion

        #region Indexers

        /// <summary>
        /// 키로 값을 조회합니다. 키가 없으면 MissingPolicy에 따라 처리합니다.
        /// </summary>
        public TValue this[TEnum key]
        {
            get
            {
                if (_entries.TryGetValue(key, out var value))
                {
                    return value;
                }
                return HandleMissingKey(key);
            }
            set
            {
                _entries[key] = value;
            }
        }

        #endregion

        #region Accessors

        /// <summary>키가 매핑되어 있는지 확인합니다.</summary>
        public bool ContainsKey(TEnum key)
        {
            return _entries.ContainsKey(key);
        }

        /// <summary>키-값을 안전하게 조회합니다.</summary>
        public bool TryGetValue(TEnum key, out TValue value)
        {
            return _entries.TryGetValue(key, out value);
        }

        /// <summary>키가 없으면 fallback 값을 반환합니다.</summary>
        public TValue GetOrDefault(TEnum key, TValue fallback = default)
        {
            if (_entries.TryGetValue(key, out var value))
            {
                return value;
            }
            return fallback;
        }

        #endregion

        #region Mutators

        /// <summary>키-값을 추가하거나 기존 값을 덮어씁니다.</summary>
        public void Set(TEnum key, TValue value)
        {
            _entries[key] = value;
        }

        /// <summary>키가 없을 때만 추가합니다.</summary>
        public bool TryAdd(TEnum key, TValue value)
        {
            return _entries.TryAdd(key, value);
        }

        /// <summary>키를 제거합니다.</summary>
        public bool Remove(TEnum key)
        {
            return _entries.Remove(key);
        }

        /// <summary>모든 매핑을 제거합니다.</summary>
        public void Clear()
        {
            _entries.Clear();
        }

        #endregion

        #region Enum Completeness

        /// <summary>아직 매핑되지 않은 enum 멤버들을 반환합니다.</summary>
        public IEnumerable<TEnum> GetMissingKeys()
        {
            foreach (TEnum value in Enum.GetValues(typeof(TEnum)))
            {
                if (!_entries.ContainsKey(value))
                {
                    yield return value;
                }
            }
        }

        /// <summary>누락된 키들을 동일한 값으로 채웁니다.</summary>
        public void FillMissing(TValue fallbackValue)
        {
            foreach (TEnum value in Enum.GetValues(typeof(TEnum)))
            {
                if (!_entries.ContainsKey(value))
                {
                    _entries[value] = fallbackValue;
                }
            }
        }

        /// <summary>누락된 키들을 팩토리 함수로 채웁니다.</summary>
        public void FillMissing(Func<TEnum, TValue> factory)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }
            foreach (TEnum value in Enum.GetValues(typeof(TEnum)))
            {
                if (!_entries.ContainsKey(value))
                {
                    _entries[value] = factory(value);
                }
            }
        }

        #endregion

        #region Enumeration

        public IEnumerator<KeyValuePair<TEnum, TValue>> GetEnumerator()
        {
            return _entries.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion

        #region Static Factory Methods

        /// <summary>
        /// 구분자로 나뉜 문자열로부터 사전을 구축합니다.
        /// enum 멤버 순서대로 값이 매핑됩니다.
        /// 데이터가 부족하면 가능한 만큼만 매핑하고 나머지는 누락 상태로 둡니다.
        /// 데이터가 더 많으면 남는 부분은 무시됩니다.
        /// </summary>
        public static UniEnumMap<TEnum, TValue> FromDelimitedString(
            string source,
            Func<string, TValue> parser,
            char delimiter = '^',
            Action<TEnum, string, Exception> onParseError = null)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (parser == null)
            {
                throw new ArgumentNullException(nameof(parser));
            }
            string[] parts = source.Split(delimiter);
            return FromOrderedValues(parts, parser, onParseError);
        }

        /// <summary>
        /// 기본 타입(int, float, double, long, bool, string, enum)에 대해 파서를 자동 적용합니다.
        /// </summary>
        public static UniEnumMap<TEnum, TValue> FromDelimitedString(
            string source,
            char delimiter = '^',
            Action<TEnum, string, Exception> onParseError = null)
        {
            return FromDelimitedString(source, GetDefaultParser(), delimiter, onParseError);
        }

        /// <summary>
        /// 순서대로 정렬된 값 배열로부터 사전을 구축합니다.
        /// enum 멤버 순서대로 매핑되며, 길이가 달라도 깨지지 않습니다.
        /// </summary>
        public static UniEnumMap<TEnum, TValue> FromOrderedValues(
            IList<string> rawValues,
            Func<string, TValue> parser,
            Action<TEnum, string, Exception> onParseError = null)
        {
            if (rawValues == null)
            {
                throw new ArgumentNullException(nameof(rawValues));
            }
            if (parser == null)
            {
                throw new ArgumentNullException(nameof(parser));
            }

            var result = new UniEnumMap<TEnum, TValue>();
            var enumValues = (TEnum[])Enum.GetValues(typeof(TEnum));

            //== 매칭 가능한 만큼만 처리 — 둘 중 짧은 쪽 기준
            int matchCount = Math.Min(enumValues.Length, rawValues.Count);

            for (int i = 0; i < matchCount; i++)
            {
                TEnum key = enumValues[i];
                string raw = rawValues[i];

                try
                {
                    TValue value = parser(raw);
                    result.Set(key, value);
                }
                catch (Exception ex)
                {
                    onParseError?.Invoke(key, raw, ex);
                    //== 해당 enum 키는 누락 상태로 남음
                }
            }

            return result;
        }

        /// <summary>
        /// 키-값 쌍 컬렉션으로부터 사전을 구축합니다.
        /// 알 수 없는 키(현재 enum에 정의되지 않은 키)는 무시됩니다.
        /// </summary>
        public static UniEnumMap<TEnum, TValue> FromKeyValuePairs(
            IEnumerable<KeyValuePair<string, string>> pairs,
            Func<string, TValue> parser,
            Action<TEnum, string, Exception> onParseError = null)
        {
            if (pairs == null)
            {
                throw new ArgumentNullException(nameof(pairs));
            }
            if (parser == null)
            {
                throw new ArgumentNullException(nameof(parser));
            }

            var result = new UniEnumMap<TEnum, TValue>();

            foreach (var pair in pairs)
            {
                if (!Enum.TryParse<TEnum>(pair.Key, out var enumKey))
                {
                    //== 알 수 없는 enum 키 — 라이브에서 enum 멤버가 제거된 경우 등
                    continue;
                }

                try
                {
                    TValue value = parser(pair.Value);
                    result.Set(enumKey, value);
                }
                catch (Exception ex)
                {
                    onParseError?.Invoke(enumKey, pair.Value, ex);
                }
            }

            return result;
        }

        /// <summary>
        /// TValue 타입에 맞는 기본 파서를 반환합니다.
        /// 지원 타입: enum, string, int, long, float, double, bool.
        /// </summary>
        public static Func<string, TValue> GetDefaultParser()
        {
            Type type = typeof(TValue);

            if (type.IsEnum)
            {
                return s => (TValue)Enum.Parse(type, s, ignoreCase: true);
            }
            if (type == typeof(string))
            {
                return s => (TValue)(object)s;
            }
            if (type == typeof(int))
            {
                return s => (TValue)(object)int.Parse(s, CultureInfo.InvariantCulture);
            }
            if (type == typeof(long))
            {
                return s => (TValue)(object)long.Parse(s, CultureInfo.InvariantCulture);
            }
            if (type == typeof(float))
            {
                return s => (TValue)(object)float.Parse(s, CultureInfo.InvariantCulture);
            }
            if (type == typeof(double))
            {
                return s => (TValue)(object)double.Parse(s, CultureInfo.InvariantCulture);
            }
            if (type == typeof(bool))
            {
                return s => (TValue)(object)bool.Parse(s);
            }

            throw new InvalidOperationException(
                $"No default parser available for type '{type.Name}'. " +
                $"Please provide a custom parser via FromDelimitedString(source, parser, ...).");
        }

        #endregion

        #region Serialization Callbacks

        /// <summary>직렬화 직전 호출. 매핑을 직렬화 백킹 리스트로 복사합니다.</summary>
        public void OnBeforeSerialize()
        {
            if (_serialized == null)
            {
                _serialized = new List<UniPair<TEnum, TValue>>();
            }
            _serialized.Clear();
            if (_entries == null)
            {
                return;
            }
            if (_serialized.Capacity < _entries.Count)
            {
                _serialized.Capacity = _entries.Count;
            }
            foreach (var kvp in _entries)
            {
                _serialized.Add(new UniPair<TEnum, TValue>(kvp.Key, kvp.Value));
            }
        }

        /// <summary>
        /// 역직렬화 직후 호출. 백킹 리스트로부터 매핑을 재구성합니다.
        /// JsonUtility 는 생성자를 호출하지 않으므로 _entries 가 null 일 수 있어 방어적으로 생성합니다.
        /// </summary>
        public void OnAfterDeserialize()
        {
            if (_entries == null)
            {
                _entries = new Dictionary<TEnum, TValue>();
            }
            else
            {
                _entries.Clear();
            }
            if (_serialized == null)
            {
                return;
            }
            for (int i = 0; i < _serialized.Count; i++)
            {
                var pair = _serialized[i];
                if (pair == null)
                {
                    continue;
                }
                _entries[pair.key] = pair.value;
            }
        }

        #endregion

        #region Private Helpers

        private TValue HandleMissingKey(TEnum key)
        {
            switch (MissingPolicy)
            {
                case MissingKeyPolicy.ReturnDefault:
                    {
                        return default;
                    }
                case MissingKeyPolicy.ReturnFallback:
                    {
                        return FallbackValue;
                    }
                case MissingKeyPolicy.Throw:
                    {
                        throw new KeyNotFoundException(
                            $"Enum key '{key}' is not mapped in UniEnumMap<{typeof(TEnum).Name}, {typeof(TValue).Name}>.");
                    }
                default:
                    {
                        return default;
                    }
            }
        }

        #endregion
    }
}