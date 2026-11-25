using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cocoa.Lib.Sound
{
    /// <summary>
    /// SoundDefinition 을 묶어 두는 에셋입니다. 씬이나 콘텐츠 단위로 나눠 두고 필요한 시점에 한 번에 등록합니다.
    /// </summary>
    /// <remarks>
    /// 목록 편집은 에디터의 SoundLibraryWindow 가 SerializedObject 로 처리합니다.
    /// 런타임 클래스에 편집용 메서드를 두지 않아 Undo 와 변경 표시가 에디터 기본 흐름을 그대로 따릅니다.
    /// </remarks>
    [CreateAssetMenu(fileName = "NewSoundLibrary", menuName = "Cocoa/Lib/Sound/Sound Library")]
    public sealed class SoundLibrary : ScriptableObject
    {
        #region Fields

        [SerializeField] private List<SoundDefinition> _definitions = new List<SoundDefinition>();

        #endregion

        #region Properties

        /// <summary>담긴 SoundDefinition 목록. 비어 있는 항목이 섞여 있을 수 있습니다.</summary>
        public IReadOnlyList<SoundDefinition> Definitions
        {
            get { return _definitions; }
        }

        /// <summary>항목 수. 비어 있는 항목도 셉니다.</summary>
        public int Count
        {
            get { return _definitions.Count; }
        }

        #endregion

        #region Public API

        /// <summary>ID 가 같은 첫 항목을 찾습니다. 없으면 null.</summary>
        public SoundDefinition Find(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            for (int i = 0; i < _definitions.Count; i++)
            {
                SoundDefinition definition = _definitions[i];
                if (definition != null && string.Equals(definition.Id, id, StringComparison.Ordinal))
                {
                    return definition;
                }
            }

            return null;
        }

        /// <summary>해당 채널의 항목을 result 에 채우고 채운 수를 반환합니다. result 는 먼저 비웁니다.</summary>
        /// <remarks>호출하는 쪽이 리스트를 재사용할 수 있도록 결과를 새로 할당하지 않습니다.</remarks>
        public int FindByChannel(SoundChannel channel, List<SoundDefinition> result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            result.Clear();
            for (int i = 0; i < _definitions.Count; i++)
            {
                SoundDefinition definition = _definitions[i];
                if (definition != null && definition.Channel == channel)
                {
                    result.Add(definition);
                }
            }

            return result.Count;
        }

        /// <summary>
        /// 모든 항목을 SoundManager 에 등록하고 실제로 등록된 수를 반환합니다.
        /// </summary>
        /// <param name="soundManager">대상 SoundManager. null 이면 SoundManager.Default 를 씁니다.</param>
        public int RegisterAllTo(SoundManager soundManager = null)
        {
            SoundManager target = soundManager != null ? soundManager : SoundManager.Default;
            if (target == null)
            {
                //== 앱 종료 중이면 Default 가 null 이라 등록할 곳이 없습니다.
                return 0;
            }

            return target.RegisterClips(_definitions);
        }

        /// <summary>모든 항목의 등록을 해제하고 해제된 수를 반환합니다. 씬을 떠날 때 RegisterAllTo 와 짝으로 씁니다.</summary>
        public int UnregisterAllFrom(SoundManager soundManager = null)
        {
            SoundManager target = soundManager != null ? soundManager : SoundManager.Default;
            if (target == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < _definitions.Count; i++)
            {
                SoundDefinition definition = _definitions[i];
                if (definition != null && target.UnregisterClip(definition.Id))
                {
                    count++;
                }
            }

            return count;
        }

        #endregion
    }
}
