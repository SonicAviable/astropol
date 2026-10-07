using System;
using UnityEngine;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Core
{
    public class TimeManager : MonoBehaviour
    {
        public static TimeManager Instance { get; private set; }
        public event Action<int, int, int> OnDayPassed;

        /// <summary>Реальных секунд на игровой день при скорости ×1 (партия 2200–2235 ≈ 3,5 ч на ×1, ≈ 53 мин на ×4).</summary>
        public const float DefaultSecondsPerDay = GamePace.SecondsPerDay;

        [SerializeField] private float secondsPerDay = DefaultSecondsPerDay;

        public float SecondsPerDay => secondsPerDay;

        private float _timer;
        private int _currentSpeed = 1;
        private int _day = 1, _month = 1, _year = 2200;

        public int CurrentSpeed => _currentSpeed;
        public int Day => _day;
        public int Month => _month;
        public int Year => _year;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
            // Темп задаётся в GamePace; старое значение, сохранённое в сцене, не должно его перебивать
            secondsPerDay = DefaultSecondsPerDay;
        }

        private void Update()
        {
            HandleInput();
            if (_currentSpeed == 0) return;

            float interval = secondsPerDay / _currentSpeed;
            _timer += Time.deltaTime;
            if (_timer >= interval)
            {
                _timer -= interval;
                TickDay();
            }
        }

        private void HandleInput()
        {
            if (!StellarisClone.Rendering.UIManager.IsGameStarted) return;   // меню, выбор цивилизации
            var flow = StellarisClone.Rendering.GameFlowUI.Instance;
            if (flow != null && flow.BlocksTimeHotkeys) return;

            if (Input.GetKeyDown(KeyCode.Space)) SetSpeedByPlayer(_currentSpeed == 0 ? 1 : 0);
            if (Input.GetKeyDown(KeyCode.Alpha1)) SetSpeedByPlayer(1);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SetSpeedByPlayer(2);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SetSpeedByPlayer(4);
        }

        /// <summary>Смена скорости игроком (клавиши, кнопки) — со звуком паузы/запуска/ускорения.</summary>
        public void SetSpeedByPlayer(int speed)
        {
            int s = Mathf.Clamp(speed, 0, 4);
            if (s != _currentSpeed)
            {
                if (s == 0) SFXManager.Play(Sfx.TimePause);
                else if (_currentSpeed == 0) SFXManager.Play(Sfx.TimeResume);
                else SFXManager.Play(Sfx.TimeSpeed, 1f, s > _currentSpeed ? 1.12f : 0.88f);
            }
            SetSpeed(s);
        }

        public void SetSpeed(int speed)
        {
            _currentSpeed = Mathf.Clamp(speed, 0, 4);
        }

        /// <summary>Дата из сохранения.</summary>
        public void SetDate(int day, int month, int year)
        {
            _day = Mathf.Clamp(day, 1, 30);
            _month = Mathf.Clamp(month, 1, 12);
            _year = Mathf.Max(2200, year);
            _timer = 0f;
        }

        private void TickDay()
        {
            _day++;
            if (_day > 30)
            {
                _day = 1;
                _month++;
                if (_month > 12) { _month = 1; _year++; }
            }
            OnDayPassed?.Invoke(_day, _month, _year);
        }

        public string GetFormattedDate() => $"{_day:D2}.{_month:D2}.{_year}";
    }
}