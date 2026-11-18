using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using StellarisClone.Core;
using StellarisClone.Core.Audio;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Экран «Сохранение» (в одном стиле с экраном загрузки).
    /// Слева — текущая партия: арт правителя, герб, показатели, имя сохранения и главная кнопка.
    /// Справа — хроника: сохранения этой партии (или все) на линии времени, у каждого «Перезаписать» и удаление.
    /// Перезапись и удаление — вторым кликом.
    /// </summary>
    public sealed class SaveScreen : MonoBehaviour
    {
        private const float ColW = 568f;

        private readonly List<MenuSlideIn> _slides = new List<MenuSlideIn>();
        private System.Action _onClose;
        private CanvasGroup _cg;
        private RectTransform _hero, _timeline;
        private Text _status, _listCap;
        private InputField _name;
        private MenuPrimaryButton _saveBtn;
        private MenuSegment _scope;
        private SaveMeta _cur;
        private string _selSlot, _armedDelete, _armedOverwrite;
        private bool _armedMain, _shown;
        private float _armedT, _doneT, _k, _kv;
        private static int s_scope;

        public static SaveScreen Create(RectTransform root, System.Action onClose)
        {
            var rt = LGBuild.Rect(root, "Save");
            rt.Stretch();
            var s = rt.gameObject.AddComponent<SaveScreen>();
            s._onClose = onClose;
            s.Build(rt);
            rt.gameObject.SetActive(false);
            return s;
        }

        private void OnEnable() => SaveSystem.OnSavesChanged += OnSavesChanged;
        private void OnDisable() => SaveSystem.OnSavesChanged -= OnSavesChanged;
        private void OnSavesChanged() { if (_shown) { BuildTimeline(false); UpdateMain(); } }

        private void Build(RectTransform rt)
        {
            _cg = rt.gameObject.AddComponent<CanvasGroup>();
            _cg.alpha = 0f;
            var dim = LGBuild.Panel(rt, "Dim", new Color(0.005f, 0.012f, 0.022f, 0.5f), raycast: true);
            dim.rectTransform.Stretch();
            LG.Ignore(dim.gameObject);

            var back = MenuLink.Create(rt, Loc.T("НАЗАД"), new Color(0.75f, 0.85f, 0.90f), () => _onClose?.Invoke(), LGIcon.Back);
            ((RectTransform)back.transform).At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(80, -52), new Vector2(150, 36));
            var title = LGBuild.Label(rt, Loc.T("СОХРАНЕНИЕ"), 34, Color.white, TextAnchor.MiddleLeft, bold: true);
            title.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(240, -48), new Vector2(500, 44));

            // ---------------- Слева — текущая партия
            var cap = LGBuild.Label(rt, Loc.T("ТЕКУЩАЯ ПАРТИЯ"), 14, UIManager.DS.NeonCyan, TextAnchor.UpperLeft, bold: true);
            cap.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(80, -130), new Vector2(ColW, 20));

            var left = LGBuild.Rect(rt, "Current");
            left.anchorMin = new Vector2(0, 0);
            left.anchorMax = new Vector2(0, 1);
            left.pivot = new Vector2(0, 0.5f);
            left.offsetMin = new Vector2(76, 60);
            left.offsetMax = new Vector2(76 + ColW, -156);
            _slides.Add(MenuSlideIn.Add(left.gameObject, 0.04f, new Vector2(-40f, 0f)));

            _hero = LGBuild.Rect(left, "Hero");
            _hero.TopBand(0, 380);

            // Имя и главная кнопка — внизу колонки
            var form = LGBuild.Rect(left, "Form");
            form.anchorMin = new Vector2(0, 0);
            form.anchorMax = new Vector2(1, 0);
            form.pivot = new Vector2(0.5f, 0);
            form.offsetMin = new Vector2(0, 0);
            form.offsetMax = new Vector2(0, 196);

            var ncap = LGBuild.Label(form, Loc.T("НАЗВАНИЕ СОХРАНЕНИЯ"), 13, new Color(0.56f, 0.66f, 0.71f), TextAnchor.UpperLeft, bold: true);
            ncap.rectTransform.TopBand(0, 18);
            var inputHost = LGBuild.Rect(form, "InputHost");
            inputHost.TopBand(26, 56);
            _name = LGControls.Input(inputHost, "", Loc.T("Название сохранения"), 40);
            _name.textComponent.fontSize = 18;
            _name.onValueChanged.AddListener(_ => { _armedMain = false; UpdateMain(); });

            _status = LGBuild.Label(form, "", 13, new Color(0.56f, 0.66f, 0.71f), TextAnchor.MiddleLeft);
            _status.rectTransform.TopBand(88, 22);
            _status.supportRichText = true;

            _saveBtn = MenuPrimaryButton.Create(form, Loc.T("СОХРАНИТЬ"), 18, DoSave, MenuPrimaryButton.Green);
            var sb = (RectTransform)_saveBtn.transform;
            sb.anchorMin = new Vector2(0, 0);
            sb.anchorMax = new Vector2(1, 0);
            sb.pivot = new Vector2(0.5f, 0);
            sb.offsetMin = new Vector2(0, 0);
            sb.offsetMax = new Vector2(0, 66);

            // ---------------- Справа — хроника
            var panel = LGBuild.Panel(rt, "Chronicle", new Color(0.03f, 0.06f, 0.10f, 0.92f), raycast: true);
            var prt = panel.rectTransform;
            prt.Stretch(680, 60, 80, 130);
            LG.Glass(panel.gameObject, 22f).SetRim(new Color(0.45f, 0.95f, 0.9f, 0.3f));
            MenuPanelFx.Sheen(prt);
            _slides.Add(MenuSlideIn.Add(panel.gameObject, 0.12f, new Vector2(50f, 0f)));

            _listCap = LGBuild.Label(prt, "", 14, UIManager.DS.NeonCyan, TextAnchor.UpperLeft, bold: true);
            _listCap.rectTransform.TopBand(36, 20, 36, 420);
            var hcap = LGBuild.Label(prt, Loc.T("Куда сохранить"), 30, Color.white, TextAnchor.UpperLeft, bold: true);
            hcap.rectTransform.TopBand(60, 40, 36, 420);

            var scopeHost = LGBuild.Rect(prt, "Scope");
            scopeHost.At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-36, -40), new Vector2(380, 48));
            _scope = MenuSegment.Create(scopeHost, new[] { Loc.T("Эта партия"), Loc.T("Все сохранения") }, null, s_scope,
                                        i => { s_scope = i; _armedDelete = _armedOverwrite = null; BuildTimeline(true); });
            _scope.GetComponent<RectTransform>().Stretch();

            var tl = LGBuild.Rect(prt, "Timeline");
            tl.Stretch(36, 60, 36, 130);
            _timeline = LGBuild.ScrollList(tl, 14f, 4);
            var hint = LGBuild.Label(prt, Loc.T("Клик по сохранению подставит его название. Перезапись и удаление — вторым нажатием."),
                                     13, new Color(0.50f, 0.60f, 0.65f), TextAnchor.LowerLeft);
            hint.rectTransform.Stretch(36, 24, 36, 0);
        }

        public void Show()
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            _shown = true;
            _armedDelete = _armedOverwrite = null;
            _armedMain = false;
            _doneT = 0f;
            try { _cur = SaveSystem.CanSaveNow ? SaveSystem.Capture("", false).Meta : null; }
            catch (System.Exception e) { Debug.LogWarning("[SaveScreen] " + e.Message); _cur = null; }
            _selSlot = null;
            _name.SetTextWithoutNotify(_cur != null ? _cur.Name : "");
            foreach (var s in _slides) s.Restart();
            BuildHero();
            BuildTimeline(true);
            UpdateMain();
        }

        public void Hide() => _shown = false;

        // ------------------------------------------------------------------ Текущая партия

        private void BuildHero()
        {
            LGBuild.Clear(_hero);
            var m = _cur;
            var leader = m != null ? LoadScreen.LeaderOf(m) : null;
            var accent = leader != null ? leader.Accent : UIManager.DS.NeonCyan;

            var card = LGBuild.Panel(_hero, "Card", new Color(0.03f, 0.06f, 0.10f, 0.95f));
            card.rectTransform.TopBand(0, 280);
            LG.Glass(card.gameObject, 20f).SetRim(new Color(accent.r, accent.g, accent.b, 0.55f));
            var mask = LG.RoundedMask(card.transform, 19f, 2f);
            if (leader != null && leader.Texture != null)
            {
                var art = LGBuild.Rect(mask, "Art").gameObject.AddComponent<RawImage>();
                art.rectTransform.Stretch();
                art.texture = leader.Texture;
                art.raycastTarget = false;
                art.uvRect = LeaderPortraits.FaceUv(leader, 0.9f, ColW / 280f);
            }
            var fade = LGBuild.Panel(mask, "Fade", new Color(0.02f, 0.04f, 0.08f, 1f));
            fade.sprite = MenuArt.HorizontalFade;
            fade.rectTransform.anchorMin = Vector2.zero;
            fade.rectTransform.anchorMax = new Vector2(0.9f, 1);
            fade.rectTransform.offsetMin = fade.rectTransform.offsetMax = Vector2.zero;
            LG.Ignore(fade.gameObject);
            var floor = LGBuild.Panel(mask, "Floor", new Color(0.02f, 0.04f, 0.08f, 1f));
            floor.sprite = MenuArt.VerticalFade;
            floor.rectTransform.anchorMin = Vector2.zero;
            floor.rectTransform.anchorMax = new Vector2(1, 0.6f);
            floor.rectTransform.offsetMin = floor.rectTransform.offsetMax = Vector2.zero;
            LG.Ignore(floor.gameObject);

            if (m == null)
            {
                var none = LGBuild.Label(card.transform, Loc.T("Сейчас сохранить нельзя: партия завершена или ещё не началась."),
                                         16, new Color(0.70f, 0.78f, 0.82f), TextAnchor.MiddleCenter);
                none.rectTransform.Stretch(30, 0, 30, 0);
                return;
            }

            var emblem = FactionEmblems.ForKey(!string.IsNullOrEmpty(m.Emblem) ? m.Emblem : leader?.Key);
            if (emblem != null)
            {
                var em = LGBuild.Panel(card.transform, "Emblem", Color.white);
                em.sprite = emblem;
                em.preserveAspect = true;
                em.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -22), new Vector2(84, 84));
                LG.Ignore(em.gameObject);
            }
            var date = LGBuild.Label(card.transform, m.GameDate, 15, Color.white, TextAnchor.UpperRight, bold: true);
            date.rectTransform.At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -24), new Vector2(200, 22));
            date.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.8f);

            var name = LGBuild.Label(card.transform, m.Empire, 32, Color.white, TextAnchor.LowerLeft, bold: true);
            name.rectTransform.Stretch(26, 62, 26, 0);
            name.resizeTextForBestFit = true; name.resizeTextMinSize = 20; name.resizeTextMaxSize = 32;
            name.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.8f);
            string ruler = !string.IsNullOrEmpty(m.Ruler) ? m.Ruler : leader?.Name;
            string sub = !string.IsNullOrEmpty(ruler)
                ? Loc.F("<color={0}>{1}</color>  ·  правитель {2}", LGBuild.Hex(accent), m.EmpireTitle ?? "", ruler)
                : $"<color={LGBuild.Hex(accent)}>{m.EmpireTitle}</color>";
            var st = LGBuild.Label(card.transform, sub, 14, new Color(0.74f, 0.82f, 0.86f), TextAnchor.UpperLeft);
            st.supportRichText = true;
            st.rectTransform.Stretch(26, 0, 26, 222);
            st.rectTransform.offsetMin = new Vector2(26, 26);

            // Показатели
            (string v, string k, Color col)[] stats =
            {
                (m.Systems.ToString(), Loc.T("СИСТЕМ"), UIManager.DS.NeonCyan),
                (m.Colonies.ToString(), Loc.T("КОЛОНИЙ"), new Color(0.36f, 0.96f, 0.60f)),
                (m.Techs.ToString(), Loc.T("ТЕХН."), new Color(0.62f, 0.72f, 1f)),
                (SaveSystem.FormatPlayTime(m.PlayTime), Loc.T("В ИГРЕ"), new Color(1f, 0.82f, 0.48f))
            };
            const float gap = 10f;
            float w = (ColW - gap * (stats.Length - 1)) / stats.Length;
            for (int i = 0; i < stats.Length; i++)
            {
                var tile = LGBuild.Panel(_hero, "Stat", new Color(0.04f, 0.08f, 0.12f, 0.9f));
                tile.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(i * (w + gap), -294), new Vector2(w, 80));
                LG.Platter(tile.gameObject, 12f).SetRim(new Color(1f, 1f, 1f, 0.08f));
                var n = LGBuild.Label(tile.transform, stats[i].v, 24, stats[i].col, TextAnchor.UpperCenter, bold: true);
                n.rectTransform.Stretch(4, 0, 4, 12);
                n.resizeTextForBestFit = true; n.resizeTextMinSize = 14; n.resizeTextMaxSize = 24;
                n.rectTransform.offsetMin = new Vector2(4, 30);
                var l = LGBuild.Label(tile.transform, stats[i].k, 11, new Color(0.56f, 0.66f, 0.71f), TextAnchor.LowerCenter, bold: true);
                l.rectTransform.Stretch(0, 12, 0, 0);
            }
        }

        // ------------------------------------------------------------------ Главная кнопка

        private string CurrentName()
            => string.IsNullOrWhiteSpace(_name.text) ? (_cur != null ? _cur.Name : Loc.T("Сохранение")) : _name.text.Trim();

        private void UpdateMain()
        {
            if (_saveBtn == null) return;
            bool can = _cur != null && SaveSystem.CanSaveNow;
            _saveBtn.Interactable = can;
            string slot = SaveSystem.SlotFromName(CurrentName());
            bool exists = SaveSystem.Exists(slot);
            if (_doneT > 0f)
            {
                _saveBtn.SetLabel(Loc.T("СОХРАНЕНО"));
                _saveBtn.SetColor(MenuPrimaryButton.Green);
            }
            else if (_armedMain)
            {
                _saveBtn.SetLabel(Loc.T("ТОЧНО ПЕРЕЗАПИСАТЬ?"));
                _saveBtn.SetColor(MenuPrimaryButton.Red);
            }
            else
            {
                _saveBtn.SetLabel(exists ? Loc.T("ПЕРЕЗАПИСАТЬ") : Loc.T("СОХРАНИТЬ"));
                _saveBtn.SetColor(exists ? MenuPrimaryButton.Cyan : MenuPrimaryButton.Green);
            }

            if (!can) _status.text = Loc.T("Сейчас сохранить нельзя");
            else if (exists)
            {
                var e = SaveSystem.List().Find(x => x.Slot == slot);
                _status.text = e != null
                    ? Loc.F("<color=#FFB27A>Заменит сохранение</color>  ·  {0}  ·  {1}", e.Meta.GameDate, SaveSystem.FormatSavedAt(e.SavedAtLocal))
                    : Loc.T("<color=#FFB27A>Заменит существующее сохранение</color>");
            }
            else _status.text = Loc.T("<color=#7FE0B0>Новое сохранение</color>");
        }

        private void DoSave()
        {
            if (_cur == null || !SaveSystem.CanSaveNow)
            {
                NotificationCenter.Show(Loc.T("Сейчас сохранить нельзя"), Loc.T("Партия завершена или ещё не началась"), NotificationCenter.Kind.Warning, 3f);
                return;
            }
            string name = CurrentName();
            string slot = SaveSystem.SlotFromName(name);
            if (SaveSystem.Exists(slot) && !_armedMain)
            {
                _armedMain = true;
                _armedDelete = _armedOverwrite = null;
                _armedT = 3f;
                UpdateMain();
                return;
            }
            SaveAs(name, slot);
        }

        private void SaveAs(string name, string slot)
        {
            _armedMain = false;
            _armedDelete = _armedOverwrite = null;
            _selSlot = slot;
            if (SaveSystem.Save(name))
            {
                SFXManager.Play(Sfx.UiConfirm);
                _doneT = 1.6f;
            }
            UpdateMain();   // список перестроится событием сохранения
        }

        // ------------------------------------------------------------------ Хроника

        private void BuildTimeline(bool animate)
        {
            LGBuild.Clear(_timeline);
            bool mine = _scope.Value == 0 && _cur != null;
            _listCap.text = mine ? Loc.T("ХРОНИКА ЭТОЙ ПАРТИИ") : Loc.T("ВСЕ СОХРАНЕНИЯ");
            var saves = new List<SaveEntry>();
            foreach (var e in SaveSystem.List())
                if (!mine || e.Meta.Campaign == _cur.Campaign) saves.Add(e);

            if (saves.Count == 0)
            {
                var row = LGBuild.Rect(_timeline, "Empty");
                LGBuild.Height(row.gameObject, 120f);
                var t = LGBuild.Label(row, mine ? Loc.T("В этой партии ещё нет сохранений — сохраните первое слева.")
                                                : Loc.T("Сохранений пока нет."),
                                      17, new Color(0.62f, 0.72f, 0.76f), TextAnchor.MiddleCenter);
                t.rectTransform.Stretch();
                return;
            }

            for (int i = 0; i < saves.Count; i++)
            {
                var e = saves[i];
                string slot = e.Slot;
                bool on = slot == _selSlot;
                bool armedDel = _armedDelete == slot;
                bool armedOw = _armedOverwrite == slot;
                var leader = LoadScreen.LeaderOf(e.Meta);
                var accent = leader != null ? leader.Accent : UIManager.DS.NeonCyan;

                var row = LGBuild.Rect(_timeline, "Save");
                LGBuild.Height(row.gameObject, 92f);

                var line = LGBuild.Panel(row, "Line", new Color(0.44f, 0.90f, 0.85f, i == saves.Count - 1 ? 0.1f : 0.35f));
                line.rectTransform.At(new Vector2(0, 0), new Vector2(0.5f, 0), new Vector2(10, -14), new Vector2(2, 106));
                LG.Ignore(line.gameObject);
                var dot = LGBuild.Panel(row, "Dot", on ? new Color(0.44f, 0.95f, 0.88f) : mine ? new Color(0.17f, 0.24f, 0.28f) : accent * 0.6f);
                dot.sprite = MenuArt.SoftDisc;
                dot.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(10, 0), new Vector2(on ? 26 : 16, on ? 26 : 16));
                LG.Ignore(dot.gameObject);

                var body = LGBuild.Panel(row, "Body", on ? new Color(0.06f, 0.20f, 0.22f, 0.9f) : new Color(1f, 1f, 1f, 0.03f), raycast: true);
                body.rectTransform.Stretch(36, 0, 0, 0);
                LG.Platter(body.gameObject, 16f).SetRim(on ? new Color(0.44f, 0.95f, 0.88f, 0.7f) : new Color(1f, 1f, 1f, 0.07f));
                var meta = e.Meta;
                body.gameObject.AddComponent<RowClick>().Init(() => Pick(slot, meta));

                var date = LGBuild.Label(body.transform, meta.GameDate, 26, Color.white, TextAnchor.MiddleLeft, bold: true);
                date.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(26, 0), new Vector2(200, 40));

                var t = LGBuild.Label(body.transform, meta.Name, 17, Color.white, TextAnchor.LowerLeft, bold: true);
                t.rectTransform.Stretch(236, 48, 330, 14);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                float chipX = 236 + Mathf.Min(t.preferredWidth, 260f) + 12;
                var chip = LGBuild.Panel(body.transform, "Chip", meta.Auto ? new Color(0.25f, 0.18f, 0.05f, 0.9f) : new Color(0.05f, 0.20f, 0.20f, 0.9f));
                chip.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(chipX, -20), new Vector2(76, 22));
                var cc = meta.Auto ? new Color(1f, 0.82f, 0.48f) : new Color(0.56f, 0.94f, 0.90f);
                LG.Chip(chip.gameObject, new Color(cc.r, cc.g, cc.b, 0.55f), 6f);
                var ct = LGBuild.Label(chip.transform, meta.Auto ? Loc.T("АВТО") : Loc.T("РУЧНОЕ"), 11, cc, TextAnchor.MiddleCenter, bold: true);
                ct.rectTransform.Stretch();

                string info = Loc.F("{0}  ·  в игре {1}", SaveSystem.FormatSavedAt(e.SavedAtLocal), SaveSystem.FormatPlayTime(meta.PlayTime));
                if (!mine) info = $"<color={LGBuild.Hex(accent)}>{meta.Empire}</color>  ·  {info}";
                var w = LGBuild.Label(body.transform, info, 13, new Color(0.56f, 0.66f, 0.71f), TextAnchor.UpperLeft);
                w.supportRichText = true;
                w.rectTransform.Stretch(236, 0, 330, 52);

                // Автосохранение перезаписывается само — кнопки нет
                if (!meta.Auto)
                {
                    var ow = LGBuild.Button(body.transform, "Overwrite", armedOw ? UIManager.DS.BtnDanger : UIManager.DS.BtnNeutral,
                                            new Color(1f, 1f, 1f, 0.3f), () => OnOverwrite(slot, meta), null,
                                            armedOw ? Loc.T("ТОЧНО?") : Loc.T("ПЕРЕЗАПИСАТЬ"), 15);
                    ((RectTransform)ow.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-84, 0), new Vector2(190, 50));
                    ow.interactable = _cur != null;
                }

                var del = LGControls.IconButton(body.transform, LGIcon.Trash, 46, armedDel ? new Color(0.45f, 0.08f, 0.06f) : UIManager.DS.BtnNeutral,
                                                armedDel ? new Color(1f, 0.4f, 0.35f, 0.9f) : new Color(1f, 1f, 1f, 0.25f), () => OnDelete(slot));
                ((RectTransform)del.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-22, 0), new Vector2(46, 46));
                TooltipHelper.Attach(del.gameObject, armedDel ? Loc.T("Нажмите ещё раз — сохранение будет удалено") : Loc.T("Удалить сохранение"));
                if (armedOw || armedDel)
                {
                    var warn = LGBuild.Label(body.transform, armedDel ? Loc.T("ещё раз — удалить") : Loc.T("ещё раз — заменить"),
                                             12, new Color(1f, 0.55f, 0.5f), TextAnchor.MiddleRight, bold: true);
                    warn.rectTransform.At(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-84, 6), new Vector2(190, 16));
                }

                if (animate) MenuSlideIn.Add(row.gameObject, 0.18f + 0.05f * Mathf.Min(i, 8), new Vector2(30f, 0f));
            }
        }

        /// <summary>Клик по строке — выделить и подставить её название в поле.</summary>
        private void Pick(string slot, SaveMeta meta)
        {
            if (_selSlot == slot) return;
            SFXManager.Play("ui_click", 0.6f, 1.1f);
            _selSlot = slot;
            _armedDelete = _armedOverwrite = null;
            if (!meta.Auto) _name.SetTextWithoutNotify(meta.Name);
            _armedMain = false;
            BuildTimeline(false);
            UpdateMain();
        }

        private void OnOverwrite(string slot, SaveMeta meta)
        {
            if (_armedOverwrite == slot) { SaveAs(meta.Name, slot); return; }
            _armedOverwrite = slot;
            _armedDelete = null;
            _armedMain = false;
            _selSlot = slot;
            _armedT = 3f;
            BuildTimeline(false);
            UpdateMain();
        }

        private void OnDelete(string slot)
        {
            if (_armedDelete == slot)
            {
                _armedDelete = null;
                SFXManager.Play("ui_click", 0.7f, 0.7f);
                if (_selSlot == slot) _selSlot = null;
                SaveSystem.Delete(slot);   // список обновится событием OnSavesChanged
                return;
            }
            _armedDelete = slot;
            _armedOverwrite = null;
            _armedT = 3f;
            BuildTimeline(false);
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);
            _k = LGEase.Spring(_k, _shown ? 1f : 0f, ref _kv, 4.5f, 0.9f, dt);
            _cg.alpha = Mathf.Clamp01(_k);
            _cg.blocksRaycasts = _cg.interactable = _shown;
            if (!_shown && _k < 0.01f) { gameObject.SetActive(false); return; }

            // Подтверждения гаснут через 3 секунды
            if (_armedDelete != null || _armedOverwrite != null || _armedMain)
            {
                _armedT -= dt;
                if (_armedT <= 0f)
                {
                    bool rows = _armedDelete != null || _armedOverwrite != null;
                    _armedDelete = _armedOverwrite = null;
                    _armedMain = false;
                    if (rows) BuildTimeline(false);
                    UpdateMain();
                }
            }
            if (_doneT > 0f)
            {
                _doneT -= dt;
                if (_doneT <= 0f) UpdateMain();
            }
        }

        private sealed class RowClick : MonoBehaviour, IPointerClickHandler
        {
            private System.Action _select;
            public void Init(System.Action select) => _select = select;
            public void OnPointerClick(PointerEventData e)
            {
                if (e.button == PointerEventData.InputButton.Left) _select?.Invoke();
            }
        }
    }
}
