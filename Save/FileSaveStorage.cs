using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

using Cocoa.Lib.Util;

namespace Cocoa.Lib.Save
{
    /// <summary>
    /// Application.persistentDataPath 아래에 키마다 파일 하나로 저장하는 저장소입니다. 진행도 같은 본 세이브에 씁니다.
    /// </summary>
    /// <remarks>
    /// 모바일에서는 쓰는 도중 앱이 종료될 수 있어 다음 순서로 기록합니다.
    /// 임시 파일(.tmp)에 끝까지 쓰고 디스크까지 내린 뒤, 기존 파일을 백업(.bak)으로 옮기고, 임시 파일을 본 파일 이름으로 바꿉니다.
    /// 이름 바꾸기는 같은 볼륨 안에서 원자적이라 본 파일이 반쯤 쓰인 상태로 남지 않습니다.
    /// 키는 파일 이름이 되므로 문자, 숫자, '_', '-', '.' 만 허용합니다.
    /// </remarks>
    public sealed class FileSaveStorage : ISaveStorage, ISaveBackupStorage
    {
        #region Constants

        private const string TempSuffix = ".tmp";
        private const string BackupSuffix = ".bak";

        #endregion

        #region Fields

        private readonly string _directoryPath;
        private readonly string _extension;

        #endregion

        #region Properties

        /// <summary>세이브 파일이 저장되는 폴더의 절대 경로.</summary>
        public string DirectoryPath
        {
            get { return _directoryPath; }
        }

        #endregion

        #region Constructors

        /// <param name="subFolder">persistentDataPath 아래 폴더 이름.</param>
        /// <param name="extension">세이브 파일 확장자. 점으로 시작해야 합니다.</param>
        public FileSaveStorage(string subFolder = "saves", string extension = ".sav")
        {
            _directoryPath = Path.Combine(Application.persistentDataPath, subFolder);
            _extension = extension;
        }

        #endregion

        #region Public API

        public bool Exists(string key)
        {
            return IsValidKey(key) && File.Exists(GetPath(key));
        }

        public bool Write(string key, string data)
        {
            if (!ValidateKey(key))
            {
                return false;
            }

            try
            {
                Directory.CreateDirectory(_directoryPath);

                string mainPath = GetPath(key);
                string tempPath = mainPath + TempSuffix;
                string backupPath = mainPath + BackupSuffix;

                WriteFlushed(tempPath, data);

                if (File.Exists(mainPath))
                {
                    if (File.Exists(backupPath))
                    {
                        File.Delete(backupPath);
                    }

                    File.Move(mainPath, backupPath);
                }

                File.Move(tempPath, mainPath);
                return true;
            }
            catch (Exception exception)
            {
                Log.Error($"[FileSaveStorage] '{key}' 쓰기 실패.\n{exception}", LogColor.Red);
                return false;
            }
        }

        public bool TryRead(string key, out string data)
        {
            data = null;
            if (!IsValidKey(key))
            {
                return false;
            }

            string mainPath = GetPath(key);
            if (TryReadFile(mainPath, out data))
            {
                return true;
            }

            //== 본 파일이 없거나 읽히지 않으면 백업으로 넘어갑니다. 기존 파일을 백업으로 옮긴 직후에 종료되면 본 파일이 없는 상태가 됩니다.
            if (TryReadFile(mainPath + BackupSuffix, out data))
            {
                Log.Warning($"[FileSaveStorage] '{key}' 본 파일을 읽지 못해 백업에서 읽었습니다.", LogColor.Yellow);
                return true;
            }

            return false;
        }

        public bool TryReadBackup(string key, out string data)
        {
            data = null;
            return IsValidKey(key) && TryReadFile(GetPath(key) + BackupSuffix, out data);
        }

        public bool Delete(string key)
        {
            if (!IsValidKey(key))
            {
                return false;
            }

            try
            {
                string mainPath = GetPath(key);
                bool deleted = File.Exists(mainPath);

                //== 백업과 임시 파일을 남기면 같은 키로 다시 읽을 때 지운 데이터가 백업에서 되살아납니다.
                DeleteIfExists(mainPath);
                DeleteIfExists(mainPath + BackupSuffix);
                DeleteIfExists(mainPath + TempSuffix);
                return deleted;
            }
            catch (Exception exception)
            {
                Log.Error($"[FileSaveStorage] '{key}' 삭제 실패.\n{exception}", LogColor.Red);
                return false;
            }
        }

        public IReadOnlyList<string> GetAllKeys()
        {
            List<string> keys = new List<string>();

            try
            {
                if (!Directory.Exists(_directoryPath))
                {
                    return keys;
                }

                string[] files = Directory.GetFiles(_directoryPath, "*" + _extension);
                for (int i = 0; i < files.Length; i++)
                {
                    //== Windows 의 검색 패턴은 세 글자 확장자를 접두어로 비교해 .sav 로 .save 까지 잡으므로 끝을 다시 확인합니다.
                    if (files[i].EndsWith(_extension, StringComparison.OrdinalIgnoreCase))
                    {
                        keys.Add(Path.GetFileNameWithoutExtension(files[i]));
                    }
                }
            }
            catch (Exception exception)
            {
                Log.Error($"[FileSaveStorage] 키 목록 조회 실패.\n{exception}", LogColor.Red);
            }

            return keys;
        }

        public void DeleteAll()
        {
            try
            {
                if (Directory.Exists(_directoryPath))
                {
                    Directory.Delete(_directoryPath, true);
                }
            }
            catch (Exception exception)
            {
                Log.Error($"[FileSaveStorage] 전체 삭제 실패.\n{exception}", LogColor.Red);
            }
        }

        #endregion

        #region Private Helpers

        private string GetPath(string key)
        {
            return Path.Combine(_directoryPath, key + _extension);
        }

        private static void WriteFlushed(string path, string data)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(data ?? string.Empty);
            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);

                //== OS 버퍼에 머문 채로 이름을 바꾸면, 직후 전원이 꺼질 때 내용이 빈 본 파일이 남을 수 있어 디스크까지 내립니다.
                stream.Flush(true);
            }
        }

        private static bool TryReadFile(string path, out string data)
        {
            data = null;

            try
            {
                if (!File.Exists(path))
                {
                    return false;
                }

                data = File.ReadAllText(path, Encoding.UTF8);
                return true;
            }
            catch (Exception exception)
            {
                Log.Warning($"[FileSaveStorage] '{path}' 읽기 실패.\n{exception}", LogColor.Yellow);
                return false;
            }
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static bool ValidateKey(string key)
        {
            if (IsValidKey(key))
            {
                return true;
            }

            Log.Error($"[FileSaveStorage] '{key}' 는 쓸 수 없는 키입니다. 문자, 숫자, '_', '-', '.' 만 허용합니다.", LogColor.Red);
            return false;
        }

        private static bool IsValidKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            //== '.' 이나 '..' 만으로 된 키는 경로 탐색이 되므로 막습니다.
            if (key.Trim('.').Length == 0)
            {
                return false;
            }

            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '-' && c != '.')
                {
                    return false;
                }
            }

            return true;
        }

        #endregion
    }
}
