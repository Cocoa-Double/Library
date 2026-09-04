using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

using Cocoa.Lib.Collection;

#if UNITASK_SUPPORTED
using Task = Cysharp.Threading.Tasks.UniTask;
#else
using Task = System.Threading.Tasks.Task;
#endif

namespace Cocoa.Lib.Util
{
    [System.Serializable]
    public class Factory<T> where T : FactoryObject
    {
        #region Fields

        [SerializeField] private List<T> _innerDatas = new();
        [SerializeField, ReadOnly] private UniDictionary<string, T> _database;

        //== 풀: ID별 비활성 인스턴스 큐
        private Dictionary<string, Queue<T>> _pool;

        //== 자동 파괴 큐 / 설정
        private readonly Queue<GameObject> _destroyQueue = new();
        private int _softLimit;
        private int _hardLimit;
        private int _destroyUnit = 5;
        private bool _isAutoDestroy;
        private bool _destroyRunning;

        private FactoryObject.OnReturnHandler _cachedReturnMethod;

        #endregion

        #region Initialization

        public void ResetToDefault()
        {
            EnsureInitialized();

            _pool.Clear();
            _database.Clear();
            _destroyQueue.Clear();

            for (int i = 0; i < _innerDatas.Count; i++)
            {
                T fObject = _innerDatas[i];
                if (fObject == null)
                {
                    Log.Error($"[Factory<{typeof(T).Name}>] _innerDatas[{i}] is null", LogColor.Red);
                    continue;
                }

                if (string.IsNullOrEmpty(fObject.ID))
                {
                    Log.Error($"[Factory<{typeof(T).Name}>] _innerDatas[{i}] has empty ID", LogColor.Red);
                    continue;
                }

                if (_database.ContainsKey(fObject.ID))
                {
                    Log.Error($"[Factory<{typeof(T).Name}>] Duplicate ID '{fObject.ID}' at index {i}", LogColor.Red);
                    continue;
                }

                _pool[fObject.ID] = new Queue<T>();
                _database[fObject.ID] = fObject;
            }
        }

        private void EnsureInitialized()
        {
            if (_pool == null)
            {
                _pool = new Dictionary<string, Queue<T>>();
            }

            if (_database == null)
            {
                _database = new UniDictionary<string, T>();
            }

            if (_cachedReturnMethod == null)
            {
                _cachedReturnMethod = ReturnMethod;
            }
        }

        public void InsertNewData(T data)
        {
            if (data == null || string.IsNullOrEmpty(data.ID))
            {
                Log.Error($"[Factory<{typeof(T).Name}>] InsertNewData: invalid data", LogColor.Red);
                return;
            }

            EnsureInitialized();

            if (!_pool.ContainsKey(data.ID))
            {
                _pool.Add(data.ID, new Queue<T>());
                _innerDatas.Add(data);
            }

            if (!_database.ContainsKey(data.ID))
            {
                _database.Add(data.ID, data);
            }
        }

        #endregion

        #region Preload

        public async Task Preload(string id, int count, int objectsPerYield = 1)
        {
            EnsureInitialized();

            if (!_database.TryGetValue(id, out T prefab))
            {
                Log.Error($"[Factory<{typeof(T).Name}>] Preload: unregistered ID '{id}'", LogColor.Red);
                return;
            }

            if (!_pool.TryGetValue(id, out Queue<T> pool))
            {
                pool = new Queue<T>();
                _pool[id] = pool;
            }

            int yieldEvery = Mathf.Max(1, objectsPerYield);

            for (int i = 0; i < count; i++)
            {
                T preparation = Object.Instantiate(prefab);
                preparation.IsReturned = true;
                preparation.gameObject.SetActive(false);
                pool.Enqueue(preparation);

                if ((i + 1) % yieldEvery == 0)
                {
                    await Task.Yield();
                }
            }
        }

        #endregion

        #region EnsureCount

        public virtual void EnsureCount(string id, int needCount, Transform parent,
            List<T> container, System.Action<T> onCreateSuccess)
        {
            EnsureCount<T>(id, needCount, parent, container, onCreateSuccess);
        }

        public virtual void EnsureCount<TItem>(string id, int needCount, Transform parent,
            List<TItem> container, System.Action<TItem> onCreateSuccess)
            where TItem : T
        {
            if (container == null)
            {
                return;
            }

            if (needCount < 0)
            {
                needCount = 0;
            }

            //== 부족하면 채움
            while (container.Count < needCount)
            {
                T instance = Create(id, parent, null);
                if (instance == null)
                {
                    return;
                }

                if (instance is TItem typed)
                {
                    container.Add(typed);
                    onCreateSuccess?.Invoke(typed);
                }
                else
                {
                    Log.Error($"[Factory<{typeof(T).Name}>] EnsureCount: '{id}' is not of type {typeof(TItem).Name}", LogColor.Red);
                    instance.Return();
                    return;
                }
            }

            //== 과잉이면 뒤에서부터 회수
            while (container.Count > needCount)
            {
                int lastIndex = container.Count - 1;
                TItem item = container[lastIndex];
                container.RemoveAt(lastIndex);
                if (item != null)
                {
                    item.Return();
                }
            }
        }

        #endregion

        #region Create / Return

        public virtual T Create(string id, Transform parent = null, object initializeParam = null, System.Action<T> onCreateSuccess = null)
        {
            EnsureInitialized();

            if (!_pool.TryGetValue(id, out Queue<T> selectPool))
            {
                Log.Error($"[Factory<{typeof(T).Name}>] Create: unregistered ID '{id}'", LogColor.Red);
                return null;
            }

            if (!_database.TryGetValue(id, out T prefab))
            {
                Log.Error($"[Factory<{typeof(T).Name}>] Create: prefab missing for ID '{id}'", LogColor.Red);
                return null;
            }

            //== 풀에서 살아있는 항목 찾기, 없으면 새로 생성
            T fObject = TryAcquireFromPool(selectPool);
            if (fObject == null)
            {
                fObject = Object.Instantiate(prefab);
            }

            //== 공통 초기화
            ActivateAndAttach(fObject, prefab, parent);
            onCreateSuccess?.Invoke(fObject);
            fObject.Initialize(initializeParam);

            return fObject;
        }

        public virtual bool TryCreate(string id, out T result,
            Transform parent = null, object initializeParam = null,
            System.Action<T> onCreateSuccess = null)
        {
            if (!CanCreate(id))
            {
                result = null;
                return false;
            }

            result = Create(id, parent, initializeParam, onCreateSuccess);
            return result != null;
        }

        //== 풀에서 살아있는 인스턴스 dequeue. null/destroyed는 폐기.
        private T TryAcquireFromPool(Queue<T> pool)
        {
            while (pool.Count > 0)
            {
                T candidate = pool.Dequeue();
                if (candidate != null && candidate.gameObject != null)
                {
                    return candidate;
                }
                //== null이면 외부에서 Destroy된 것 → 폐기하고 다음 항목으로
            }

            return null;
        }

        //== 인스턴스 활성화 + 부모 설정 + transform 초기화.
        private void ActivateAndAttach(T fObject, T prefab, Transform parent)
        {
            fObject.gameObject.SetActive(true);
            fObject.OnReturn = _cachedReturnMethod;

            Transform t = fObject.transform;
            if (t.parent != parent)
            {
                t.SetParent(parent);
            }

            t.localPosition = Vector3.zero;
            t.localScale = prefab.transform.localScale;
            t.localRotation = Quaternion.identity;

            fObject.IsReturned = false;
        }

        private void ReturnMethod(FactoryObject data)
        {
            if (data == null)
            {
                return;
            }

            if (data.IsReturned)
            {
                return;
            }

            EnsureInitialized();

            T typed = data as T;
            if (typed == null)
            {
                Log.Error($"[Factory<{typeof(T).Name}>] ReturnMethod: data is not of type {typeof(T).Name}", LogColor.Red);
                return;
            }

            if (!_pool.TryGetValue(data.ID, out Queue<T> selectPool))
            {
                Log.Error($"[Factory<{typeof(T).Name}>] ReturnMethod: no pool for ID '{data.ID}'", LogColor.Red);
                if (data.gameObject != null)
                {
                    Object.Destroy(data.gameObject);
                }

                return;
            }

            data.IsReturned = true;
            data.OnReturn = null;

            //== AutoDestroy 정책 평가
            if (ShouldDestroyOnReturn(selectPool.Count))
            {
                EnqueueDestroy(data.gameObject);
                return;
            }

            //== 풀에 반납
            if (data.gameObject != null)
            {
                data.gameObject.SetActive(false);
            }

            selectPool.Enqueue(typed);
        }

        //== 자동 파괴 정책: hard 초과면 무조건, soft 초과면 절반 확률.
        private bool ShouldDestroyOnReturn(int currentPoolCount)
        {
            if (!_isAutoDestroy)
            {
                return false;
            }

            if (currentPoolCount >= _hardLimit)
            {
                return true;
            }

            if (currentPoolCount >= _softLimit && Random.value < 0.5f)
            {
                return true;
            }

            return false;
        }

        #endregion

        #region AutoDestroy

        public void SetAutoDestroy(bool autoDestroy, int softLimit, int hardLimit)
        {
            _isAutoDestroy = autoDestroy;
            _softLimit = Mathf.Max(0, softLimit);
            _hardLimit = Mathf.Max(_softLimit, hardLimit);
            _destroyUnit = Mathf.Max(_softLimit / 5, 5);

            if (_isAutoDestroy && !_destroyRunning && _destroyQueue.Count > 0)
            {
                StartDestroyLoop();
            }
        }

        private void EnqueueDestroy(GameObject fObject)
        {
            if (fObject == null)
            {
                return;
            }

            fObject.SetActive(false);
            _destroyQueue.Enqueue(fObject);

            if (!_destroyRunning)
            {
                StartDestroyLoop();
            }
        }

        private async void StartDestroyLoop()
        {
            if (_destroyRunning)
            {
                return;
            }

            _destroyRunning = true;

            try
            {
                while (_destroyQueue.Count > 0 && Application.isPlaying)
                {
                    int limit = _destroyUnit;
                    while (_destroyQueue.Count > 0 && limit-- > 0)
                    {
                        GameObject fObject = _destroyQueue.Dequeue();
                        if (fObject != null)
                        {
                            Object.Destroy(fObject);
                        }
                    }

                    await Task.Yield();
                }
            }
            finally
            {
                _destroyRunning = false;
            }
        }

        #endregion

        #region Query / Utility

        public virtual bool CanCreate(string id)
        {
            EnsureInitialized();
            return _database.ContainsKey(id);
        }

        public bool IsExist(string id)
        {
            EnsureInitialized();
            return _database.ContainsKey(id);
        }

        public (List<string> ids, System.Action UseEndCallback) GetIDs(System.Type type)
        {
            EnsureInitialized();

            List<string> ids = ListPool<string>.Get();

            for (int i = 0; i < _innerDatas.Count; i++)
            {
                T ui = _innerDatas[i];
                if (ui != null && ui.GetType() == type)
                {
                    ids.Add(ui.ID);
                }
            }

            return (ids, () => ListPool<string>.Release(ids));
        }

        //== 에디터 전용 진단. 호출 자체가 빌드에서 제거됩니다.
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public void DuplicateCheck()
        {
            HashSet<string> seen = new HashSet<string>();
            for (int i = 0; i < _innerDatas.Count; i++)
            {
                T data = _innerDatas[i];
                if (data == null)
                {
                    Log.Warning($"[Factory<{typeof(T).Name}>] Index {i}: null data", LogColor.Yellow);
                    continue;
                }

                if (!seen.Add(data.ID))
                {
                    Log.Warning($"[Factory<{typeof(T).Name}>] Duplicate ID '{data.ID}' at index {i}", LogColor.Yellow);
                }
            }
        }

        #endregion
    }
}
