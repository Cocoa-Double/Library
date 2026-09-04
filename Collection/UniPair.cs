using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cocoa.Lib.Collection
{
    /// <summary>
    /// 직렬화 가능한 키-값 쌍.
    /// Unity Inspector에 키와 값으로 표시되며, Dictionary류 자료구조의 표시 용도 등에 적합합니다.
    /// 좌표나 두 개의 X/Y 값을 묶는 용도라면 UniVector를 사용하세요.
    /// </summary>
    [Serializable]
    public class UniPair<TKey, TValue> : IEquatable<UniPair<TKey, TValue>>
    {
        #region Fields

        [SerializeField] public TKey key;
        [SerializeField] public TValue value;

        #endregion

        #region Constructors

        public UniPair() { }

        public UniPair(TKey key, TValue value)
        {
            this.key = key;
            this.value = value;
        }

        public UniPair(UniPair<TKey, TValue> other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }
            this.key = other.key;
            this.value = other.value;
        }

        #endregion

        #region Deconstruction

        public void Deconstruct(out TKey key, out TValue value)
        {
            key = this.key;
            value = this.value;
        }

        #endregion

        #region Conversion

        public KeyValuePair<TKey, TValue> ToKeyValuePair()
        {
            return new KeyValuePair<TKey, TValue>(key, value);
        }

        public (TKey, TValue) ToTuple()
        {
            return (key, value);
        }

        #endregion

        #region Static Factory Methods

        public static UniPair<TKey, TValue> FromKeyValuePair(KeyValuePair<TKey, TValue> pair)
        {
            return new UniPair<TKey, TValue>(pair.Key, pair.Value);
        }

        #endregion

        #region Equality

        public bool Equals(UniPair<TKey, TValue> other)
        {
            if (other == null)
            {
                return false;
            }
            return EqualityComparer<TKey>.Default.Equals(key, other.key)
                && EqualityComparer<TValue>.Default.Equals(value, other.value);
        }

        public override bool Equals(object obj)
        {
            return obj is UniPair<TKey, TValue> other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (key?.GetHashCode() ?? 0);
                hash = hash * 31 + (value?.GetHashCode() ?? 0);
                return hash;
            }
        }

        public override string ToString()
        {
            return $"({key}, {value})";
        }

        #endregion

        #region Operators

        public static bool operator ==(UniPair<TKey, TValue> left, UniPair<TKey, TValue> right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }
            if (left is null || right is null)
            {
                return false;
            }
            return left.Equals(right);
        }

        public static bool operator !=(UniPair<TKey, TValue> left, UniPair<TKey, TValue> right)
        {
            return !(left == right);
        }

        #endregion
    }
}