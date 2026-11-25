using System;
using System.Collections.Generic;
using UnityEngine;

using Cocoa.Lib.Util;

namespace Cocoa.Lib.Sound
{
    /// <summary>
    /// 채널별 볼륨/음소거, AudioSource 풀링, 페이드를 다루는 사운드 재생 관리자입니다.
    /// </summary>
    /// <remarks>
    /// AudioListener 가 전역 하나뿐이라 보통 Default 하나만 씁니다.
    /// 다른 저장소를 쓰려면 재생이나 상태 조회보다 먼저 Initialize 를 호출해야 합니다. 그 전에 다른 API 를 부르면 PlayerPrefs 저장소로 자동 초기화됩니다.
    /// 최종 볼륨은 재생 볼륨 × 채널 볼륨 × 페이드 배율이며, Master 는 AudioListener.volume 으로 모든 소리에 곱해집니다.
    /// </remarks>
    public class SoundManager : MonoBehaviour
    {
        #region Nested Types

        private sealed class RegisteredClip
        {
            public SoundChannel Channel;
            public AudioClip Clip;
            public float DefaultVolume;
        }

        private sealed class ActiveSound
        {
            public AudioSource Source;
            public SoundChannel Channel;
            public string ClipId;

            //== 재생 요청 볼륨. 채널 볼륨과 페이드 배율은 적용 시점에 곱합니다.
            public float BaseVolume;

            //== 페이드 진행 배율 (0 ~ 1). 채널 볼륨과 분리해 두어야 페이드 중에 채널 볼륨을 바꿔도 서로 덮어쓰지 않습니다.
            public float FadeFactor;

            //== 페이드 아웃이 끝나면 정지할 예정인지 여부.
            public bool IsStopping;

            public ScheduleHandle FadeHandle;

            public void Reset()
            {
                Source = null;
                ClipId = null;
                BaseVolume = 0f;
                FadeFactor = 1f;
                IsStopping = false;
                FadeHandle = ScheduleHandle.None;
            }
        }

        #endregion

        #region Constants

        private const int DefaultMinSourceCount = 5;
        private const int DefaultMaxSourceCount = 32;
        private const int DefaultFadeTickRate = 30;

        //== AudioClip 을 직접 재생할 때 붙는 ID 접두사. 등록 ID 와 겹치지 않게 구분합니다.
        private const string DirectClipIdPrefix = "__direct_";

        #endregion

        #region Static

        private static readonly int _channelCount = Enum.GetValues(typeof(SoundChannel)).Length;

        private static SoundManager _defaultInstance;

        /// <summary>
        /// 라이브러리가 제공하는 공용 인스턴스. 첫 접근 시 DontDestroyOnLoad GameObject 를 만듭니다.
        /// 앱 종료 중에는 새로 만들지 않고 null 을 반환합니다.
        /// </summary>
        public static SoundManager Default
        {
            get
            {
                if (_defaultInstance != null)
                {
                    return _defaultInstance;
                }

                //== 종료 중에 만들면 정리되지 않은 GameObject 가 남습니다.
                if (AppLifecycle.IsQuitting)
                {
                    return null;
                }

                GameObject host = new GameObject("[Cocoa.Lib] SoundManager");
                DontDestroyOnLoad(host);
                _defaultInstance = host.AddComponent<SoundManager>();

                return _defaultInstance;
            }
        }

        #endregion

        #region Fields

        [SerializeField, Min(0)] private int _minSourceCount = DefaultMinSourceCount;
        [SerializeField, Min(1)] private int _maxSourceCount = DefaultMaxSourceCount;
        [SerializeField, Min(1)] private int _fadeTickRate = DefaultFadeTickRate;

        private readonly Dictionary<string, RegisteredClip> _clips = new Dictionary<string, RegisteredClip>();
        private readonly SoundState[] _states = new SoundState[_channelCount];

        //== BGM 은 동시에 하나라 전용 소스를 둡니다.
        private AudioSource _bgmSource;
        private ActiveSound _bgm;

        //== SFX/Voice 는 재생 순서대로 쌓아 두어 소스가 모자랄 때 가장 오래된 것부터 회수합니다.
        private readonly List<ActiveSound> _activeSounds = new List<ActiveSound>();
        private readonly Stack<AudioSource> _idleSources = new Stack<AudioSource>();
        private readonly Stack<ActiveSound> _soundPool = new Stack<ActiveSound>();
        private int _createdSourceCount;

        private ISoundSettingsStorage _settingsStorage;
        private bool _isInitialized;

        #endregion

        #region Events

        /// <summary>채널 상태가 실제로 바뀌었을 때 발생합니다. (channel, newState)</summary>
        public event Action<SoundChannel, SoundState> StateChanged;

        /// <summary>재생이 시작되었을 때 발생합니다. (clipId, channel)</summary>
        public event Action<string, SoundChannel> SoundStarted;

        /// <summary>재생이 끝났거나 정지되었을 때 발생합니다. (clipId, channel)</summary>
        public event Action<string, SoundChannel> SoundStopped;

        #endregion

        #region Properties

        /// <summary>초기화 완료 여부.</summary>
        public bool IsInitialized
        {
            get { return _isInitialized; }
        }

        /// <summary>재생 중인 BGM 의 ID. 없거나 페이드 아웃 중이면 null.</summary>
        public string CurrentBgmId
        {
            get { return _bgm != null && !_bgm.IsStopping ? _bgm.ClipId : null; }
        }

        /// <summary>등록된 클립 수.</summary>
        public int RegisteredClipCount
        {
            get { return _clips.Count; }
        }

        /// <summary>재생 중인 사운드 수 (BGM 포함).</summary>
        public int ActiveSoundCount
        {
            get { return _activeSounds.Count + (_bgm != null ? 1 : 0); }
        }

        #endregion

        #region Initialization

        /// <summary>
        /// 설정 저장소를 지정해 초기화합니다. null 이면 PlayerPrefsSoundSettings 를 씁니다.
        /// 다른 API 보다 먼저 호출해야 하며, 이미 초기화되었으면 무시됩니다.
        /// </summary>
        public void Initialize(ISoundSettingsStorage settingsStorage)
        {
            if (_isInitialized)
            {
                Log.Warning("[SoundManager] 이미 초기화되어 Initialize 를 무시합니다. 다른 API 보다 먼저 호출하세요.", LogColor.Yellow);
                return;
            }

            _settingsStorage = settingsStorage ?? new PlayerPrefsSoundSettings();

            GameObject bgmHost = new GameObject("BGM Source");
            bgmHost.transform.SetParent(transform, false);
            _bgmSource = bgmHost.AddComponent<AudioSource>();
            _bgmSource.playOnAwake = false;

            for (int i = 0; i < _minSourceCount; i++)
            {
                _idleSources.Push(CreatePooledSource());
            }

            for (int i = 0; i < _channelCount; i++)
            {
                SoundState state;
                _states[i] = _settingsStorage.TryLoad((SoundChannel)i, out state) ? state : SoundState.Default;
            }

            ApplyMaster();

            //== 모바일은 백그라운드에서 그대로 종료될 수 있어 종료 신호만으로는 저장을 놓칩니다.
            AppLifecycle.PauseChanged += OnAppPauseChanged;
            AppLifecycle.Quitting += FlushSettings;

            _isInitialized = true;
        }

        #endregion

        #region Public API - Registration

        /// <summary>SoundDefinition 을 등록합니다. ID 가 이미 있으면 false 를 반환합니다.</summary>
        public bool RegisterClip(SoundDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            return RegisterClip(definition.Id, definition.Channel, definition.Clip, definition.DefaultVolume);
        }

        /// <summary>SoundDefinition 목록을 등록하고 실제로 등록된 수를 반환합니다. null 항목은 건너뜁니다.</summary>
        public int RegisterClips(IEnumerable<SoundDefinition> definitions)
        {
            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            int count = 0;
            foreach (SoundDefinition definition in definitions)
            {
                if (definition != null && RegisterClip(definition))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>클립을 ID 로 등록합니다. ID 가 이미 있으면 false 를 반환합니다.</summary>
        public bool RegisterClip(string id, SoundChannel channel, AudioClip clip, float defaultVolume = 1f)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("id 가 비어 있습니다.", nameof(id));
            }

            if (channel == SoundChannel.Master)
            {
                throw new ArgumentException("Master 채널에는 클립을 등록할 수 없습니다.", nameof(channel));
            }

            if (_clips.ContainsKey(id))
            {
                Log.Warning($"[SoundManager] '{id}' 는 이미 등록되어 있어 건너뜁니다.", LogColor.Yellow);
                return false;
            }

            if (clip == null)
            {
                Log.Warning($"[SoundManager] '{id}' 를 AudioClip 없이 등록합니다. 재생 요청은 무시됩니다.", LogColor.Yellow);
            }

            _clips.Add(id, new RegisteredClip
            {
                Channel = channel,
                Clip = clip,
                DefaultVolume = Mathf.Clamp01(defaultVolume)
            });

            return true;
        }

        /// <summary>등록을 해제합니다. 같은 ID 로 재생 중인 사운드는 즉시 정지합니다.</summary>
        public bool UnregisterClip(string id)
        {
            if (string.IsNullOrEmpty(id) || !_clips.Remove(id))
            {
                return false;
            }

            if (_isInitialized)
            {
                StopClip(id);
            }

            return true;
        }

        /// <summary>모든 사운드를 정지하고 등록을 모두 해제합니다.</summary>
        public void UnregisterAll()
        {
            if (_isInitialized)
            {
                StopAll();
            }

            _clips.Clear();
        }

        /// <summary>해당 ID 가 등록되어 있는지 확인합니다.</summary>
        public bool IsClipRegistered(string id)
        {
            return !string.IsNullOrEmpty(id) && _clips.ContainsKey(id);
        }

        #endregion

        #region Public API - State

        /// <summary>채널 상태를 바꾸고 저장소에 기록합니다. 값이 같으면 아무것도 하지 않습니다.</summary>
        public void SetState(SoundChannel channel, SoundState state)
        {
            EnsureInitialized();

            int index = (int)channel;
            if (_states[index] == state)
            {
                return;
            }

            _states[index] = state;

            if (channel == SoundChannel.Master)
            {
                ApplyMaster();
            }
            else
            {
                ApplyChannel(channel);
            }

            _settingsStorage.Save(channel, state);

            if (StateChanged != null)
            {
                try
                {
                    StateChanged(channel, state);
                }
                catch (Exception exception)
                {
                    Log.Error($"[SoundManager] StateChanged 핸들러에서 예외가 발생했습니다.\n{exception}", LogColor.Red);
                }
            }
        }

        /// <summary>채널 볼륨만 바꿉니다.</summary>
        public void SetVolume(SoundChannel channel, float volume)
        {
            SetState(channel, GetState(channel).WithVolume(volume));
        }

        /// <summary>채널 음소거만 바꿉니다.</summary>
        public void SetMute(SoundChannel channel, bool isMuted)
        {
            SetState(channel, GetState(channel).WithMute(isMuted));
        }

        /// <summary>채널 상태를 조회합니다.</summary>
        public SoundState GetState(SoundChannel channel)
        {
            EnsureInitialized();
            return _states[(int)channel];
        }

        #endregion

        #region Public API - Play

        /// <summary>등록된 ID 로 재생합니다.</summary>
        /// <param name="id">등록된 클립 ID.</param>
        /// <param name="volume">재생 볼륨 (0 ~ 1). 음수면 등록 시의 기본 볼륨을 씁니다.</param>
        /// <param name="loop">반복 재생 여부.</param>
        /// <param name="fadeIn">페이드 인 시간(초). 0 이면 바로 목표 볼륨으로 시작합니다.</param>
        public void Play(string id, float volume = -1f, bool loop = false, float fadeIn = 0f)
        {
            EnsureInitialized();

            RegisteredClip registered;
            if (string.IsNullOrEmpty(id) || !_clips.TryGetValue(id, out registered))
            {
                Log.Warning($"[SoundManager] '{id}' 는 등록되지 않은 ID 입니다.", LogColor.Yellow);
                return;
            }

            if (registered.Clip == null)
            {
                Log.Warning($"[SoundManager] '{id}' 에 AudioClip 이 없습니다.", LogColor.Yellow);
                return;
            }

            float resolvedVolume = volume < 0f ? registered.DefaultVolume : volume;
            PlayInternal(id, registered.Channel, registered.Clip, resolvedVolume, loop, fadeIn);
        }

        /// <summary>
        /// 등록 없이 AudioClip 을 바로 재생합니다.
        /// 이벤트와 StopClip 에서 쓰는 ID 는 "__direct_" 뒤에 클립 이름을 붙인 값입니다.
        /// </summary>
        public void Play(AudioClip clip, SoundChannel channel, float volume = 1f, bool loop = false, float fadeIn = 0f)
        {
            EnsureInitialized();

            if (clip == null)
            {
                throw new ArgumentNullException(nameof(clip));
            }

            if (channel == SoundChannel.Master)
            {
                throw new ArgumentException("Master 채널로는 재생할 수 없습니다.", nameof(channel));
            }

            PlayInternal(DirectClipIdPrefix + clip.name, channel, clip, volume, loop, fadeIn);
        }

        #endregion

        #region Public API - Stop

        /// <summary>해당 ID 로 재생 중인 사운드를 모두 정지합니다.</summary>
        /// <param name="fadeOut">페이드 아웃 시간(초). 0 이면 즉시 정지합니다.</param>
        public void StopClip(string id, float fadeOut = 0f)
        {
            EnsureInitialized();

            if (_bgm != null && _bgm.ClipId == id)
            {
                Stop(_bgm, fadeOut);
            }

            for (int i = _activeSounds.Count - 1; i >= 0; i--)
            {
                //== 정지 이벤트 핸들러가 목록을 바꿀 수 있으므로 인덱스를 다시 확인합니다.
                if (i < _activeSounds.Count && _activeSounds[i].ClipId == id)
                {
                    Stop(_activeSounds[i], fadeOut);
                }
            }
        }

        /// <summary>BGM 을 정지합니다.</summary>
        public void StopBgm(float fadeOut = 0f)
        {
            EnsureInitialized();

            if (_bgm != null)
            {
                Stop(_bgm, fadeOut);
            }
        }

        /// <summary>해당 채널의 사운드를 모두 정지합니다. Master 를 넘기면 모든 채널을 정지합니다.</summary>
        public void StopChannel(SoundChannel channel, float fadeOut = 0f)
        {
            if (channel == SoundChannel.Master)
            {
                StopAll(fadeOut);
                return;
            }

            if (channel == SoundChannel.BGM)
            {
                StopBgm(fadeOut);
                return;
            }

            EnsureInitialized();

            for (int i = _activeSounds.Count - 1; i >= 0; i--)
            {
                if (i < _activeSounds.Count && _activeSounds[i].Channel == channel)
                {
                    Stop(_activeSounds[i], fadeOut);
                }
            }
        }

        /// <summary>모든 사운드를 정지합니다.</summary>
        public void StopAll(float fadeOut = 0f)
        {
            EnsureInitialized();

            if (_bgm != null)
            {
                Stop(_bgm, fadeOut);
            }

            for (int i = _activeSounds.Count - 1; i >= 0; i--)
            {
                if (i < _activeSounds.Count)
                {
                    Stop(_activeSounds[i], fadeOut);
                }
            }
        }

        #endregion

        #region Public API - Pause

        /// <summary>AudioListener 단위로 모든 소리를 일시정지합니다. 이 매니저 밖의 AudioSource 도 함께 멈춥니다.</summary>
        public void PauseAll()
        {
            AudioListener.pause = true;
        }

        /// <summary>PauseAll 로 멈춘 소리를 다시 재생합니다.</summary>
        public void ResumeAll()
        {
            AudioListener.pause = false;
        }

        #endregion

        #region Unity Messages

        private void Update()
        {
            //== 일시정지 중에는 isPlaying 으로 재생 종료를 판단할 수 없으므로 정리를 미룹니다.
            if (!_isInitialized || AudioListener.pause)
            {
                return;
            }

            if (_bgm != null && !_bgm.Source.isPlaying && !_bgm.FadeHandle.IsValid)
            {
                Finish(_bgm);
            }

            for (int i = _activeSounds.Count - 1; i >= 0; i--)
            {
                if (i >= _activeSounds.Count)
                {
                    continue;
                }

                ActiveSound sound = _activeSounds[i];
                if (!sound.Source.isPlaying && !sound.FadeHandle.IsValid)
                {
                    Finish(sound);
                }
            }
        }

        private void OnDestroy()
        {
            if (_isInitialized)
            {
                AppLifecycle.PauseChanged -= OnAppPauseChanged;
                AppLifecycle.Quitting -= FlushSettings;
                FlushSettings();
            }

            if (_defaultInstance == this)
            {
                _defaultInstance = null;
            }
        }

        #endregion

        #region Private Helpers - Playback

        private void PlayInternal(string id, SoundChannel channel, AudioClip clip, float volume, bool loop, float fadeIn)
        {
            if (channel == SoundChannel.BGM)
            {
                PlayBgm(id, clip, volume, loop, fadeIn);
            }
            else
            {
                PlayPooled(id, channel, clip, volume, loop, fadeIn);
            }
        }

        private void PlayBgm(string id, AudioClip clip, float volume, bool loop, float fadeIn)
        {
            //== 같은 곡을 다시 요청하면 처음부터 다시 틀지 않고 볼륨과 반복 설정만 갱신합니다. 씬마다 같은 BGM 을 요청하는 흐름에서 곡이 끊기지 않게 하기 위해서입니다.
            if (_bgm != null && !_bgm.IsStopping && _bgm.ClipId == id && _bgmSource.clip == clip)
            {
                _bgm.BaseVolume = Mathf.Clamp01(volume);
                _bgmSource.loop = loop;
                ApplyVolume(_bgm);
                return;
            }

            if (_bgm != null)
            {
                Finish(_bgm);
            }

            _bgm = RentSound(_bgmSource, SoundChannel.BGM, id, volume);
            StartPlayback(_bgm, clip, loop, fadeIn);
        }

        private void PlayPooled(string id, SoundChannel channel, AudioClip clip, float volume, bool loop, float fadeIn)
        {
            AudioSource source = AcquireSource();
            if (source == null)
            {
                return;
            }

            ActiveSound sound = RentSound(source, channel, id, volume);
            _activeSounds.Add(sound);
            StartPlayback(sound, clip, loop, fadeIn);
        }

        private void StartPlayback(ActiveSound sound, AudioClip clip, bool loop, float fadeIn)
        {
            AudioSource source = sound.Source;
            source.clip = clip;
            source.loop = loop;

            sound.FadeFactor = fadeIn > 0f ? 0f : 1f;
            ApplyVolume(sound);
            source.Play();

            RaiseSoundStarted(sound.ClipId, sound.Channel);

            if (fadeIn > 0f)
            {
                StartFade(sound, 1f, fadeIn, false);
            }
        }

        private void Stop(ActiveSound sound, float fadeOut)
        {
            if (fadeOut > 0f)
            {
                //== 이미 페이드 아웃 중이면 진행 중인 것을 유지해 정지 시점이 뒤로 밀리지 않게 합니다.
                if (!sound.IsStopping)
                {
                    sound.IsStopping = true;
                    StartFade(sound, 0f, fadeOut, true);
                }

                return;
            }

            Finish(sound);
        }

        //== 재생을 끝내고 정지 이벤트를 보낸 뒤 소스를 반납합니다. 정지 경로는 모두 여기로 모입니다.
        private void Finish(ActiveSound sound)
        {
            CancelFade(sound);
            sound.Source.Stop();
            sound.Source.clip = null;

            string clipId = sound.ClipId;
            SoundChannel channel = sound.Channel;

            if (sound == _bgm)
            {
                _bgm = null;
            }
            else
            {
                _activeSounds.Remove(sound);
                _idleSources.Push(sound.Source);
            }

            sound.Reset();
            _soundPool.Push(sound);

            RaiseSoundStopped(clipId, channel);
        }

        private AudioSource AcquireSource()
        {
            if (_idleSources.Count > 0)
            {
                return _idleSources.Pop();
            }

            if (_createdSourceCount < _maxSourceCount)
            {
                return CreatePooledSource();
            }

            if (_activeSounds.Count == 0)
            {
                return null;
            }

            //== 상한에 닿으면 가장 오래된 단발성 소리를 끊고 자리를 넘깁니다. 반복 재생은 끊으면 다시 시작되지 않으므로 단발성이 없을 때만 회수합니다.
            int victim = 0;
            for (int i = 0; i < _activeSounds.Count; i++)
            {
                if (!_activeSounds[i].Source.loop)
                {
                    victim = i;
                    break;
                }
            }

            Finish(_activeSounds[victim]);
            return _idleSources.Pop();
        }

        private AudioSource CreatePooledSource()
        {
            _createdSourceCount++;

            GameObject host = new GameObject($"Sound Source {_createdSourceCount}");
            host.transform.SetParent(transform, false);

            AudioSource source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            return source;
        }

        private ActiveSound RentSound(AudioSource source, SoundChannel channel, string id, float volume)
        {
            ActiveSound sound = _soundPool.Count > 0 ? _soundPool.Pop() : new ActiveSound();
            sound.Reset();
            sound.Source = source;
            sound.Channel = channel;
            sound.ClipId = id;
            sound.BaseVolume = Mathf.Clamp01(volume);
            return sound;
        }

        #endregion

        #region Private Helpers - Volume

        private void ApplyVolume(ActiveSound sound)
        {
            SoundState state = _states[(int)sound.Channel];
            sound.Source.mute = state.IsMuted;
            sound.Source.volume = sound.BaseVolume * state.Volume * sound.FadeFactor;
        }

        private void ApplyChannel(SoundChannel channel)
        {
            if (_bgm != null && _bgm.Channel == channel)
            {
                ApplyVolume(_bgm);
            }

            for (int i = 0; i < _activeSounds.Count; i++)
            {
                if (_activeSounds[i].Channel == channel)
                {
                    ApplyVolume(_activeSounds[i]);
                }
            }
        }

        private void ApplyMaster()
        {
            //== Master 음소거를 각 소스의 mute 로 풀면 채널 음소거와 엉키므로 AudioListener 볼륨 하나로 처리합니다.
            SoundState master = _states[(int)SoundChannel.Master];
            AudioListener.volume = master.IsMuted ? 0f : master.Volume;
        }

        #endregion

        #region Private Helpers - Fade

        private void StartFade(ActiveSound sound, float to, float duration, bool finishOnComplete)
        {
            CancelFade(sound);

            float from = sound.FadeFactor;
            Scheduler scheduler = Scheduler.Default;
            if (scheduler == null)
            {
                //== 종료 중이라 스케줄러가 없으면 페이드를 생략하고 결과만 적용합니다.
                CompleteFade(sound, to, finishOnComplete);
                return;
            }

            int count = Mathf.Max(2, Mathf.CeilToInt(duration * _fadeTickRate));

            //== 일시정지 메뉴처럼 timeScale 을 0 으로 둔 상태에서도 페이드가 멈추지 않도록 실제 시간으로 진행합니다.
            sound.FadeHandle = scheduler.RegisterOverDurationFor(
                this,
                duration,
                count,
                info =>
                {
                    sound.FadeFactor = Mathf.Lerp(from, to, (info.IterationIndex + 1f) / count);
                    ApplyVolume(sound);
                },
                () => CompleteFade(sound, to, finishOnComplete),
                true);
        }

        private void CompleteFade(ActiveSound sound, float to, bool finishOnComplete)
        {
            sound.FadeHandle = ScheduleHandle.None;
            sound.FadeFactor = to;
            ApplyVolume(sound);

            if (finishOnComplete)
            {
                Finish(sound);
            }
        }

        private static void CancelFade(ActiveSound sound)
        {
            //== 빈 핸들이나 이미 끝난 핸들에 호출해도 안전합니다.
            sound.FadeHandle.Unregister();
            sound.FadeHandle = ScheduleHandle.None;
        }

        #endregion

        #region Private Helpers - Misc

        private void EnsureInitialized()
        {
            if (!_isInitialized)
            {
                Initialize(null);
            }
        }

        private void OnAppPauseChanged(bool isPaused)
        {
            if (isPaused)
            {
                FlushSettings();
            }
        }

        private void FlushSettings()
        {
            if (_settingsStorage == null)
            {
                return;
            }

            try
            {
                _settingsStorage.Flush();
            }
            catch (Exception exception)
            {
                Log.Error($"[SoundManager] 설정 저장 중 예외가 발생했습니다.\n{exception}", LogColor.Red);
            }
        }

        private void RaiseSoundStarted(string clipId, SoundChannel channel)
        {
            if (SoundStarted == null)
            {
                return;
            }

            try
            {
                SoundStarted(clipId, channel);
            }
            catch (Exception exception)
            {
                Log.Error($"[SoundManager] SoundStarted 핸들러에서 예외가 발생했습니다.\n{exception}", LogColor.Red);
            }
        }

        private void RaiseSoundStopped(string clipId, SoundChannel channel)
        {
            if (SoundStopped == null)
            {
                return;
            }

            try
            {
                SoundStopped(clipId, channel);
            }
            catch (Exception exception)
            {
                Log.Error($"[SoundManager] SoundStopped 핸들러에서 예외가 발생했습니다.\n{exception}", LogColor.Red);
            }
        }

        #endregion
    }
}
