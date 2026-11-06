using System;
using UnityEngine;
using UnityEngine.EventSystems;
using StellarisClone.Core;
using Sfx = StellarisClone.Core.Audio.Sfx;

namespace StellarisClone.Rendering
{
    public class StarSystemSelector : MonoBehaviour
    {
        public static event Action<StarSystem> OnSystemSelected;
        public static event Action<StarSystem> OnSystemEntered;

        public StarSystem Data { get; private set; }

        private float _lastClickTime;
        private const float DoubleClickThreshold = 0.35f;

        public void Initialize(StarSystem data)
        {
            Data = data;
        }
private void OnMouseDown()
{
    // Блокируем клик сквозь элементы интерфейса
    if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        return;

    if (Data == null || MenuAtmosphere.IsActive || LoadingScreen.IsActive) return;
    // Под курсором планета мини-системы — клик её (откроет экран планеты)
    if (GalaxyView.HoveredMiniPlanet != null) return;

    // Звук клика
    SFXManager.Play(Sfx.SystemSelect);

    // 1. Активируем анимированный прицел выбора на галактической карте
    if (GalaxyView.Instance != null)
    {
        GalaxyView.Instance.SelectSystem(Data.Id);
    }

    // 2. Отправляем событие выбора для UI-панели информации
    OnSystemSelected?.Invoke(Data);

    // Вход внутрь системы убран: планеты и звезда видны на карте при приближении (мини-системы GalaxyView)
    _lastClickTime = Time.time;
}
    }
}