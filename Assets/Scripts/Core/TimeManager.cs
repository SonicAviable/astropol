using System;
using UnityEngine;

namespace StellarisClone.Core
{
    public class TimeManager : MonoBehaviour
    {
        public static TimeManager Instance { get; private set; }
        public event Action<int, int, int> OnDayPassed;

        [SerializeField] private float baseSecondsPerDay = 1.0f;

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
        }

        private void Update()
        {
            HandleInput();
            if (_currentSpeed == 0) return;

            float interval = baseSecondsPerDay / _currentSpeed;
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

            if (Input.GetKeyDown(KeyCode.Space)) SetSpeed(_currentSpeed == 0 ? 1 : 0);
            if (Input.GetKeyDown(KeyCode.Alpha1)) SetSpeed(1);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SetSpeed(2);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SetSpeed(4);
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