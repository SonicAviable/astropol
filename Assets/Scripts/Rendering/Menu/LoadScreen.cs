using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Экран «Загрузка» главного меню: сохранения сгруппированы по партиям.
    /// Слева — партии (арт правителя, герб, число сохранений, время в игре), справа — хроника выбранной партии:
    /// сохранения на линии времени, у каждого «Загрузить» и удаление (второй клик подтверждает).
    /// </summary>
    public sealed class LoadScreen : MonoBehaviour
    {
        private sealed class Campaign
        {
            public string Key;
            public readonly List<SaveEntry> Saves = new List<SaveEntry>();
            public SaveEntry Latest => Saves[0];
        }

        private readonly List<Campaign> _camps = new List<Campaign>();
        private readonly List<CampaignCard> _cards = new List<CampaignCard>();
        private readonly List<MenuSlideIn> _slides = new List<MenuSlideIn>();
        private System.Action _onClose;
        private CanvasGroup _cg;
        private RectTransform _list, _panel, _head, _timeline;
        private Text _empty;
        private int _sel;
        private string _selSlot, _armedDelete;
        private float _armedT, _k, _kv;
        private bool _shown;

        public static LoadScreen Create(RectTransform root, System.Action onClose)
        {
            var rt = LGBuild.Rect(root, "Load");
            rt.Stretch();
            var s = rt.gameObject.AddComponent<LoadScreen>();
            s._onClose = onClose;
            s.Build(rt);
            rt.gameObject.SetActive(false);
            return s;
        }

        private void OnEnable() => SaveSystem.OnSavesChanged += OnSavesChanged;
        private void OnDisable() => SaveSystem.OnSavesChanged -= OnSavesChanged;
        private void OnSavesChanged() { if (_shown) Refresh(false); }

        private void Build(RectTransform rt)
        {
            _cg = rt.gameObject.AddComponent<CanvasGroup>();
            _cg.alpha = 0f;
            var dim = LGBuild.Panel(rt, "Dim", new Color(0.005f, 0.012f, 0.022f, 0.5f), raycast: true);
            dim.rectTransform.Stretch();
            LG.Ignore(dim.gameObject);

            var back = MenuLink.Create(rt, Loc.T("НАЗАД"), new Color(0.75f, 0.85f, 0.90f), () => _onClose?.Invoke(), LGIcon.Back);
            ((RectTransform)back.transform).At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(80, -52), new Vector2(150, 36));
            var title = LGBuild.Label(rt, Loc.T("ЗАГРУЗКА"), 34, Color.white, TextAnchor.MiddleLeft, bold: true);
            title.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(240, -48), new Vector2(500, 44));

            // Слева — партии
            var cap = LGBuild.Label(rt, Loc.T("ВАШИ ПАРТИИ"), 14, UIManager.DS.NeonCyan, TextAnchor.UpperLeft, bold: true);
            cap.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(80, -130), new Vector2(560, 20));
            var listHost = LGBuild.Rect(rt, "Campaigns");
            listHost.anchorMin = new Vector2(0, 0);
            listHost.anchorMax = new Vector2(0, 1);
            listHost.pivot = new Vector2(0, 0.5f);
            listHost.offsetMin = new Vector2(76, 60);
            listHost.offsetMax = new Vector2(76 + 568, -156);
            _list = LGBuild.ScrollList(listHost, 14f, 4);

            // Справа — хроника
            var panel = LGBuild.Panel(rt, "Chronicle", new Color(0.03f, 0.06f, 0.10f, 0.92f), raycast: true);
            _panel = panel.rectTransform;
            _panel.Stretch(680, 60, 80, 130);
            LG.Glass(panel.gameObject, 22f).SetRim(new Color(0.45f, 0.95f, 0.9f, 0.3f));
            MenuPanelFx.Sheen(_panel);
            _slides.Add(MenuSlideIn.Add(panel.gameObject, 0.12f, new Vector2(50f, 0f)));

            _head = LGBuild.Rect(_panel, "Head");
            _head.TopBand(30, 110, 36, 36);
            var tl = LGBuild.Rect(_panel, "Timeline");
            tl.Stretch(36, 60, 36, 170);
            _timeline = LGBuild.ScrollList(tl, 14f, 4);
            var hint = LGBuild.Label(_panel, Loc.T("Сохранения сгруппированы по партиям: выберите империю слева, затем точку в её истории."),
                                     13, new Color(0.50f, 0.60f, 0.65f), TextAnchor.LowerLeft);
            hint.rectTransform.Stretch(36, 24, 36, 0);

            _empty = LGBuild.Label(rt, Loc.T("Сохранений пока нет — начните новую партию."), 20, new Color(0.62f, 0.72f, 0.76f), TextAnchor.MiddleCenter);
            _empty.rectTransform.Stretch();
        }

        public void Show()
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            _shown = true;
            _armedDelete = null;
            foreach (var s in _slides) s.Restart();
            Refresh(true);
        }

        public void Hide() => _shown = false;

        // ------------------------------------------------------------------ Данные

        private static string KeyOf(SaveMeta m)
            => !string.IsNullOrEmpty(m.Campaign) ? m.Campaign : $"{m.Empire}|{m.GalaxySize}|{m.Level}";

        private void Refresh(bool animate)
        {
            string selKey = _sel >= 0 && _sel < _camps.Count ? _camps[_sel].Key : null;
            _camps.Clear();
            var byKey = new Dictionary<string, Campaign>();
            foreach (var e in SaveSystem.List())   // уже от новых к старым
            {
                string k = KeyOf(e.Meta);
                if (!byKey.TryGetValue(k, out var c)) { c = new Campaign { Key = k }; byKey[k] = c; _camps.Add(c); }
                c.Saves.Add(e);
            }
            _sel = 0;
            for (int i = 0; i < _camps.Count; i++) if (_camps[i].Key == selKey) _sel = i;

            bool any = _camps.Count > 0;
            _empty.gameObject.SetActive(!any);
            _panel.gameObject.SetActive(any);

            LGBuild.Clear(_list);
            _cards.Clear();
            for (int i = 0; i < _camps.Count; i++)
            {
                int idx = i;
                var card = CampaignCard.Create(_list, _camps[i].Latest.Meta, _camps[i].Saves.Count, MaxPlay(_camps[i]), () => Select(idx));
                card.AppearDelay = animate ? 0.08f + 0.06f * i : 0f;
                _cards.Add(card);
            }
            if (any) Select(_sel, animate);
        }

        private static float MaxPlay(Campaign c)
        {
            float m = 0f;
            foreach (var s in c.Saves) m = Mathf.Max(m, s.Meta.PlayTime);
            return m;
        }

        private void Select(int i, bool animate = true)
        {
            if (i < 0 || i >= _camps.Count) return;
            if (i != _sel) { SFXManager.Play("ui_click", 0.6f, 1.1f); _selSlot = null; _armedDelete = null; }
            _sel = i;
            for (int k = 0; k < _cards.Count; k++) _cards[k].Selected = k == i;
            BuildHead(_camps[i]);
            BuildTimeline(_camps[i], animate);
        }

        // ------------------------------------------------------------------ Хроника

        private void BuildHead(Campaign c)
        {
            LGBuild.Clear(_head);
            var m = c.Latest.Meta;
            var cap = LGBuild.Label(_head, Loc.T("ХРОНИКА ПАРТИИ"), 14, UIManager.DS.NeonCyan, TextAnchor.UpperLeft, bold: true);
            cap.rectTransform.Stretch(0, 0, 470, 0);
            var name = LGBuild.Label(_head, m.Empire, 40, Color.white, TextAnchor.UpperLeft, bold: true);
            name.rectTransform.Stretch(0, 0, 470, 24);
            name.resizeTextForBestFit = true; name.resizeTextMinSize = 24; name.resizeTextMaxSize = 40;
            name.rectTransform.offsetMin = new Vector2(0, 40);
            var sub = LGBuild.Label(_head, Subtitle(m), 15, new Color(0.62f, 0.72f, 0.76f), TextAnchor.LowerLeft);
            sub.rectTransform.Stretch(0, 8, 470, 0);

            (int v, string k, Color col)[] stats =
            {
                (m.Systems, Loc.T("СИСТЕМ"), UIManager.DS.NeonCyan),
                (m.Colonies, Loc.T("КОЛОНИЙ"), new Color(0.36f, 0.96f, 0.60f)),
                (m.Techs, Loc.T("ТЕХН."), new Color(0.62f, 0.72f, 1f))
            };
            for (int i = 0; i < stats.Length; i++)
            {
                var tile = LGBuild.Panel(_head, "Stat", new Color(0.04f, 0.08f, 0.12f, 0.9f));
                tile.rectTransform.At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-(2 - i) * 148f, 0), new Vector2(136, 76));
                LG.Platter(tile.gameObject, 12f).SetRim(new Color(1f, 1f, 1f, 0.08f));
                var n = LGBuild.Label(tile.transform, stats[i].v.ToString(), 26, stats[i].col, TextAnchor.UpperCenter, bold: true);
                n.rectTransform.Stretch(0, 0, 0, 10);
                var l = LGBuild.Label(tile.transform, stats[i].k, 11, new Color(0.56f, 0.66f, 0.71f), TextAnchor.LowerCenter, bold: true);
                l.rectTransform.Stretch(0, 12, 0, 0);
            }
        }

        private static string Subtitle(SaveMeta m)
        {
            var parts = new List<string>();
            var leader = LeaderOf(m);
            string ruler = !string.IsNullOrEmpty(m.Ruler) ? m.Ruler : leader?.Name;
            if (!string.IsNullOrEmpty(ruler)) parts.Add(ruler);
            if (m.GalaxySize >= 0 && m.GalaxySize < NewGameSettings.SizeNames.Length)
                parts.Add(Loc.F("{0} галактика", NewGameSettings.SizeNames[m.GalaxySize]).ToLower());
            if (m.Shape >= 0 && m.Shape < NewGameSettings.ShapeNames.Length) parts.Add(NewGameSettings.ShapeNames[m.Shape].ToLower());
            if (m.Level >= 0) parts.Add(NewGameSettings.LevelName(m.Level).ToLower());
            return string.Join("  ·  ", parts);
        }

        public static LeaderPortraits.Entry LeaderOf(SaveMeta m)
            => !string.IsNullOrEmpty(m.FactionKey) ? LeaderPortraits.ForKey(m.FactionKey) : LeaderPortraits.ForFaction(m.Empire);

        private void BuildTimeline(Campaign c, bool animate)
        {
            LGBuild.Clear(_timeline);
            if (_selSlot == null || !c.Saves.Exists(s => s.Slot == _selSlot)) _selSlot = c.Saves[0].Slot;
            for (int i = 0; i < c.Saves.Count; i++)
            {
                var e = c.Saves[i];
                bool on = e.Slot == _selSlot;
                bool armed = _armedDelete == e.Slot;
                var row = LGBuild.Rect(_timeline, "Save");
                LGBuild.Height(row.gameObject, 92f);

                // Точка и линия времени
                var line = LGBuild.Panel(row, "Line", new Color(0.44f, 0.90f, 0.85f, i == c.Saves.Count - 1 ? 0.1f : 0.35f));
                line.rectTransform.At(new Vector2(0, 0), new Vector2(0.5f, 0), new Vector2(10, -14), new Vector2(2, 106));
                LG.Ignore(line.gameObject);
                var dot = LGBuild.Panel(row, "Dot", on ? new Color(0.44f, 0.95f, 0.88f) : new Color(0.17f, 0.24f, 0.28f));
                dot.sprite = MenuArt.SoftDisc;
                dot.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(10, 0), new Vector2(on ? 26 : 16, on ? 26 : 16));
                LG.Ignore(dot.gameObject);

                var body = LGBuild.Panel(row, "Body", on ? new Color(0.06f, 0.20f, 0.22f, 0.9f) : new Color(1f, 1f, 1f, 0.03f), raycast: true);
                body.rectTransform.Stretch(36, 0, 0, 0);
                LG.Platter(body.gameObject, 16f).SetRim(on ? new Color(0.44f, 0.95f, 0.88f, 0.7f) : new Color(1f, 1f, 1f, 0.07f));
                string slot = e.Slot;
                body.gameObject.AddComponent<RowClick>().Init(() => { if (_selSlot != slot) { _selSlot = slot; _armedDelete = null; BuildTimeline(c, false); } },
                                                             () => SaveSystem.LoadSlot(slot));

                var date = LGBuild.Label(body.transform, e.Meta.GameDate, 26, Color.white, TextAnchor.MiddleLeft, bold: true);
                date.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(26, 0), new Vector2(200, 40));

                string kind = e.Meta.Auto ? Loc.T("Автосохранение") : Loc.T("Ручное сохранение");
                var t = LGBuild.Label(body.transform, kind, 17, Color.white, TextAnchor.LowerLeft, bold: true);
                t.rectTransform.Stretch(236, 48, 330, 14);
                var chip = LGBuild.Panel(body.transform, "Chip", e.Meta.Auto ? new Color(0.25f, 0.18f, 0.05f, 0.9f) : new Color(0.05f, 0.20f, 0.20f, 0.9f));
                chip.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(236 + t.preferredWidth + 12, -20), new Vector2(76, 22));
                var cc = e.Meta.Auto ? new Color(1f, 0.82f, 0.48f) : new Color(0.56f, 0.94f, 0.90f);
                LG.Chip(chip.gameObject, new Color(cc.r, cc.g, cc.b, 0.55f), 6f);
                var ct = LGBuild.Label(chip.transform, e.Meta.Auto ? Loc.T("АВТО") : Loc.T("РУЧНОЕ"), 11, cc, TextAnchor.MiddleCenter, bold: true);
                ct.rectTransform.Stretch();
                var w = LGBuild.Label(body.transform, Loc.F("{0}  ·  в игре {1}", SaveSystem.FormatSavedAt(e.SavedAtLocal), SaveSystem.FormatPlayTime(e.Meta.PlayTime)),
                                      13, new Color(0.56f, 0.66f, 0.71f), TextAnchor.UpperLeft);
                w.rectTransform.Stretch(236, 0, 330, 52);

                // Выбранное сохранение — яркая главная кнопка, остальные — обычные
                Transform load = on
                    ? MenuPrimaryButton.Create(body.transform, Loc.T("ЗАГРУЗИТЬ"), 16, () => SaveSystem.LoadSlot(slot)).transform
                    : LGBuild.Button(body.transform, "Load", UIManager.DS.BtnNeutral, new Color(1f, 1f, 1f, 0.3f),
                                     () => SaveSystem.LoadSlot(slot), null, Loc.T("ЗАГРУЗИТЬ"), 15).transform;
                ((RectTransform)load).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-84, 0), new Vector2(190, 50));

                var del = LGControls.IconButton(body.transform, LGIcon.Trash, 46, armed ? new Color(0.45f, 0.08f, 0.06f) : UIManager.DS.BtnNeutral,
                                                armed ? new Color(1f, 0.4f, 0.35f, 0.9f) : new Color(1f, 1f, 1f, 0.25f), () => OnDelete(slot, c));
                ((RectTransform)del.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-22, 0), new Vector2(46, 46));
                TooltipHelper.Attach(del.gameObject, armed ? Loc.T("Нажмите ещё раз — сохранение будет удалено") : Loc.T("Удалить сохранение"));
                if (armed)
                {
                    var warn = LGBuild.Label(body.transform, Loc.T("ещё раз — удалить"), 12, new Color(1f, 0.55f, 0.5f), TextAnchor.MiddleRight, bold: true);
                    warn.rectTransform.At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-284, 0), new Vector2(160, 20));
                }

                if (animate) MenuSlideIn.Add(row.gameObject, 0.18f + 0.06f * i, new Vector2(30f, 0f));
            }
        }

        private void OnDelete(string slot, Campaign c)
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
            _armedT = 3f;
            BuildTimeline(c, false);
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);
            _k = LGEase.Spring(_k, _shown ? 1f : 0f, ref _kv, 4.5f, 0.9f, dt);
            _cg.alpha = Mathf.Clamp01(_k);
            _cg.blocksRaycasts = _cg.interactable = _shown;
            if (!_shown && _k < 0.01f) { gameObject.SetActive(false); return; }

            // Подтверждение удаления гаснет через 3 секунды
            if (_armedDelete != null)
            {
                _armedT -= dt;
                if (_armedT <= 0f && _sel >= 0 && _sel < _camps.Count) { _armedDelete = null; BuildTimeline(_camps[_sel], false); }
            }
        }

        /// <summary>Клик по строке — выбрать, двойной клик — загрузить.</summary>
        private sealed class RowClick : MonoBehaviour, IPointerClickHandler
        {
            private System.Action _select, _load;
            public void Init(System.Action select, System.Action load) { _select = select; _load = load; }
            public void OnPointerClick(PointerEventData e)
            {
                if (e.button != PointerEventData.InputButton.Left) return;
                if (e.clickCount >= 2) _load?.Invoke();
                else _select?.Invoke();
            }
        }
    }

    /// <summary>Карточка партии: арт правителя (кадр по лицу), затемнение слева, герб, империя, число сохранений.</summary>
    public sealed class CampaignCard : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public bool Selected;
        public float AppearDelay;
        private CanvasGroup _cg;
        private RawImage _art;
        private LiquidGlassEffect _fx;
        private RectTransform _body;
        private Color _accent;
        private System.Action _onClick;
        private bool _over;
        private float _h, _hv, _s, _sv, _t;

        public static CampaignCard Create(RectTransform parent, SaveMeta m, int saves, float play, System.Action onClick)
        {
            var root = LGBuild.Rect(parent, "Campaign");
            LGBuild.Height(root.gameObject, 150f);
            var c = root.gameObject.AddComponent<CampaignCard>();
            c._onClick = onClick;
            c._cg = root.gameObject.AddComponent<CanvasGroup>();
            c._cg.alpha = 0f;
            var leader = LoadScreen.LeaderOf(m);
            c._accent = leader != null ? leader.Accent : UIManager.DS.NeonCyan;

            var body = LGBuild.Panel(root, "Body", new Color(0.03f, 0.06f, 0.10f, 0.95f), raycast: true);
            c._body = body.rectTransform;
            c._body.Stretch();
            c._fx = LG.Glass(body.gameObject, 18f);
            var mask = LG.RoundedMask(body.transform, 17f, 2f);
            if (leader != null && leader.Texture != null)
            {
                c._art = LGBuild.Rect(mask, "Art").gameObject.AddComponent<RawImage>();
                c._art.rectTransform.Stretch();
                c._art.texture = leader.Texture;
                c._art.raycastTarget = false;
                c._art.uvRect = LeaderPortraits.FaceUv(leader, 0.9f, 560f / 150f);
            }
            var fade = LGBuild.Panel(mask, "Fade", new Color(0.02f, 0.04f, 0.08f, 1f));
            fade.sprite = MenuArt.HorizontalFade;
            fade.rectTransform.anchorMin = Vector2.zero;
            fade.rectTransform.anchorMax = new Vector2(0.85f, 1);
            fade.rectTransform.offsetMin = fade.rectTransform.offsetMax = Vector2.zero;
            LG.Ignore(fade.gameObject);

            var emblem = FactionEmblems.ForKey(!string.IsNullOrEmpty(m.Emblem) ? m.Emblem : leader?.Key);
            float x = 26f;
            if (emblem != null)
            {
                var em = LGBuild.Panel(body.transform, "Emblem", Color.white);
                em.sprite = emblem;
                em.preserveAspect = true;
                em.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(22, 0), new Vector2(78, 78));
                LG.Ignore(em.gameObject);
                x = 118f;
            }
            var name = LGBuild.Label(body.transform, m.Empire, 24, Color.white, TextAnchor.LowerLeft, bold: true);
            name.rectTransform.Stretch(x, 76, 20, 0);
            name.rectTransform.offsetMax = new Vector2(-20, -30);
            var sh = name.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, 0.8f);
            var sub = LGBuild.Label(body.transform, Loc.F("{0} {1}  ·  {2}", saves, SavesWord(saves), SaveSystem.FormatPlayTime(play)),
                                    14, new Color(0.70f, 0.80f, 0.84f), TextAnchor.UpperLeft);
            sub.rectTransform.Stretch(x, 0, 20, 80);
            return c;
        }

        private static string SavesWord(int n)
        {
            int a = n % 100, b = n % 10;
            if (a >= 11 && a <= 14) return Loc.T("сохранений");
            if (b == 1) return Loc.T("сохранение");
            if (b >= 2 && b <= 4) return Loc.T("сохранения");
            return Loc.T("сохранений");
        }

        public void OnPointerEnter(PointerEventData e)
        {
            _over = true;
            if (SFXManager.Has("ui_hover")) SFXManager.Play("ui_hover", 0.3f, 1.1f);
        }

        public void OnPointerExit(PointerEventData e) => _over = false;

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left) _onClick?.Invoke();
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);
            _t += dt;
            float k = LGEase.OutCubic(Mathf.Clamp01((_t - AppearDelay) / 0.5f));
            _cg.alpha = k;
            _body.anchoredPosition = new Vector2(-40f * (1f - k), 0f);
            _h = LGEase.Spring(_h, _over ? 1f : 0f, ref _hv, 4f, 0.7f, dt);
            _s = LGEase.Spring(_s, Selected ? 1f : 0f, ref _sv, 4f, 0.8f, dt);
            float h = Mathf.Clamp01(_h), s = Mathf.Clamp01(_s);
            float pulse = s * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2f));
            if (_art != null)
            {
                float b = Mathf.Lerp(0.5f, 1f, Mathf.Max(s, h * 0.8f));
                _art.color = new Color(b, b, b, 1f);
            }
            _fx.SetRim(new Color(_accent.r, _accent.g, _accent.b, Mathf.Lerp(0.12f, 0.9f, Mathf.Max(s, h * 0.6f)) - 0.12f * pulse));
            _fx.SetHover(Mathf.Max(h * 0.6f, s * 0.3f));
            float sc = 1f + 0.012f * h;
            _body.localScale = new Vector3(sc, sc, 1f);
        }
    }
}
