using System;
using System.Collections.Generic;
using UnityEngine;
using StellarisClone.Rendering;

namespace StellarisClone.Core
{
    /// <summary>
    /// Знакомства цивилизаций, как в Civilization VI: в начале партии никто никого не знает.
    /// Первый контакт — когда одна сторона впервые видит другую (её систему или корабль) сквозь туман войны.
    /// До контакта:
    ///   • империи нет в окне дипломатии и во вкладке соперников, в сообщениях она — «неизвестная цивилизация»;
    ///   • никаких предложений, требований, пактов и объявлений войны друг другу;
    ///   • ИИ между собой тоже не враждуют и не вступают в коалиции против незнакомцев.
    /// Контакт игрока сопровождается сигналом, уведомлением и приветствием правителя в окне дипломатии.
    /// Сохраняется; в старых сохранениях все считаются знакомыми.
    /// </summary>
    public static class Contacts
    {
        public const string UnknownName = "Неизвестная цивилизация";

        private static readonly HashSet<long> _met = new HashSet<long>();
        private static readonly List<int> _pendingGreetings = new List<int>();

        /// <summary>Первый контакт (a, b) — порядок не важен.</summary>
        public static event Action<int, int> OnFirstContact;

        private static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        public static void Reset()
        {
            _met.Clear();
            _pendingGreetings.Clear();
        }

        /// <summary>Знакомы ли две стороны. Вне партии (меню) и для угроз (пираты, левиафаны) — всегда да.</summary>
        public static bool Met(int a, int b)
        {
            if (a == b || a < 0 || b < 0) return true;
            if (Threats.IsThreat(a) || Threats.IsThreat(b)) return true;
            if (!Vision.Active) return true;
            return _met.Contains(Key(a, b));
        }

        public static bool PlayerMet(int owner) => Met(0, owner);

        /// <summary>Имя для игрока: настоящее — только после контакта.</summary>
        public static string NameForPlayer(int owner, string fallback = "Соперник")
            => PlayerMet(owner) ? AIEmpireManager.NameOf(owner, fallback) : UnknownName;

        public static int UnmetByPlayer
        {
            get
            {
                int n = 0;
                foreach (var ai in AIEmpireManager.All) if (!ai.IsEliminated && !PlayerMet(ai.OwnerId)) n++;
                return n;
            }
        }

        public static void Meet(int a, int b)
        {
            if (a == b || a < 0 || b < 0 || Threats.IsThreat(a) || Threats.IsThreat(b)) return;
            if (!_met.Add(Key(a, b))) return;
            OnFirstContact?.Invoke(a, b);
            if (a == 0 || b == 0) PlayerContact(a == 0 ? b : a);
            AIEmpireManager.NotifyDiplomacyChanged();
        }

        private static void PlayerContact(int owner)
        {
            var ai = AIEmpireManager.For(owner);
            if (ai == null) return;
            SFXManager.Play(Audio.Sfx.Anomaly);
            NotificationCenter.Show("Первый контакт",
                $"Мы встретили цивилизацию «{ai.AIName}». Её правитель выходит на связь — откройте дипломатию, чтобы ответить",
                NotificationCenter.Kind.Success, 9f);
            _pendingGreetings.Add(owner);
        }

        /// <summary>Отложенные приветствия: окно дипломатии открывается, когда на экране нет других окон.</summary>
        public static int TakePendingGreeting()
        {
            if (_pendingGreetings.Count == 0) return -1;
            int o = _pendingGreetings[0];
            _pendingGreetings.RemoveAt(0);
            return o;
        }

        // ==================== ОБНАРУЖЕНИЕ ====================

        /// <summary>
        /// Вызывается после пересчёта видимости: каждая сторона знакомится со всеми, чьи системы или корабли она сейчас видит.
        /// </summary>
        public static void DetectFrom(Dictionary<int, HashSet<int>> visible)
        {
            var systems = EmpireStats.Systems;
            foreach (var kv in visible)
            {
                int viewer = kv.Key;
                foreach (int id in kv.Value)
                {
                    if (id < 0 || id >= systems.Count) continue;
                    int owner = systems[id].OwnerId;
                    if (owner >= 0 && owner != viewer) Meet(viewer, owner);
                }
            }
            var fm = FleetManager.Instance;
            if (fm == null) return;
            foreach (var f in fm.AllFleets)
            {
                var d = f?.Data;
                if (d == null || d.Destroyed) continue;
                foreach (var kv in visible)
                {
                    if (kv.Key == d.OwnerId) continue;
                    bool seen = kv.Value.Contains(d.CurrentSystemId)
                             || (d.State == FleetState.InHyperlane && d.TargetSystemId >= 0 && kv.Value.Contains(d.TargetSystemId));
                    if (seen) Meet(kv.Key, d.OwnerId);
                }
            }
        }

        // ==================== СОХРАНЕНИЕ ====================

        // Метка «сохранение уже знает о контактах» — чтобы пустой список не путался со старым форматом
        private const long SaveMarker = long.MinValue;

        public static List<long> Capture()
        {
            var list = new List<long> { SaveMarker };
            list.AddRange(_met);
            return list;
        }

        /// <summary>Пустой список или null — сохранение из версии без контактов: все знакомы.</summary>
        public static void Restore(List<long> list)
        {
            Reset();
            if (list != null && list.Count > 0)
            {
                foreach (var k in list) if (k != SaveMarker) _met.Add(k);
                return;
            }
            var owners = new List<int> { 0 };
            foreach (var ai in AIEmpireManager.All) owners.Add(ai.OwnerId);
            for (int i = 0; i < owners.Count; i++)
                for (int j = i + 1; j < owners.Count; j++) _met.Add(Key(owners[i], owners[j]));
        }
    }

    /// <summary>Показывает приветствие правителя после первого контакта, когда игрок не занят другим окном.</summary>
    public class ContactGreeter : MonoBehaviour
    {
        private float _timer;

        private void Update()
        {
            if ((_timer -= Time.unscaledDeltaTime) > 0f) return;
            _timer = 0.5f;
            if (!UIManager.IsGameStarted) return;
            var dm = DiplomacyModal.Instance;
            if (dm == null || dm.IsOpen) return;
            if (SystemViewManager.Instance != null && SystemViewManager.Instance.IsInSystemView) return;
            int owner = Contacts.TakePendingGreeting();
            if (owner < 0) return;
            dm.OpenFirstContact(owner);
        }
    }
}
