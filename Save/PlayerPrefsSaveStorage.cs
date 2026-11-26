using System.Collections.Generic;
using UnityEngine;

using Cocoa.Lib.Util;

namespace Cocoa.Lib.Save
{
    /// <summary>
    /// PlayerPrefs 에 저장하는 가벼운 저장소입니다. 설정값이나 플래그처럼 작은 데이터에 씁니다.
    /// </summary>
    /// <remarks>
    /// 쓰기의 원자성은 PlayerPrefs 구현에 맡기므로 진행도 같은 본 세이브에는 FileSaveStorage 를 씁니다.
    /// PlayerPrefs 에는 키를 나열하는 API 가 없어 GetAllKeys 를 위해 키 목록을 별도 키에 함께 보관합니다.
    /// 키 목록은 인스턴스가 메모리에 들고 있으므로 같은 PlayerPrefs 를 쓰는 인스턴스는 SaveManager.Light 하나로 모아 씁니다.
    /// </remarks>
    public sealed class PlayerPrefsSaveStorage : ISaveStorage
    {
        #region Constants

        private const string KeyPrefix = "Cocoa.Lib.Save.";
        private const string ManifestKey = "Cocoa.Lib.Save.__keys__";
        private const char ManifestSeparator = '\n';

        #endregion

        #region Fields

        //== 매번 PlayerPrefs 에서 목록 문자열을 읽어 나누지 않도록 처음 한 번만 읽어 둡니다.
        private List<string> _keys;
        private HashSet<string> _keySet;

        #endregion

        #region Public API

        public bool Exists(string key)
        {
            return IsValidKey(key) && PlayerPrefs.HasKey(KeyPrefix + key);
        }

        public bool Write(string key, string data)
        {
            if (!IsValidKey(key))
            {
                Log.Error($"[PlayerPrefsSaveStorage] '{key}' 는 쓸 수 없는 키입니다. 비어 있거나 줄바꿈을 포함할 수 없습니다.", LogColor.Red);
                return false;
            }

            PlayerPrefs.SetString(KeyPrefix + key, data ?? string.Empty);

            EnsureManifest();
            if (_keySet.Add(key))
            {
                _keys.Add(key);
                WriteManifest();
            }

            PlayerPrefs.Save();
            return true;
        }

        public bool TryRead(string key, out string data)
        {
            if (IsValidKey(key) && PlayerPrefs.HasKey(KeyPrefix + key))
            {
                data = PlayerPrefs.GetString(KeyPrefix + key);
                return true;
            }

            data = null;
            return false;
        }

        public bool Delete(string key)
        {
            if (!Exists(key))
            {
                return false;
            }

            PlayerPrefs.DeleteKey(KeyPrefix + key);

            EnsureManifest();
            if (_keySet.Remove(key))
            {
                _keys.Remove(key);
                WriteManifest();
            }

            PlayerPrefs.Save();
            return true;
        }

        public IReadOnlyList<string> GetAllKeys()
        {
            EnsureManifest();

            //== 내부 목록을 그대로 넘기면 호출자가 순회하는 중에 Write/Delete 가 목록을 바꿀 수 있어 복사본을 줍니다.
            return new List<string>(_keys);
        }

        public void DeleteAll()
        {
            EnsureManifest();
            for (int i = 0; i < _keys.Count; i++)
            {
                PlayerPrefs.DeleteKey(KeyPrefix + _keys[i]);
            }

            _keys.Clear();
            _keySet.Clear();
            PlayerPrefs.DeleteKey(ManifestKey);
            PlayerPrefs.Save();
        }

        #endregion

        #region Private Helpers

        private void EnsureManifest()
        {
            if (_keys != null)
            {
                return;
            }

            _keys = new List<string>();
            _keySet = new HashSet<string>();

            string raw = PlayerPrefs.GetString(ManifestKey, string.Empty);
            if (string.IsNullOrEmpty(raw))
            {
                return;
            }

            string[] parts = raw.Split(ManifestSeparator);
            for (int i = 0; i < parts.Length; i++)
            {
                if (!string.IsNullOrEmpty(parts[i]) && _keySet.Add(parts[i]))
                {
                    _keys.Add(parts[i]);
                }
            }
        }

        private void WriteManifest()
        {
            PlayerPrefs.SetString(ManifestKey, string.Join(ManifestSeparator.ToString(), _keys));
        }

        private static bool IsValidKey(string key)
        {
            //== 목록을 줄바꿈으로 구분해 저장하므로 줄바꿈이 든 키는 목록을 깨뜨립니다.
            return !string.IsNullOrEmpty(key) && key.IndexOf(ManifestSeparator) < 0;
        }

        #endregion
    }
}
