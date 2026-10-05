using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;
using StellarisClone.Core.Audio;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Список сохранений. Режим «Загрузка» (главное меню, пауза) и «Сохранение» (пауза):
    /// имя слота, перезапись, удаление с подтверждением, автосохранения помечены.
    /// </summary>
    public class SaveLoadPanel : MonoBehaviour
    {
        public enum Mode { Load, Save }

        private Mode _mode;
        private bool _confirmLoad;
        private RectTransform _list;
        private InputField _name;
        private Text _saveLabel;
        private string _armedDelete, _armedLoad;
        private float _armTimer;
        private bool _armedOverwrite;
        private string _highlight;

        private static readonly Color RowTint = new Color(0.05f, 0.10f, 0.135f, 0.80f);

        public static SaveLoadPanel Build(RectTransform host, Mode mode, bool confirmLoad)
        {
            var rt = LGBuild.Rect(host, mode == Mode.Save ? "SavePanel" : "LoadPanel");
            rt.Stretch();
            var p = rt.gameObject.AddComponent<SaveLoadPanel>();
            p._mode = mode;
            p._confirmLoad = confirmLoad;
            p.BuildLayout(rt);
            return p;
        }

        private void OnEnable() => SaveSystem.OnSavesChanged += Refresh;
        private void OnDisable() => SaveSystem.OnSavesChanged -= Refresh;

        private void BuildLayout(RectTransform rt)
        {
            float listTop = 0f;
            if (_mode == Mode.Save)
            {
                var bar = LGBuild.Rect(rt, "SaveBar");
                bar.TopBand(0, 48);
                var inputHost = LGBuild.Rect(bar, "InputHost");
                inputHost.Stretch(0, 0, 250, 0);
                _name = LGControls.Input(inputHost, DefaultName(), "Название сохранения", 40);
                _name.onValueChanged.AddListener(_ => { _armedOverwrite = false; UpdateSaveLabel(); });

                var btn = LGBuild.Button(bar, "SaveBtn", UIManager.DS.BtnSuccess, UIManager.DS.Green, DoSave, LGIcon.Save, "СОХРАНИТЬ", 13);
                var brt = (RectTransform)btn.transform;
                brt.anchorMin = new Vector2(1, 0);
                brt.anchorMax = new Vector2(1, 1);
                brt.pivot = new Vector2(1, 0.5f);
                brt.sizeDelta = new Vector2(238, 0);
                brt.anchoredPosition = Vector2.zero;
                _saveLabel = btn.GetComponentInChildren<Text>();
                listTop = 62f;
            }

            var view = LGBuild.Rect(rt, "ListHost");
            view.Stretch(0, 0, 0, listTop);
            _list = LGBuild.ScrollList(view, 8f, 2);
            Refresh();
            UpdateSaveLabel();
        }

        private static string DefaultName()
        {
            var f = UIManager.Instance != null ? UIManager.Instance.SelectedFaction : null;
            string date = TimeManager.Instance != null ? TimeManager.Instance.GetFormattedDate() : "";
            return $"{(f != null ? f.Name : "Империя")} · {date}";
        }

        private void Update()
        {
            if (_armTimer <= 0f) return;
            _armTimer -= Time.unscaledDeltaTime;
            if (_armTimer > 0f) return;
            _armedDelete = _armedLoad = null;
            _armedOverwrite = false;
            UpdateSaveLabel();
            Refresh();
        }

        // ------------------------------------------------------------------ Сохранение

        private void UpdateSaveLabel()
        {
            if (_saveLabel == null || _name == null) return;
            bool exists = SaveSystem.Exists(SaveSystem.SlotFromName(_name.text));
            _saveLabel.text = _armedOverwrite ? "ТОЧНО ПЕРЕЗАПИСАТЬ?" : exists ? "ПЕРЕЗАПИСАТЬ" : "СОХРАНИТЬ";
        }

        private void DoSave()
        {
            if (!SaveSystem.CanSaveNow)
            {
                NotificationCenter.Show("Сейчас сохранить нельзя", "Партия завершена или ещё не началась", NotificationCenter.Kind.Warning, 3f);
                return;
            }
            string name = string.IsNullOrWhiteSpace(_name.text) ? DefaultName() : _name.text.Trim();
            string slot = SaveSystem.SlotFromName(name);
            if (SaveSystem.Exists(slot) && !_armedOverwrite)
            {
                _armedOverwrite = true;
                _armTimer = 3f;
                UpdateSaveLabel();
                return;
            }
            _armedOverwrite = false;
            _highlight = slot;   // список перестроится событием сохранения — строка вспыхнет
            if (SaveSystem.Save(name)) SFXManager.Play(Sfx.UiConfirm);
            _highlight = null;
            UpdateSaveLabel();
        }

        // ------------------------------------------------------------------ Список

        public void Refresh()
        {
            if (_list == null) return;
            LGBuild.Clear(_list);
            var saves = SaveSystem.List();

            if (saves.Count == 0)
            {
                var empty = LGBuild.Panel(_list, "Empty", new Color(0.04f, 0.08f, 0.11f, 0.6f));
                LGBuild.Height(empty.gameObject, 180);
                LG.Platter(empty.gameObject, 18f).GlowMultiplier = 0f;
                var ic = LGIcons.Create(empty.transform, LGIcon.Load, 40, new Color(1, 1, 1, 0.25f));
                ic.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 26), new Vector2(40, 40));
                var t = LGBuild.Label(empty.transform, _mode == Mode.Save
                        ? "Сохранений пока нет — введите название и нажмите «Сохранить»"
                        : "Сохранений пока нет\n<size=11><color=#8AA2A8>Сохранить партию можно в меню паузы (Esc) или клавишей F5</color></size>",
                    14, UIManager.DS.TextMuted, TextAnchor.MiddleCenter, wrap: true);
                t.rectTransform.Stretch(30, 10, 30, 70);
                return;
            }

            int i = 0;
            foreach (var e in saves) BuildRow(e, i++);
        }

        private void BuildRow(SaveEntry e, int index)
        {
            var m = e.Meta;
            bool armedDel = _armedDelete == e.Slot;
            bool armedLoad = _armedLoad == e.Slot;

            var row = LGBuild.Panel(_list, "Save_" + e.Slot, RowTint, raycast: true);
            LGBuild.Height(row.gameObject, 86);
            var fx = LG.Platter(row.gameObject, 18f);
            fx.FillMultiplier = 0.85f;
            fx.GlowMultiplier = 0.25f;
            var ec = m.EmpireColor;
            fx.SetRim(new Color(ec.r, ec.g, ec.b, _highlight == e.Slot ? 0.8f : 0.22f));
            if (_highlight == e.Slot) row.gameObject.AddComponent<RowFlash>();

            // Клик по строке: в режиме сохранения подставляет имя
            if (_mode == Mode.Save)
            {
                var b = row.gameObject.AddComponent<Button>();
                b.transition = Selectable.Transition.None;
                string nm = m.Auto ? DefaultName() : m.Name;
                b.onClick.AddListener(() => { _name.text = nm; _armedOverwrite = false; UpdateSaveLabel(); });
            }

            // Эмблема империи
            var emb = LGBuild.Panel(row.transform, "Emblem", new Color(ec.r * 0.3f, ec.g * 0.3f, ec.b * 0.3f, 1f));
            emb.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(14, 0), new Vector2(58, 58));
            var efx = LG.Platter(emb.gameObject, 29f);
            efx.FillMultiplier = 2f;
            efx.SetRim(new Color(ec.r, ec.g, ec.b, 0.85f));
            var eic = LGIcons.Create(emb.transform, m.Auto ? LGIcon.Clock : LGIcon.Globe, 28, ec);
            eic.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(28, 28));

            var body = LGBuild.Rect(row.transform, "Body");
            body.Stretch(88, 12, 330, 12);
            var title = LGBuild.Label(body, m.Name, 15, UIManager.DS.TextPrimary, TextAnchor.UpperLeft, bold: true);
            title.horizontalOverflow = HorizontalWrapMode.Wrap;
            title.verticalOverflow = VerticalWrapMode.Truncate;
            title.rectTransform.offsetMax = new Vector2(m.Auto ? -70 : 0, 0);
            if (m.Auto)
            {
                var chip = LGBuild.Panel(body, "AutoChip", new Color(0.3f, 0.24f, 0.06f, 1f));
                chip.rectTransform.At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(62, 18));
                LG.Chip(chip.gameObject, new Color(1f, 0.82f, 0.36f, 0.6f), 9f);
                LGBuild.Label(chip.transform, "АВТО", 9, UIManager.DS.Gold, TextAnchor.MiddleCenter, bold: true);
            }

            string diff = NewGameSettings.DifficultyNames[Mathf.Clamp(m.Difficulty, 0, 2)];
            string size = NewGameSettings.SizeNames[Mathf.Clamp(m.GalaxySize, 0, 2)];
            var sub = LGBuild.Label(body, $"<color={LGBuild.Hex(ec)}>{m.Empire}</color>   ·   {m.GameDate}   ·   {size} галактика, {diff.ToLower()}",
                                    11, UIManager.DS.TextMuted, TextAnchor.MiddleLeft);
            sub.rectTransform.offsetMax = new Vector2(0, -2);
            var stats = LGBuild.Label(body,
                $"Систем <b>{m.Systems}</b>   ·   колоний <b>{m.Colonies}</b>   ·   технологий <b>{m.Techs}</b>   ·   в игре {SaveSystem.FormatPlayTime(m.PlayTime)}",
                11, new Color(0.78f, 0.88f, 0.92f), TextAnchor.LowerLeft);

            // Справа: время и кнопки
            var right = LGBuild.Rect(row.transform, "Right");
            right.anchorMin = new Vector2(1, 0);
            right.anchorMax = new Vector2(1, 1);
            right.pivot = new Vector2(1, 0.5f);
            right.sizeDelta = new Vector2(316, 0);
            right.anchoredPosition = new Vector2(-14, 0);

            var when = LGBuild.Label(right, SaveSystem.FormatSavedAt(e.SavedAtLocal), 10, UIManager.DS.TextMuted, TextAnchor.UpperRight);
            when.rectTransform.offsetMax = new Vector2(0, -9);

            var actions = LGBuild.Rect(right, "Actions");
            actions.anchorMin = new Vector2(0, 0);
            actions.anchorMax = new Vector2(1, 0);
            actions.pivot = new Vector2(1, 0);
            actions.offsetMin = new Vector2(0, 12);
            actions.offsetMax = new Vector2(0, 50);

            string slot = e.Slot;
            var del = LGBuild.Button(actions, "Delete",
                armedDel ? UIManager.DS.BtnDanger : UIManager.DS.BtnNeutral,
                armedDel ? UIManager.DS.Red : new Color(1f, 0.5f, 0.5f, 0.35f),
                () => OnDelete(slot), LGIcon.Trash, armedDel ? "УДАЛИТЬ?" : null, 11);
            ((RectTransform)del.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, new Vector2(armedDel ? 118 : 42, 38));
            TooltipHelper.Attach(del.gameObject, "Удалить сохранение");

            if (_mode == Mode.Load)
            {
                var load = LGBuild.Button(actions, "Load",
                    armedLoad ? new Color(0.30f, 0.24f, 0.08f) : UIManager.DS.BtnPrimary,
                    armedLoad ? UIManager.DS.Gold : UIManager.DS.NeonCyan,
                    () => OnLoad(slot), LGIcon.Play, armedLoad ? "ТОЧНО?" : "ЗАГРУЗИТЬ", 12);
                ((RectTransform)load.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(armedDel ? -126 : -50, 0), new Vector2(150, 38));
                if (armedLoad)
                    TooltipHelper.Attach(load.gameObject, "Несохранённый прогресс текущей партии будет потерян");
            }
        }

        private void OnDelete(string slot)
        {
            if (_armedDelete == slot)
            {
                _armedDelete = null;
                SFXManager.Play("ui_deselect", 0.7f, 0.9f);
                SaveSystem.Delete(slot);   // Refresh придёт событием
                UpdateSaveLabel();
                return;
            }
            _armedDelete = slot;
            _armedLoad = null;
            _armTimer = 3f;
            Refresh();
        }

        private void OnLoad(string slot)
        {
            if (_confirmLoad && _armedLoad != slot)
            {
                _armedLoad = slot;
                _armedDelete = null;
                _armTimer = 3f;
                Refresh();
                return;
            }
            SaveSystem.LoadSlot(slot);
        }

        /// <summary>Вспышка кромки только что сохранённой строки.</summary>
        private sealed class RowFlash : MonoBehaviour
        {
            private float _t;
            private LiquidGlassEffect _fx;
            private void Awake() => _fx = GetComponent<LiquidGlassEffect>();
            private void Update()
            {
                _t += Time.unscaledDeltaTime;
                if (_fx != null) _fx.SetPulse(Mathf.Clamp01(1f - _t / 1.6f) * (0.6f + 0.4f * Mathf.Sin(_t * 10f)));
                if (_t > 1.6f) { if (_fx != null) _fx.SetPulse(0f); Destroy(this); }
            }
        }
    }
}
