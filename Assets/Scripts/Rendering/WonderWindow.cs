using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Окно «Чудо фракции» (левое меню, клавиша U):
    ///   • слева — сцена: иллюстрация чуда (Resources/UI/Events/Wonder_*.jpg), поверх — живая голограмма модели
    ///     (готовые части металлом, недостроенное — чертежом); без иллюстрации голограмма занимает всю сцену;
    ///   • справа — место стройки (выбор из подходящих систем, пока не заложен первый этап), три этапа с ценой,
    ///     сроком и эффектом, кнопка «Заложить этап», скидки от цепочек событий, Сеть Врат (у Оракула)
    ///     и чудеса встреченных соперников.
    /// Правая колонка пересобирается, только когда меняется её содержимое; прогресс обновляется на месте.
    /// </summary>
    public class WonderWindow : MonoBehaviour
    {
        public static WonderWindow Instance { get; private set; }
        public bool IsOpen => _root != null && LG.IsVisible(_root);

        private GameObject _root;
        private Text _title, _subtitle, _name, _tagline, _lore;
        private RawImage _art, _holo, _holoFull;
        private RectTransform _holoFrame, _right, _sceneShade;
        private Image _sceneFrame;
        private readonly List<(RectTransform bar, Text label)> _progress = new List<(RectTransform, Text)>();
        private string _sig;
        private float _refresh;
        private int _candidate = -1;

        private static Color Muted => UIManager.DS.TextMuted;
        private static Color Gold => UIManager.DS.Gold;
        private static readonly Color CPanel = new Color(0.020f, 0.045f, 0.065f, 0.92f);
        private static readonly Color CEnergy = new Color(1f, 0.85f, 0.3f), CMinerals = new Color(0.55f, 0.85f, 1f);
        private static readonly Color CAlloys = new Color(0.85f, 0.7f, 1f), CInfluence = new Color(0.75f, 0.6f, 1f);

        public static void Toggle() => Toggle(null);

        /// <summary>Открыть/закрыть; preferred — система, предложенная как место стройки (если подходит).</summary>
        public static void Toggle(StarSystem preferred)
        {
            if (Instance == null) new GameObject("[UI] WonderWindow").AddComponent<WonderWindow>();
            if (Instance.IsOpen) Instance.Close();
            else Instance.Open(preferred);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Update()
        {
            if (!IsOpen) { WonderStudio.Instance?.Hide(); return; }
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            if ((_refresh -= Time.unscaledDeltaTime) <= 0f) { _refresh = 0.5f; Refresh(); }
        }

        public void Open(StarSystem preferred = null)
        {
            if (WonderManager.Instance == null) return;
            if (_root == null) Build();
            if (_root == null) return;
            var def = WonderManager.DefOf(0);
            if (preferred != null && def != null && def.SiteOk(preferred, 0)) _candidate = preferred.Id;
            _sig = null;
            Refresh();
            LG.Show(_root);
            _root.transform.SetAsLastSibling();
            UIManager.Instance?.ShowModalDimPublic();
            SFXManager.Play(Core.Audio.Sfx.WindowOpen);
        }

        public void Close()
        {
            if (_root == null) return;
            LG.Hide(_root);
            WonderStudio.Instance?.Hide();
            UIManager.Instance?.HideModalDimPublic();
            TooltipSystem.Hide();
            SFXManager.Play(Core.Audio.Sfx.WindowClose);
        }

        // ================================================================ Построение

        private void Build()
        {
            var host = GameObject.Find("ModalCanvas");
            if (host == null) return;
            var rt = LGBuild.Rect(host.transform, "WonderWindow");
            rt.Stretch(28, 28, 28, 28);
            _root = rt.gameObject;
            _root.AddComponent<CanvasGroup>();
            var bg = _root.AddComponent<Image>();
            bg.color = new Color(0.010f, 0.025f, 0.038f, 0.97f);
            bg.raycastTarget = true;
            LG.Glass(_root).SetRim(new Color(1f, 0.8f, 0.4f, 0.42f));
            LG.Motion(_root, LGAppear.Kind.Pop).fromScale = 0.97f;

            // Шапка
            var icon = LGIcons.Create(rt, LGIcon.Wonder, 26, Gold);
            icon.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -24), new Vector2(26, 26));
            _title = LGBuild.Label(rt, "ЧУДО ФРАКЦИИ", 28, Color.white, TextAnchor.UpperLeft, bold: true);
            _title.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(64, -18), new Vector2(700, 36));
            _subtitle = LGBuild.Label(rt, "", 12, Muted, TextAnchor.UpperLeft);
            _subtitle.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(66, -56), new Vector2(1100, 18));

            var close = LGBuild.Button(rt, "Close", UIManager.DS.BtnNeutral, new Color(1f, 1f, 1f, 0.25f), Close, LGIcon.Close, null, 12);
            ((RectTransform)close.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-20, -18), new Vector2(38, 38));
            TooltipHelper.Attach(close.gameObject, "Закрыть  <color=#8AA2A8>(Esc / U)</color>");

            // ---- Сцена слева ----
            var scene = LGBuild.Panel(rt, "Scene", new Color(0.004f, 0.01f, 0.018f, 1f));
            _sceneFrame = scene;
            scene.rectTransform.anchorMin = new Vector2(0, 0);
            scene.rectTransform.anchorMax = new Vector2(0.56f, 1);
            scene.rectTransform.offsetMin = new Vector2(24, 24);
            scene.rectTransform.offsetMax = new Vector2(-10, -86);
            LG.Platter(scene.gameObject, 18f).SetRim(new Color(1f, 0.8f, 0.4f, 0.35f));
            var sceneMask = LG.RoundedMask(scene.transform, 18f, 1.5f);

            _art = LGBuild.Rect(sceneMask, "Art").gameObject.AddComponent<RawImage>();
            _art.rectTransform.Stretch();
            _art.raycastTarget = false;
            var fitter = _art.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = 16f / 9f;

            // Голограмма во всю сцену (когда иллюстрации нет)
            _holoFull = LGBuild.Rect(sceneMask, "HoloFull").gameObject.AddComponent<RawImage>();
            _holoFull.rectTransform.Stretch(0, 120, 0, 0);
            _holoFull.raycastTarget = false;
            var fullFit = _holoFull.gameObject.AddComponent<AspectRatioFitter>();
            fullFit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fullFit.aspectRatio = 4f / 3f;

            // Затемнение снизу под название и предание
            var shade = LGBuild.Panel(sceneMask, "Shade", new Color(0.004f, 0.01f, 0.018f, 0.96f));
            shade.sprite = MenuArt.VerticalFade;
            _sceneShade = shade.rectTransform;
            _sceneShade.anchorMin = Vector2.zero;
            _sceneShade.anchorMax = new Vector2(1, 0.48f);
            _sceneShade.offsetMin = _sceneShade.offsetMax = Vector2.zero;

            _tagline = LGBuild.Label(sceneMask, "", 11, Gold, TextAnchor.LowerLeft, bold: true);
            _tagline.rectTransform.anchorMin = new Vector2(0, 0); _tagline.rectTransform.anchorMax = new Vector2(1, 0);
            _tagline.rectTransform.pivot = new Vector2(0, 0);
            _tagline.rectTransform.offsetMin = new Vector2(28, 172); _tagline.rectTransform.offsetMax = new Vector2(-28, 190);
            _tagline.Track(2.2f);
            _name = LGBuild.Label(sceneMask, "", 34, Color.white, TextAnchor.LowerLeft, bold: true);
            _name.rectTransform.anchorMin = new Vector2(0, 0); _name.rectTransform.anchorMax = new Vector2(1, 0);
            _name.rectTransform.pivot = new Vector2(0, 0);
            _name.rectTransform.offsetMin = new Vector2(26, 126); _name.rectTransform.offsetMax = new Vector2(-28, 172);
            _lore = LGBuild.Label(sceneMask, "", 12, new Color(0.82f, 0.88f, 0.92f), TextAnchor.UpperLeft, wrap: true);
            _lore.rectTransform.anchorMin = new Vector2(0, 0); _lore.rectTransform.anchorMax = new Vector2(1, 0);
            _lore.rectTransform.pivot = new Vector2(0, 0);
            _lore.rectTransform.offsetMin = new Vector2(28, 18); _lore.rectTransform.offsetMax = new Vector2(-28, 122);
            _lore.lineSpacing = 1.12f;

            // Голограмма-вставка в правом верхнем углу сцены (поверх иллюстрации)
            var hf = LGBuild.Panel(scene.transform, "HoloInset", new Color(0.004f, 0.016f, 0.026f, 0.78f));
            _holoFrame = hf.rectTransform;
            _holoFrame.At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-16, -16), new Vector2(300, 225));
            LG.Platter(hf.gameObject, 14f).SetRim(new Color(1f, 0.8f, 0.4f, 0.4f));
            var holoMask = LG.RoundedMask(hf.transform, 14f, 1.5f);
            _holo = LGBuild.Rect(holoMask, "Holo").gameObject.AddComponent<RawImage>();
            _holo.rectTransform.Stretch();
            _holo.raycastTarget = false;
            var hl = LGBuild.Label(hf.transform, "ГОЛОГРАММА СТРОЙКИ", 9, Gold, TextAnchor.UpperLeft, bold: true);
            hl.rectTransform.Stretch(12, 0, 0, 8);
            hl.Track(1.8f);

            // ---- Правая колонка ----
            var right = LGBuild.Rect(rt, "Right");
            right.anchorMin = new Vector2(0.56f, 0);
            right.anchorMax = new Vector2(1, 1);
            right.offsetMin = new Vector2(10, 24);
            right.offsetMax = new Vector2(-24, -86);
            _right = LGBuild.ScrollList(right, 10f, 2);
        }

        // ================================================================ Обновление

        private void Refresh()
        {
            var wm = WonderManager.Instance;
            var def = WonderManager.DefOf(0);
            var st = wm != null ? wm.StateOf(0) : null;
            if (def == null || st == null) return;

            // Место: закреплённое, иначе выбранный кандидат, иначе лучшее
            var sites = Candidates(def);
            if (st.SystemId < 0)
            {
                if (_candidate < 0 || !sites.Contains(_candidate))
                    _candidate = sites.Count > 0 ? WonderManager.PickSite(0, def, sites.Contains(EconomyManager.PlayerCapitalId) ? EconomyManager.PlayerCapitalId : sites[0]) : -1;
            }
            int siteId = st.SystemId >= 0 ? st.SystemId : _candidate;
            var site = EmpireStats.GetSystem(siteId);

            _title.text = "ЧУДО ФРАКЦИИ";
            _subtitle.text = $"<color={LGBuild.Hex(def.Color)}>{def.Name}</color>  ·  {def.Title.ToLower()}  ·  этапов завершено: {st.Built} из 3" +
                             (st.InProgress ? $"  ·  строится этап {Roman(st.Built + 1)}" : "");
            _name.text = def.Name.ToUpper();
            _tagline.text = def.Title;
            _tagline.color = def.Color;
            _lore.text = def.Lore;

            // Сцена: иллюстрация (если есть) + голограмма
            var tex = LoadArt(def.Art);
            _art.texture = tex;
            _art.enabled = tex != null;
            if (tex != null) _art.GetComponent<AspectRatioFitter>().aspectRatio = tex.width / (float)tex.height;
            var studio = WonderStudio.Ensure();
            var spec = new WonderModels.Spec
            {
                Key = def.Key, Built = st.Built, InProgress = st.InProgress, Progress = wm.Progress(st),
                StarMul = site != null ? StarFx.SizeMul(site.SpectralClass) : 1f, Blueprint = true
            };
            studio.Show(spec, site != null ? GalaxyView.StarColor(site.SpectralClass) : new Color(1f, 0.85f, 0.6f));
            _holo.texture = studio.Target;
            _holoFull.texture = studio.Target;
            _holoFrame.gameObject.SetActive(tex != null);
            _holoFull.enabled = tex == null;

            string sig = Signature(def, st, siteId, sites.Count);
            if (sig != _sig)
            {
                _sig = sig;
                RebuildRight(def, st, site, sites);
            }
            UpdateProgress(st);
        }

        private static List<int> Candidates(WonderDef def)
        {
            var list = new List<int>();
            foreach (var s in EmpireStats.Systems) if (def.SiteOk(s, 0)) list.Add(s.Id);
            return list;
        }

        private string Signature(WonderDef def, WonderSave st, int siteId, int siteCount)
        {
            var sb = new StringBuilder();
            sb.Append(def.Key).Append('|').Append(st.Built).Append(st.InProgress).Append('|').Append(siteId).Append('|').Append(siteCount);
            sb.Append('|').Append(EmpireStats.TechCount(0)).Append('|').Append(st.GateCooldown > 0);
            sb.Append('|').Append(EventDatabase.HasChainFlag("wonder_key")).Append(EventDatabase.HasChainFlag("wonder_mind"));
            var wm = WonderManager.Instance;
            sb.Append('|').Append(wm.CanStart(0, siteId, out string why)).Append(why);
            if (wm.GateOpen(0)) sb.Append('|').Append(wm.FleetsAtGate()).Append(EmpireStats.SystemCount(0));
            foreach (var ai in AIEmpireManager.All)
            {
                var r = wm.StateOf(ai.OwnerId);
                sb.Append('|').Append(Contacts.PlayerMet(ai.OwnerId)).Append(r != null ? r.Built : -1).Append(r != null && r.InProgress);
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------- Правая колонка

        private void RebuildRight(WonderDef def, WonderSave st, StarSystem site, List<int> sites)
        {
            LGBuild.Clear(_right);
            _progress.Clear();
            var wm = WonderManager.Instance;

            BuildSite(def, st, site, sites);
            for (int i = 0; i < def.Stages.Length; i++) BuildStage(def, st, i, site);
            BuildBonuses();
            if (wm.GateOpen(0)) BuildGate(st);
            BuildRivals();
        }

        private RectTransform Card(string name, float height, Color rim)
        {
            var card = LGBuild.Panel(_right, name, CPanel);
            LGBuild.Height(card.gameObject, height);
            LG.Platter(card.gameObject, 16f).SetRim(rim);
            return card.rectTransform;
        }

        private static Text Caption(RectTransform parent, string text, Color col, float top = 12f)
        {
            var t = LGBuild.Label(parent, text, 10, col, TextAnchor.UpperLeft, bold: true);
            t.rectTransform.Stretch(18, 0, 18, top);
            t.Track(1.8f);
            return t;
        }

        private void BuildSite(WonderDef def, WonderSave st, StarSystem site, List<int> sites)
        {
            bool locked = st.SystemId >= 0;
            var card = Card("Site", 104, new Color(def.Color.r, def.Color.g, def.Color.b, 0.35f));
            Caption(card, locked ? "МЕСТО СТРОЙКИ" : "МЕСТО СТРОЙКИ  ·  ВЫБОР", Muted);

            string name = site != null ? site.Name : "нет подходящей системы";
            string star = site != null ? StarClassName(site.SpectralClass) : "";
            string extra = "";
            if (site != null && def.SiteYield != null)
            {
                float y = locked ? st.Yield : def.SiteYield(site);
                extra = $"  ·  отдача <color={(y >= 1f ? "#7CF29A" : "#FF9A8A")}>×{y:0.##}</color>";
            }
            var t = LGBuild.Label(card, $"<b>{name}</b>   <color=#8AA2A8>{star}{extra}</color>", 16, Color.white, TextAnchor.UpperLeft);
            t.rectTransform.Stretch(18, 0, 120, 32);
            var rule = LGBuild.Label(card, (locked ? "Место закреплено после закладки первого этапа. " : "") + def.Site + ".",
                                     11, Muted, TextAnchor.UpperLeft, wrap: true);
            rule.rectTransform.Stretch(18, 8, 120, 60);

            if (!locked && sites.Count > 1)
            {
                var prev = LGBuild.Button(card, "Prev", UIManager.DS.BtnNeutral, new Color(1f, 0.8f, 0.4f, 0.4f), () => Cycle(sites, -1), LGIcon.Back, null, 12);
                ((RectTransform)prev.transform).At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-64, 0), new Vector2(40, 40));
                var next = LGBuild.Button(card, "Next", UIManager.DS.BtnNeutral, new Color(1f, 0.8f, 0.4f, 0.4f), () => Cycle(sites, 1), LGIcon.Back, null, 12);
                var nrt = (RectTransform)next.transform;
                nrt.At(new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-18, 0), new Vector2(40, 40));
                foreach (Transform c in nrt) c.localRotation = Quaternion.Euler(0, 0, 180f);
                TooltipHelper.Attach(prev.gameObject, $"Предыдущая подходящая система ({sites.Count})");
                TooltipHelper.Attach(next.gameObject, $"Следующая подходящая система ({sites.Count})");
            }
        }

        private void Cycle(List<int> sites, int dir)
        {
            if (sites.Count == 0) return;
            int i = Mathf.Max(0, sites.IndexOf(_candidate));
            _candidate = sites[(i + dir + sites.Count) % sites.Count];
            SFXManager.Play(Core.Audio.Sfx.UiTab);
            _sig = null;
            Refresh();
        }

        private void BuildStage(WonderDef def, WonderSave st, int i, StarSystem site)
        {
            var stage = def.Stages[i];
            bool done = i < st.Built, building = i == st.Built && st.InProgress, next = i == st.Built && !st.InProgress;
            Color rim = done ? new Color(0.45f, 1f, 0.6f, 0.4f) : building ? new Color(def.Color.r, def.Color.g, def.Color.b, 0.6f)
                      : next ? new Color(1f, 0.8f, 0.4f, 0.45f) : new Color(1f, 1f, 1f, 0.12f);
            var card = Card("Stage" + i, building || next ? 178 : 132, rim);

            // Номер этапа
            var badge = LGBuild.Panel(card, "Badge", done ? new Color(0.1f, 0.3f, 0.16f, 1f) : new Color(0.06f, 0.1f, 0.14f, 1f));
            badge.rectTransform.At(new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -14), new Vector2(40, 40));
            LG.Chip(badge.gameObject, rim);
            if (done)
            {
                var ic = LGIcons.Create(badge.transform, LGIcon.Check, 18, new Color(0.5f, 1f, 0.65f));
                ic.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18, 18));
            }
            else LGBuild.Label(badge.transform, Roman(i + 1), 15, building ? def.Color : Color.white, TextAnchor.MiddleCenter, bold: true);

            var title = LGBuild.Label(card, $"<b>{stage.Name.ToUpper()}</b>", 14, done ? new Color(0.7f, 1f, 0.78f) : Color.white, TextAnchor.UpperLeft);
            title.rectTransform.Stretch(70, 0, 160, 14);
            title.Track(0.8f);
            string state = done ? "<color=#7CF29A>ЗАВЕРШЁН</color>" : building ? $"<color={LGBuild.Hex(def.Color)}>СТРОИТСЯ</color>"
                         : next ? "<color=#F2C747>СЛЕДУЮЩИЙ</color>" : "<color=#6F8790>ВПЕРЕДИ</color>";
            var stLbl = LGBuild.Label(card, state, 10, Color.white, TextAnchor.UpperRight, bold: true);
            stLbl.rectTransform.Stretch(0, 0, 18, 16);
            stLbl.Track(1.6f);

            var desc = LGBuild.Label(card, stage.Desc, 11, Muted, TextAnchor.UpperLeft, wrap: true);
            desc.rectTransform.Stretch(70, 0, 18, 36);
            var eff = LGBuild.Label(card, $"<color=#F2C747>▸</color> {stage.Effect}", 12, new Color(1f, 0.9f, 0.62f), TextAnchor.UpperLeft, wrap: true);
            eff.rectTransform.Stretch(70, 0, 18, 70);

            if (done) return;

            // Цена и срок
            WonderManager.Cost(stage, 0, out float e, out float m, out float a, out float inf);
            var eco = EconomyManager.Instance;
            var row = LGBuild.Rect(card, "Cost");
            row.anchorMin = new Vector2(0, 1); row.anchorMax = new Vector2(1, 1);
            row.pivot = new Vector2(0, 1);
            row.offsetMin = new Vector2(70, -128); row.offsetMax = new Vector2(-18, -104);
            var hlg = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 14; hlg.childControlWidth = true; hlg.childControlHeight = true; hlg.childForceExpandWidth = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            void Res(LGIcon ic, Color c, float need, float have)
            {
                if (need <= 0f) return;
                bool ok = have >= need;
                LGIcons.IconLabel(row, ic, $"{need:0}", 12, c, ok ? Color.white : new Color(1f, 0.5f, 0.45f), 15);
            }
            Res(LGIcon.Alloys, CAlloys, a, eco != null ? eco.Alloys : 0f);
            Res(LGIcon.Minerals, CMinerals, m, eco != null ? eco.Minerals : 0f);
            Res(LGIcon.Energy, CEnergy, e, eco != null ? eco.EnergyCredits : 0f);
            Res(LGIcon.Influence, CInfluence, inf, eco != null ? eco.Influence : 0f);
            int techs = EmpireStats.TechCount(0);
            LGIcons.IconLabel(row, LGIcon.Clock, WonderManager.FormatDays(WonderManager.StageDays(stage, 0)), 12, Muted, Color.white, 14);
            LGIcons.IconLabel(row, LGIcon.Research, $"{stage.Techs} техн.", 12, techs >= stage.Techs ? new Color(0.5f, 0.9f, 1f) : new Color(1f, 0.5f, 0.45f),
                              techs >= stage.Techs ? Color.white : new Color(1f, 0.5f, 0.45f), 14);

            if (!building && !next) return;

            if (building)
            {
                var barHost = LGBuild.Rect(card, "Progress");
                barHost.anchorMin = new Vector2(0, 0); barHost.anchorMax = new Vector2(1, 0);
                barHost.pivot = new Vector2(0.5f, 0);
                barHost.offsetMin = new Vector2(70, 18); barHost.offsetMax = new Vector2(-150, 40);
                var fill = LGBuild.Bar(barHost, def.Color, WonderManager.Instance.Progress(st), 8f);
                var lbl = LGBuild.Label(card, "", 11, Color.white, TextAnchor.LowerLeft, bold: true);
                lbl.rectTransform.anchorMin = new Vector2(0, 0); lbl.rectTransform.anchorMax = new Vector2(1, 0);
                lbl.rectTransform.pivot = new Vector2(0, 0);
                lbl.rectTransform.offsetMin = new Vector2(70, 34); lbl.rectTransform.offsetMax = new Vector2(-150, 52);
                _progress.Add((fill, lbl));

                var cancel = LGBuild.Button(card, "Cancel", UIManager.DS.BtnNeutral, new Color(1f, 0.45f, 0.45f, 0.45f), () =>
                {
                    WonderManager.Instance.CancelStage(0);
                    SFXManager.Play(Core.Audio.Sfx.UiBack);
                    _sig = null; Refresh();
                }, null, "ОТМЕНИТЬ", 10);
                ((RectTransform)cancel.transform).At(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-18, 16), new Vector2(116, 32));
                TooltipHelper.Attach(cancel.gameObject, "Отменить этап: вернётся половина ресурсов." + (st.Built == 0 ? " Место стройки освободится." : ""));
                return;
            }

            // Следующий этап — кнопка закладки или причина, почему нельзя
            int siteId = st.SystemId >= 0 ? st.SystemId : _candidate;
            bool can = WonderManager.Instance.CanStart(0, siteId, out string why);
            var go = LGBuild.Button(card, "Start", can ? UIManager.DS.BtnSuccess : UIManager.DS.BtnDisabled,
                can ? new Color(0.5f, 1f, 0.65f, 0.6f) : new Color(1f, 1f, 1f, 0.15f), () =>
                {
                    if (!WonderManager.Instance.StartStage(0, siteId)) { SFXManager.Play(Core.Audio.Sfx.UiDenied); return; }
                    SFXManager.Play(Core.Audio.Sfx.UiConfirm);
                    _sig = null; Refresh();
                }, null, st.Built == 0 ? "ЗАЛОЖИТЬ ЧУДО" : "ЗАЛОЖИТЬ ЭТАП", 11);
            ((RectTransform)go.transform).At(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-18, 16), new Vector2(170, 34));
            go.interactable = can;
            if (!can)
            {
                var reason = LGBuild.Label(card, $"<color=#FF9A8A>{why}</color>", 11, Color.white, TextAnchor.LowerLeft, wrap: true);
                reason.rectTransform.anchorMin = new Vector2(0, 0); reason.rectTransform.anchorMax = new Vector2(1, 0);
                reason.rectTransform.pivot = new Vector2(0, 0);
                reason.rectTransform.offsetMin = new Vector2(70, 16); reason.rectTransform.offsetMax = new Vector2(-200, 52);
            }
        }

        private void UpdateProgress(WonderSave st)
        {
            if (_progress.Count == 0) return;
            float p = WonderManager.Instance.Progress(st);
            var sys = EmpireStats.GetSystem(st.SystemId);
            bool paused = sys == null || sys.OwnerId != 0;
            foreach (var (bar, label) in _progress)
            {
                LGBuild.SetBar(bar, p);
                if (label != null)
                    label.text = paused
                        ? "<color=#FF9A8A>ОСТАНОВЛЕНО — система потеряна</color>"
                        : $"{p * 100f:0}%   <color=#8AA2A8>осталось {WonderManager.FormatDays(st.DaysLeft)}</color>";
            }
        }

        private void BuildBonuses()
        {
            bool key = EventDatabase.HasChainFlag("wonder_key"), mind = EventDatabase.HasChainFlag("wonder_mind");
            var card = Card("Bonuses", key || mind ? 84 : 64, new Color(0.85f, 0.65f, 0.95f, 0.3f));
            Caption(card, "УСКОРЕНИЕ СТРОЙКИ", Muted);
            string text = key || mind
                ? (key ? "<color=#E3B8FF>Ключ Строителей</color> — этапы на 25% дешевле и быстрее.   " : "") +
                  (mind ? "<color=#E3B8FF>Архитектура Тессерама</color> — этапы на 20% быстрее." : "")
                : "Пока нет. Цепочки событий «Сердце Тишины» и «Пробуждённый разум» могут дать скидку на стройку.";
            var t = LGBuild.Label(card, text, 11, new Color(0.85f, 0.9f, 0.94f), TextAnchor.UpperLeft, wrap: true);
            t.rectTransform.Stretch(18, 8, 18, 32);
        }

        private void BuildGate(WonderSave st)
        {
            var wm = WonderManager.Instance;
            var gateSys = EmpireStats.GetSystem(st.SystemId);
            var dests = new List<StarSystem>();
            foreach (var s in EmpireStats.Systems) if (s.OwnerId == 0 && s.Id != st.SystemId && s.HasStarbase) dests.Add(s);
            dests.Sort((x, y) => string.CompareOrdinal(x.Name, y.Name));
            int rows = Mathf.CeilToInt(dests.Count / 3f);
            var card = Card("Gate", 96 + rows * 40, new Color(0.72f, 0.5f, 1f, 0.5f));
            Caption(card, "СЕТЬ ВРАТ", new Color(0.8f, 0.65f, 1f));
            int fleets = wm.FleetsAtGate();
            string status = st.GateCooldown > 0
                ? $"Врата перезаряжаются: ещё {st.GateCooldown} дн."
                : fleets > 0 ? $"У Врат ({gateSys?.Name}) стоят флотов: <b>{fleets}</b>. Выберите систему — все они перейдут туда мгновенно."
                             : $"У Врат ({gateSys?.Name}) нет флотов. Приведите флот в систему Врат, чтобы перебросить его.";
            var t = LGBuild.Label(card, status, 11, new Color(0.85f, 0.9f, 0.94f), TextAnchor.UpperLeft, wrap: true);
            t.rectTransform.Stretch(18, 0, 18, 32);

            var grid = LGBuild.Rect(card, "Dests");
            grid.Stretch(18, 14, 18, 76);
            var g = grid.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(150, 32);
            g.spacing = new Vector2(8, 8);
            bool ready = st.GateCooldown <= 0 && fleets > 0;
            foreach (var d in dests)
            {
                int id = d.Id;
                var b = LGBuild.Button(grid, "Dest", ready ? UIManager.DS.BtnNeutral : UIManager.DS.BtnDisabled, new Color(0.72f, 0.5f, 1f, 0.5f), () =>
                {
                    if (WonderManager.Instance.GateJump(id) > 0) { _sig = null; Refresh(); }
                    else SFXManager.Play(Core.Audio.Sfx.UiDenied);
                }, null, d.Name, 10);
                b.interactable = ready;
            }
        }

        private void BuildRivals()
        {
            var wm = WonderManager.Instance;
            int n = 0;
            foreach (var ai in AIEmpireManager.All) if (!ai.IsEliminated) n++;
            var card = Card("Rivals", 50 + n * 46, new Color(1f, 1f, 1f, 0.15f));
            Caption(card, "ЧУДЕСА СОПЕРНИКОВ", Muted);
            float y = -40f;
            foreach (var ai in AIEmpireManager.All)
            {
                if (ai.IsEliminated) continue;
                bool met = Contacts.PlayerMet(ai.OwnerId);
                var st = wm.StateOf(ai.OwnerId);
                var def = WonderManager.DefOf(ai.OwnerId);
                var row = LGBuild.Rect(card, "Rival");
                row.anchorMin = new Vector2(0, 1); row.anchorMax = new Vector2(1, 1);
                row.pivot = new Vector2(0.5f, 1);
                row.offsetMin = new Vector2(18, y - 40); row.offsetMax = new Vector2(-18, y);
                y -= 46f;

                Color ac = ai.MapColor;
                var crest = FactionEmblems.Create(row, met ? FactionEmblems.ForOwner(ai.OwnerId) : null, 30, ac);
                if (crest != null) crest.rectTransform.At(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(30, 30));
                string who = met ? $"<b>{ai.AIName}</b>" : "<color=#6F8790>Неизвестная империя</color>";
                string what = met && def != null ? $"<color=#8AA2A8>{def.Name}</color>" : "<color=#6F8790>—</color>";
                var name = LGBuild.Label(row, $"{who}\n{what}", 11, Color.white, TextAnchor.MiddleLeft);
                name.rectTransform.Stretch(40, 0, 180, 0);

                string prog = !met || st == null ? "?" : st.Built >= 3 ? "<color=#F2C747>ЗАВЕРШЕНО</color>"
                            : st.InProgress ? $"этап {st.Built + 1} из 3 · {wm.Progress(st) * 100f:0}%"
                            : st.Built > 0 ? $"этапов: {st.Built} из 3" : "не начато";
                var p = LGBuild.Label(row, prog, 11, Color.white, TextAnchor.MiddleRight, bold: true);
                p.rectTransform.Stretch(0, 0, 0, 0);
            }
        }

        // ---------------------------------------------------------------- Помощники

        private static readonly Dictionary<string, Texture2D> s_art = new Dictionary<string, Texture2D>();

        /// <summary>Иллюстрация чуда: «Своя|Запасная» из Resources/UI/Events — берётся только своя (запасная — для окна события).</summary>
        private static Texture2D LoadArt(string art)
        {
            if (string.IsNullOrEmpty(art)) return null;
            string own = art.Split('|')[0].Trim();
            if (s_art.TryGetValue(own, out var t) && t != null) return t;
            t = Resources.Load<Texture2D>("UI/Events/" + own);
            if (t != null) s_art[own] = t;
            return t;
        }

        private static string Roman(int n) => n switch { 1 => "I", 2 => "II", 3 => "III", _ => n.ToString() };

        private static string StarClassName(StarSpectralClass c) => c switch
        {
            StarSpectralClass.ClassM => "красный карлик (M)",
            StarSpectralClass.ClassK => "оранжевая звезда (K)",
            StarSpectralClass.ClassG => "жёлтая звезда (G)",
            StarSpectralClass.ClassA => "белая звезда (A)",
            StarSpectralClass.ClassB => "голубой гигант (B)",
            _ => "чёрная дыра"
        };
    }
}
