using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using Cocoa.Lib.Util;

namespace Cocoa.Lib.MasterTable
{
    /// <summary>
    /// 한 시트의 모든 행을 에셋 하나에 담는 마스터 테이블입니다.
    /// 키 조회는 최초 호출 때 Dictionary 를 한 번 만들어 그 뒤로는 O(1) 로 처리합니다.
    /// </summary>
    /// <remarks>
    /// T 는 ScriptableObject 가 아니라 순수 [Serializable] 클래스입니다.
    /// 행마다 에셋을 만들면 테이블 하나에 수천 개의 파일이 생기고 로드 비용도 그만큼 늘어납니다.
    /// 제네릭 베이스에 담긴 List{T} 의 직렬화는 Unity 2020.1 부터 지원되는 제네릭 직렬화에 의존합니다.
    /// </remarks>
    public abstract class MasterTableSO<TKey, T> : MasterTableBase where T : ITableData<TKey>
    {
        #region Static Fields

        //== 제약 없는 제네릭을 null 과 비교하면 값 타입에서도 박싱이 일어나고 결과는 항상 false 입니다.
        private static readonly bool _keyCanBeNull = !typeof(TKey).IsValueType;

        #endregion

        #region Fields

        [SerializeField] protected List<T> _datas = new List<T>();

        //== Key -> 데이터. 최초 조회 때 지연 생성합니다.
        private Dictionary<TKey, T> _dataByKey;

        #endregion

        #region Properties

        /// <summary>전체 데이터. 읽기 전용으로 노출합니다.</summary>
        public IReadOnlyList<T> Datas
        {
            get
            {
                if (_datas == null)
                {
                    return Array.Empty<T>();
                }

                return _datas;
            }
        }

        public override int Count
        {
            get { return _datas != null ? _datas.Count : 0; }
        }

        #endregion

        #region Public API - Lookup

        /// <summary>키로 조회합니다. 없으면 기본값(참조 타입이면 null)을 반환합니다.</summary>
        public T Get(TKey id)
        {
            EnsureCache();
            return _dataByKey.TryGetValue(id, out T value) ? value : default;
        }

        /// <summary>키로 조회합니다. 찾으면 true 를 반환합니다.</summary>
        public bool TryGet(TKey id, out T value)
        {
            EnsureCache();
            return _dataByKey.TryGetValue(id, out value);
        }

        /// <summary>해당 키가 있는지 여부.</summary>
        public bool Contains(TKey id)
        {
            EnsureCache();
            return _dataByKey.ContainsKey(id);
        }

        #endregion

        #region Private Helpers

        private void EnsureCache()
        {
            if (_dataByKey != null)
            {
                return;
            }

            _dataByKey = new Dictionary<TKey, T>(_datas != null ? _datas.Count : 0);
            if (_datas == null)
            {
                return;
            }

            for (int i = 0; i < _datas.Count; i++)
            {
                T data = _datas[i];
                if (data == null)
                {
                    continue;
                }

                TKey key = data.Key;
                if (_keyCanBeNull && key == null)
                {
                    continue;
                }

                //== 키가 겹치면 뒤에 온 행이 이기는데, 어느 쪽이 남았는지는 조회해 보기 전까지 알 수 없습니다.
                //== Assert 로 두면 릴리스에서 ContainsKey 호출과 문자열 결합까지 함께 사라집니다.
                Log.Assert(!_dataByKey.ContainsKey(key),
                    $"[MasterTable] {GetType().Name} 의 키 '{key}' 가 여러 행에 있습니다. 마지막 행으로 덮어씁니다.");

                _dataByKey[key] = data;
            }
        }

        #endregion

#if UNITY_EDITOR
        #region Editor API

        /// <summary>생성기가 만든 데이터를 설정합니다. 타입이 맞지 않으면 아무것도 바꾸지 않습니다.</summary>
        public override void ApplyGeneratedDatas(IList datas)
        {
            List<T> typed = datas as List<T>;
            if (datas != null && typed == null)
            {
                Log.Error($"[MasterTable] {GetType().Name} 에 List<{typeof(T).Name}> 이 아닌 데이터가 들어왔습니다.", LogColor.Red);
                return;
            }

            SetDatas(typed);
        }

        /// <summary>데이터를 통째로 설정합니다. 캐시는 다음 조회 때 다시 만들어집니다.</summary>
        public void SetDatas(List<T> datas)
        {
            _datas = datas;
            _dataByKey = null;
        }

        /// <summary>조회 캐시를 버리고 다시 만듭니다.</summary>
        public override void RebuildCache()
        {
            _dataByKey = null;
            EnsureCache();
        }

        #endregion
#endif
    }
}
