using UnityEngine;

namespace MazeChase.Audio
{
    /// <summary>
    /// Manages all game audio. Creates AudioSources and procedural clips on init.
    /// Provides simple Play methods for game events.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        private AudioSource _sfxSource;
        private AudioSource _sirenSource;
        private AudioSource _frightenedSource;

        // Clips
        private AudioClip _pelletEatHigh;
        private AudioClip _pelletEatLow;
        private AudioClip _energizerEat;
        private AudioClip _ghostEat;
        private AudioClip _death;
        private AudioClip _roundClear;
        private AudioClip _fruitCollect;
        private AudioClip _siren;
        private AudioClip _frightenedLoop;
        private AudioClip _uiClick;
        private AudioClip _gameStart;

        private bool _pelletToggle;
        private float _masterVolume = 1f;
        private float _sfxVolume = 0.7f;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Create audio sources
            _sfxSource = gameObject.AddComponent<AudioSource>();
            _sfxSource.playOnAwake = false;

            _sirenSource = gameObject.AddComponent<AudioSource>();
            _sirenSource.playOnAwake = false;
            _sirenSource.loop = true;
            _sirenSource.volume = 0.15f;

            _frightenedSource = gameObject.AddComponent<AudioSource>();
            _frightenedSource.playOnAwake = false;
            _frightenedSource.loop = true;
            _frightenedSource.volume = 0.12f;

            // Generate all clips
            GenerateClips();

            Debug.Log("[AudioManager] Initialized with procedural audio clips.");
        }

        private void GenerateClips()
        {
            _pelletEatHigh = ProceduralAudio.CreatePelletEat(1.2f);
            _pelletEatLow = ProceduralAudio.CreatePelletEat(1.0f);
            _energizerEat = ProceduralAudio.CreateEnergizerEat();
            _ghostEat = ProceduralAudio.CreateGhostEat();
            _death = ProceduralAudio.CreateDeath();
            _roundClear = ProceduralAudio.CreateRoundClear();
            _fruitCollect = ProceduralAudio.CreateFruitCollect();
            _siren = ProceduralAudio.CreateSiren();
            _frightenedLoop = ProceduralAudio.CreateFrightenedLoop();
            _uiClick = ProceduralAudio.CreateUIClick();
            _gameStart = ProceduralAudio.CreateGameStart();
        }

        public void PlayPelletEat()
        {
            _pelletToggle = !_pelletToggle;
            _sfxSource.PlayOneShot(_pelletToggle ? _pelletEatHigh : _pelletEatLow, _sfxVolume);
        }

        public void PlayEnergizerEat()
        {
            _sfxSource.PlayOneShot(_energizerEat, _sfxVolume);
        }

        public void PlayGhostEat()
        {
            _sfxSource.PlayOneShot(_ghostEat, _sfxVolume);
        }

        public void PlayDeath()
        {
            StopSiren();
            StopFrightened();
            _sfxSource.PlayOneShot(_death, _sfxVolume);
        }

        public void PlayRoundClear()
        {
            StopSiren();
            StopFrightened();
            _sfxSource.PlayOneShot(_roundClear, _sfxVolume);
        }

        public void PlayFruitCollect()
        {
            _sfxSource.PlayOneShot(_fruitCollect, _sfxVolume);
        }

        public void PlayUIClick()
        {
            _sfxSource.PlayOneShot(_uiClick, _sfxVolume * 0.5f);
        }

        public void PlayGameStart()
        {
            _sfxSource.PlayOneShot(_gameStart, _sfxVolume);
        }

        public void StartSiren()
        {
            if (_sirenSource.isPlaying) return;
            _sirenSource.clip = _siren;
            _sirenSource.Play();
        }

        public void StopSiren()
        {
            _sirenSource.Stop();
        }

        public void StartFrightened()
        {
            StopSiren();
            if (_frightenedSource.isPlaying) return;
            _frightenedSource.clip = _frightenedLoop;
            _frightenedSource.Play();
        }

        public void StopFrightened()
        {
            _frightenedSource.Stop();
        }

        /// <summary>Transition from frightened back to siren.</summary>
        public void ResumeNormalAudio()
        {
            StopFrightened();
            StartSiren();
        }

        public void StopAll()
        {
            _sfxSource.Stop();
            _sirenSource.Stop();
            _frightenedSource.Stop();
        }

        public void SetMasterVolume(float vol)
        {
            _masterVolume = Mathf.Clamp01(vol);
            AudioListener.volume = _masterVolume;
        }

        public void SetSFXVolume(float vol)
        {
            _sfxVolume = Mathf.Clamp01(vol);
        }
    }
}
