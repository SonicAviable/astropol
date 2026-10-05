using System;
using UnityEngine;
using UnityEngine.EventSystems;
using StellarisClone.Core;

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

    if (Data == null) return;

    // Звук клика
    SFXManager.Play("ui_click", 0.9f, UnityEngine.Random.Range(0.96f, 1.04f));

    // 1. Активируем анимированный прицел выбора на галактической карте
    if (GalaxyView.Instance != null)
    {
        GalaxyView.Instance.SelectSystem(Data.Id);
    }

    // 2. Отправляем событие выбора для UI-панели информации
    OnSystemSelected?.Invoke(Data);

    // 3. Проверка на двойной клик для входа внутрь звёздной системы к планетам
    if (Time.time - _lastClickTime < DoubleClickThreshold)
    {
        SFXManager.Play("ui_open", 1f, 1f);   // звук «открытия» системы
        OnSystemEntered?.Invoke(Data);
    }

    _lastClickTime = Time.time;
}
    }
}