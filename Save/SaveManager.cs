using System;
using System.Collections.Generic;
using UnityEngine;

using Cocoa.Lib.Util;

namespace Cocoa.Lib.Save
{
    /// <summary>
    /// 직렬화기와 저장 매체를 조합해 세이브/로드를 처리합니다. 버전 봉투와 마이그레이션, 자동 저장, 백그라운드 저장을 맡습니다.
    /// </summary>
    /// <remarks>
    /// 공용 인스턴스는 둘입니다. Default 는 파일 저장(본 세이브), Light 는 PlayerPrefs 저장(설정, 플래그)입니다.
    /// 직렬화기나 스키마 버전을 바꾸려면 Configure 를, 따로 쓰려면 생성자로 새 인스턴스를 만듭니다.
    /// 저장 경로는 세 가지입니다.
    /// Save 는 즉시 기록합니다.
    /// MarkDirty 는 마지막 표시 후 AutoSaveDelay 가 지나면 한 번에 기록하되, 첫 표시부터 MaxAutoSaveDelay 를 넘기지는 않습니다.
    /// 앱이 백그라운드로 내려가거나 종료될 때는 보류분을 즉시 동기로 기록합니다.
    /// MonoBehaviour 가 아니라서 씬 오브젝트 없이 동작하며, 수명 신호는 AppLifecycle 로 받습니다.
    /// </remarks>
    public sealed class SaveManager : IDisposable
    {
        #region Constants

        private const float DefaultAutoSaveDelay = 1f;
        private const float DefaultMaxAutoSaveDelay = 10f;

        #endregion

        #region Static Fields

        private static SaveManager _default;
        private static SaveManager _light;

        #endregion

        #region Static Properties

        /// <summary>파일에 저장하는 공용 인스턴스(본 세이브). 첫 접근 시 만들어집니다.</summary>
        public static SaveManager Default
        {
            get { return _default ?? (_default = new SaveManager(new FileSaveStorage())); }
        }

        /// <summary>PlayerPrefs 에 저장하는 공용 인스턴스(설정, 플래그). 첫 접근 시 만들어집니다.</summary>
        public static SaveManager Light
        {
            get { return _light ?? (_light = new SaveManager(new PlayerPrefsSaveStorage())); }
        }

        #endregion

        #region Fields

        private ISaveSerializer _serializer;
        private ISaveStorage _storage;
        private int _version;
        private bool _isDisposed;

        private readonly Dictionary<int, ISaveMigrator> _migratorByVersion = new Dictionary<int, ISaveMigrator>();

        //== 자동 저장 보류분. 기록 시점에 최신 상태로 직렬화하도록 값 대신 직렬화 함수를 들고 있습니다.
        private readonly Dictionary<string, Func<string>> _pendingByKey = new Dictionary<string, Func<string>>();
        private readonly List<string> _flushKeys = new List<string>();
        private float _firstPendingTime;
        private ScheduleHandle _autoSaveHandle;

        #endregion

        #region Properties

        /// <summary>현재 스키마 버전. 저장할 때 봉투에 기록되고, 로드할 때 이 버전까지 마이그레이션합니다.</summary>
        public int Version
        {
            get { return _version; }
            set { _version = value; }
        }

        /// <summary>마지막 MarkDirty 후 기록까지 기다리는 시간(초).</summary>
        public float AutoSaveDelay { get; set; } = DefaultAutoSaveDelay;

        /// <summary>
        /// 첫 MarkDirty 부터 기록까지 기다리는 최대 시간(초).
        /// 재화처럼 계속 바뀌는 값은 기다리는 시간이 매번 뒤로 밀려 백그라운드 전환 전까지 한 번도 기록되지 않으므로 상한을 둡니다.
        /// </summary>
        public float MaxAutoSaveDelay { get; set; } = DefaultMaxAutoSaveDelay;

        /// <summary>아직 기록하지 않은 보류분이 있는지 여부.</summary>
        public bool HasPending
        {
            get { return _pendingByKey.Count > 0; }
        }

        /// <summary>현재 저장 매체.</summary>
        public ISaveStorage Storage
        {
            get { return _storage; }
        }

        /// <summary>현재 직렬화기.</summary>
        public ISaveSerializer Serializer
        {
            get { return _serializer; }
        }

        #endregion

        #region Constructors

        /// <param name="storage">저장 매체.</param>
        /// <param name="serializer">직렬화기. null 이면 JsonUtilitySerializer 를 씁니다.</param>
        /// <param name="version">스키마 버전.</param>
        public SaveManager(ISaveStorage storage, ISaveSerializer serializer = null, int version = 1)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _serializer = serializer ?? new JsonUtilitySerializer();
            _version = version;

            //== 종료 중에 구독하면 AppLifecycle 이 수신용 오브젝트를 새로 만들게 되므로 건너뜁니다. 종료 중이라면 받을 신호도 남아 있지 않습니다.
            if (!AppLifecycle.IsQuitting)
            {
                AppLifecycle.PauseChanged += OnAppPauseChanged;
                AppLifecycle.Quitting += OnAppQuitting;
            }
        }

        #endregion

        #region Public API - Configuration

        /// <summary>
        /// 직렬화기, 저장 매체, 버전 중 넘긴 것만 바꿉니다. 저장 매체를 바꾸기 전에 보류분은 기존 매체에 먼저 기록합니다.
        /// </summary>
        public SaveManager Configure(ISaveSerializer serializer = null, ISaveStorage storage = null, int? version = null)
        {
            //== 보류분은 기존 설정으로 만든 데이터라 새 매체나 새 버전으로 넘어가기 전에 비웁니다.
            FlushPending();

            if (serializer != null)
            {
                _serializer = serializer;
            }

            if (storage != null)
            {
                _storage = storage;
            }

            if (version.HasValue)
            {
                _version = version.Value;
            }

            return this;
        }

        /// <summary>마이그레이터를 등록합니다. FromVersion 이 같으면 덮어씁니다.</summary>
        public SaveManager RegisterMigrator(ISaveMigrator migrator)
        {
            if (migrator == null)
            {
                throw new ArgumentNullException(nameof(migrator));
            }

            _migratorByVersion[migrator.FromVersion] = migrator;
            return this;
        }

        #endregion

        #region Public API - Save / Load

        /// <summary>즉시 기록합니다. 같은 키의 자동 저장 보류분은 취소됩니다.</summary>
        public SaveResult Save<T>(string key, T data)
        {
            if (string.IsNullOrEmpty(key))
            {
                Log.Error("[SaveManager] 키가 비어 있습니다.", LogColor.Red);
                return SaveResult.Failed;
            }

            _pendingByKey.Remove(key);

            try
            {
                return _storage.Write(key, BuildEnvelope(data)) ? SaveResult.Success : SaveResult.Failed;
            }
            catch (Exception exception)
            {
                Log.Error($"[SaveManager] '{key}' 저장 실패.\n{exception}", LogColor.Red);
                return SaveResult.Failed;
            }
        }

        /// <summary>
        /// 데이터를 읽습니다. 없으면 NotFound, 해석하지 못하면 Corrupted, 버전을 올릴 수 없으면 UnsupportedVersion 입니다.
        /// 저장소가 백업을 갖고 있으면 본 데이터가 손상됐을 때 백업으로 한 번 더 시도합니다.
        /// </summary>
        public LoadResult<T> Load<T>(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                Log.Error("[SaveManager] 키가 비어 있습니다.", LogColor.Red);
                return LoadResult<T>.Fail(SaveStatus.Failed);
            }

            string text;
            if (!_storage.TryRead(key, out text))
            {
                //== 파일은 있는데 읽지 못한 경우까지 NotFound 로 돌려주면, 호출하는 쪽이 새 데이터로 덮어써 세이브를 날리게 됩니다.
                return LoadResult<T>.Fail(_storage.Exists(key) ? SaveStatus.Failed : SaveStatus.NotFound);
            }

            LoadResult<T> result = Decode<T>(key, text);
            if (result.Status != SaveStatus.Corrupted)
            {
                return result;
            }

            //== 저장소는 파일이 읽히는지만 알 뿐 내용이 온전한지는 모르므로, 해석에 실패한 경우의 백업 복구는 여기서 합니다.
            ISaveBackupStorage backupStorage = _storage as ISaveBackupStorage;
            string backup;
            if (backupStorage != null && backupStorage.TryReadBackup(key, out backup) && backup != text)
            {
                LoadResult<T> restored = Decode<T>(key, backup);
                if (restored.IsSuccess)
                {
                    Log.Warning($"[SaveManager] '{key}' 가 손상되어 직전 백업에서 복원했습니다.", LogColor.Yellow);
                    return restored;
                }
            }

            return result;
        }

        /// <summary>해당 키의 데이터가 있는지 확인합니다.</summary>
        public bool Exists(string key)
        {
            return !string.IsNullOrEmpty(key) && _storage.Exists(key);
        }

        /// <summary>해당 키를 삭제합니다. 자동 저장 보류분도 함께 취소합니다.</summary>
        public SaveResult Delete(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return SaveResult.Failed;
            }

            _pendingByKey.Remove(key);
            return _storage.Delete(key) ? SaveResult.Success : SaveResult.Failed;
        }

        /// <summary>저장된 모든 키를 반환합니다.</summary>
        public IReadOnlyList<string> GetAllKeys()
        {
            return _storage.GetAllKeys();
        }

        /// <summary>
        /// 모든 키를 같은 타입 T 로 읽어 result 에 채웁니다(slot_1, slot_2 같은 슬롯 세이브용).
        /// 실패한 키는 건너뛰고 경고를 남깁니다. 채운 수를 반환합니다.
        /// </summary>
        public int LoadAll<T>(Dictionary<string, T> result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            result.Clear();
            IReadOnlyList<string> keys = _storage.GetAllKeys();
            for (int i = 0; i < keys.Count; i++)
            {
                LoadResult<T> loaded = Load<T>(keys[i]);
                if (loaded.IsSuccess)
                {
                    result[keys[i]] = loaded.Value;
                }
                else
                {
                    Log.Warning($"[SaveManager] LoadAll<{typeof(T).Name}>: '{keys[i]}' 를 건너뜁니다 ({loaded.Status}).", LogColor.Yellow);
                }
            }

            return result.Count;
        }

        /// <summary>모든 키의 저장 원문(봉투 JSON)을 반환합니다. 키마다 타입이 다를 때의 점검이나 디버그용입니다.</summary>
        public Dictionary<string, string> LoadAllRaw()
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            IReadOnlyList<string> keys = _storage.GetAllKeys();
            for (int i = 0; i < keys.Count; i++)
            {
                string text;
                if (_storage.TryRead(keys[i], out text))
                {
                    result[keys[i]] = text;
                }
            }

            return result;
        }

        #endregion

        #region Public API - Auto Save

        /// <summary>
        /// 변경을 표시합니다. 같은 키를 여러 번 표시하면 마지막 것 하나로 합쳐 한 번만 기록합니다.
        /// </summary>
        /// <remarks>
        /// 참조 타입은 기록 시점의 상태로 직렬화되므로 표시한 뒤에 바뀐 내용도 함께 저장됩니다.
        /// 값 타입은 표시한 시점의 복사본이 저장됩니다.
        /// </remarks>
        public void MarkDirty<T>(string key, T data)
        {
            if (string.IsNullOrEmpty(key))
            {
                Log.Error("[SaveManager] MarkDirty: 키가 비어 있습니다.", LogColor.Red);
                return;
            }

            if (_pendingByKey.Count == 0)
            {
                _firstPendingTime = Time.realtimeSinceStartup;
            }

            _pendingByKey[key] = () => BuildEnvelope(data);
            ScheduleAutoSave();
        }

        /// <summary>
        /// 보류분을 즉시 동기로 기록합니다. 모두 기록했으면 true.
        /// 저장소 쓰기에 실패한 키는 보류분에 남겨 다음 기록 때 다시 시도합니다.
        /// </summary>
        public bool FlushPending()
        {
            _autoSaveHandle.Unregister();
            _autoSaveHandle = ScheduleHandle.None;

            if (_pendingByKey.Count == 0)
            {
                return true;
            }

            //== 직렬화 함수가 MarkDirty 를 다시 부를 수 있으므로 키를 따로 떠 둔 뒤 돕니다.
            _flushKeys.Clear();
            _flushKeys.AddRange(_pendingByKey.Keys);

            bool allWritten = true;
            for (int i = 0; i < _flushKeys.Count; i++)
            {
                string key = _flushKeys[i];
                Func<string> build;
                if (!_pendingByKey.TryGetValue(key, out build))
                {
                    continue;
                }

                string text;
                try
                {
                    text = build();
                }
                catch (Exception exception)
                {
                    //== 직렬화 실패는 다시 시도해도 같은 결과라 보류분에서 뺍니다. 남겨 두면 기록할 때마다 같은 예외가 반복됩니다.
                    Log.Error($"[SaveManager] '{key}' 직렬화 실패로 자동 저장을 포기합니다.\n{exception}", LogColor.Red);
                    _pendingByKey.Remove(key);
                    allWritten = false;
                    continue;
                }

                if (_storage.Write(key, text))
                {
                    _pendingByKey.Remove(key);
                }
                else
                {
                    allWritten = false;
                }
            }

            _flushKeys.Clear();

            if (_pendingByKey.Count > 0)
            {
                _firstPendingTime = Time.realtimeSinceStartup;
            }

            return allWritten;
        }

        #endregion

        #region Public API - Lifecycle

        /// <summary>보류분을 기록하고 수명 신호 구독을 해제합니다. 직접 만든 인스턴스를 더 쓰지 않을 때 호출합니다.</summary>
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            FlushPending();
            Unsubscribe();
            _isDisposed = true;
        }

        #endregion

        #region Private Helpers

        private string BuildEnvelope<T>(T data)
        {
            SaveEnvelope envelope = new SaveEnvelope
            {
                version = _version,
                payload = _serializer.Serialize(data)
            };

            return _serializer.Serialize(envelope);
        }

        private LoadResult<T> Decode<T>(string key, string text)
        {
            SaveEnvelope envelope;
            try
            {
                envelope = _serializer.Deserialize<SaveEnvelope>(text);
            }
            catch (Exception exception)
            {
                Log.Warning($"[SaveManager] '{key}' 봉투를 해석하지 못했습니다.\n{exception}", LogColor.Yellow);
                return LoadResult<T>.Fail(SaveStatus.Corrupted);
            }

            if (envelope == null || envelope.payload == null)
            {
                Log.Warning($"[SaveManager] '{key}' 봉투가 비어 있습니다.", LogColor.Yellow);
                return LoadResult<T>.Fail(SaveStatus.Corrupted);
            }

            //== 더 새 버전의 앱이 쓴 세이브를 옛 형식으로 읽으면 모르는 필드가 버려지고, 그대로 다시 저장하면 데이터가 사라집니다.
            if (envelope.version > _version)
            {
                Log.Error($"[SaveManager] '{key}' 는 버전 {envelope.version} 세이브라 현재 버전 {_version} 에서 읽지 않습니다.", LogColor.Red);
                return LoadResult<T>.Fail(SaveStatus.UnsupportedVersion);
            }

            string payload = envelope.payload;
            for (int current = envelope.version; current < _version; current++)
            {
                ISaveMigrator migrator;
                if (!_migratorByVersion.TryGetValue(current, out migrator))
                {
                    //== 변환 없이 진행하면 옛 형식이 새 타입에 억지로 들어가 값이 조용히 기본값이 됩니다.
                    Log.Error($"[SaveManager] '{key}' 의 버전 {current} → {current + 1} 마이그레이터가 없습니다.", LogColor.Red);
                    return LoadResult<T>.Fail(SaveStatus.UnsupportedVersion);
                }

                try
                {
                    payload = migrator.Migrate(payload);
                }
                catch (Exception exception)
                {
                    Log.Error($"[SaveManager] '{key}' 의 버전 {current} → {current + 1} 마이그레이션 실패.\n{exception}", LogColor.Red);
                    return LoadResult<T>.Fail(SaveStatus.Failed);
                }
            }

            try
            {
                return LoadResult<T>.Success(_serializer.Deserialize<T>(payload));
            }
            catch (Exception exception)
            {
                Log.Warning($"[SaveManager] '{key}' 본문을 {typeof(T).Name} 으로 해석하지 못했습니다.\n{exception}", LogColor.Yellow);
                return LoadResult<T>.Fail(SaveStatus.Corrupted);
            }
        }

        private void ScheduleAutoSave()
        {
            _autoSaveHandle.Unregister();
            _autoSaveHandle = ScheduleHandle.None;

            float remaining = MaxAutoSaveDelay - (Time.realtimeSinceStartup - _firstPendingTime);
            float delay = Mathf.Max(0f, Mathf.Min(AutoSaveDelay, remaining));

            Scheduler scheduler = Scheduler.Default;
            if (scheduler == null || delay <= 0f)
            {
                //== 종료 중이라 스케줄러가 없거나 상한에 닿았으면 미루지 않고 바로 기록합니다.
                FlushPending();
                return;
            }

            //== timeScale 을 0 으로 둔 일시정지 화면에서도 저장이 멈추지 않도록 실제 시간으로 잽니다.
            _autoSaveHandle = scheduler.ScheduleOnce(delay, OnAutoSaveDue, true);
        }

        private void OnAutoSaveDue()
        {
            _autoSaveHandle = ScheduleHandle.None;
            FlushPending();
        }

        private void OnAppPauseChanged(bool isPaused)
        {
            //== OS 가 백그라운드 앱을 종료하기 전에 끝내야 하므로 비동기로 미루지 않습니다.
            if (isPaused)
            {
                FlushPending();
            }
        }

        private void OnAppQuitting()
        {
            FlushPending();
        }

        private void Unsubscribe()
        {
            AppLifecycle.PauseChanged -= OnAppPauseChanged;
            AppLifecycle.Quitting -= OnAppQuitting;
        }

        //== 도메인 리로드를 끈 상태로 플레이를 다시 시작하면 이전 실행의 인스턴스가 남습니다.
        //== AppLifecycle 이 구독을 이미 비웠으므로 인스턴스만 버리고 다음 접근에서 새로 만듭니다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _default = null;
            _light = null;
        }

        #endregion
    }
}
