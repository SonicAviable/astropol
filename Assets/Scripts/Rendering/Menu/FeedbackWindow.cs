using UnityEngine;
using UnityEngine.UI;
using StellarisClone.Core;

namespace StellarisClone.Rendering
{
    /// <summary>
    /// Окно «Обратная связь» поверх главного меню или паузы: тип (баг / идея / другое), текст, необязательный
    /// контакт для ответа и галочка «приложить сведения о системе и журнал». Письмо уходит разработчику через
    /// <see cref="FeedbackSender"/>; если отправить не вышло — кнопка открывает почтовую программу с готовым письмом.
    /// Esc закрывает окно (черновик сохраняется до закрытия игры).
    /// </summary>
    public sealed class FeedbackWindow : MonoBehaviour
    {
        private const float MaxW = 820f, MaxH = 760f, Pad = 44f;
        private const int MinChars = 10, MaxChars = 3000;

        // Черновик переживает закрытие окна — случайный Esc не стирает длинное описание бага
        private static string s_draft = "", s_contact = "";
        private static int s_category;
        private static bool s_attach = true;
        private static float s_lastSent = -999f;

        private RectTransform _panel, _root;
        private CanvasGroup _cg;
        private InputField _message, _contact;
        private Text _status, _counter;
        private MenuPrimaryButton _send;
        private Button _mail;
        private float _k, _kv;
        private bool _closing, _sending, _failed, _sentOk;
        private string _lastText = "";

        public static bool IsOpen { get; private set; }

        public static FeedbackWindow Open(RectTransform root)
        {
            if (IsOpen) return null;
            var rt = LGBuild.Rect(root, "Feedback");
            rt.Stretch();
            rt.SetAsLastSibling();
            var w = rt.gameObject.AddComponent<FeedbackWindow>();
            w._root = root;
            w._lastText = s_draft;
            w.Build(rt);
            IsOpen = true;
            return w;
        }

        private void OnDestroy() => IsOpen = false;

        private void Build(RectTransform rt)
        {
            _cg = rt.gameObject.AddComponent<CanvasGroup>();
            _cg.alpha = 0f;
            var dim = LGBuild.Panel(rt, "Dim", new Color(0.005f, 0.012f, 0.022f, 0.6f), raycast: true);
            dim.rectTransform.Stretch();
            LG.Ignore(dim.gameObject);

            var panel = LGBuild.Panel(rt, "Panel", new Color(0.03f, 0.06f, 0.10f, 0.95f), raycast: true);
            _panel = panel.rectTransform;
            _panel.At(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(MaxW, MaxH));
            LG.Glass(panel.gameObject, 24f).SetRim(new Color(0.45f, 0.95f, 0.9f, 0.3f));
            MenuPanelFx.Sheen(_panel);
            MenuSlideIn.Add(panel.gameObject, 0.02f, new Vector2(0f, -30f));

            var cap = LGBuild.Label(_panel, Loc.T("ОБРАТНАЯ СВЯЗЬ"), 13, UIManager.DS.NeonCyan, TextAnchor.UpperLeft, bold: true).Track(3f);
            cap.rectTransform.TopBand(36, 18, Pad, Pad);
            var title = LGBuild.Label(_panel, Loc.T("Нашли баг или есть идея?"), 30, Color.white, TextAnchor.UpperLeft, bold: true);
            title.rectTransform.TopBand(60, 40, Pad, Pad);
            var sub = LGBuild.Label(_panel, Loc.T("Сообщение придёт разработчику на почту. Опишите, что делали и что пошло не так — так баг найдётся быстрее."),
                                    14, new Color(0.60f, 0.70f, 0.74f), TextAnchor.UpperLeft, wrap: true);
            sub.rectTransform.TopBand(106, 40, Pad, Pad);

            var close = LGControls.IconButton(_panel, LGIcon.Close, 40, UIManager.DS.BtnNeutral, new Color(1, 1, 1, 0.3f), Close);
            ((RectTransform)close.transform).At(new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -24), new Vector2(40, 40));

            var body = LGBuild.Rect(_panel, "Body");
            body.Stretch(Pad, 130, Pad, 166);
            float y = 0f;

            // Тип сообщения
            var cats = new[] { Loc.T("Баг"), Loc.T("Идея"), Loc.T("Другое") };
            var seg = MenuSegment.Create(body, cats, null, s_category, i => s_category = i);
            seg.GetComponent<RectTransform>().TopBand(y, 46);
            y += 46f + 16f;

            // Текст
            var msgHost = LGBuild.Rect(body, "Message");
            msgHost.TopBand(y, 220);
            _message = LGControls.Input(msgHost, s_draft,
                Loc.T("Что случилось? Что вы делали перед этим? Повторяется ли?"), MaxChars);
            _message.lineType = InputField.LineType.MultiLineNewline;
            _message.textComponent.alignment = TextAnchor.UpperLeft;
            _message.textComponent.horizontalOverflow = HorizontalWrapMode.Wrap;
            ((Text)_message.placeholder).alignment = TextAnchor.UpperLeft;
            ((Text)_message.placeholder).horizontalOverflow = HorizontalWrapMode.Wrap;
            _message.textComponent.rectTransform.parent.GetComponent<RectTransform>().Stretch(16, 12, 16, 12);
            // Esc в поле ввода откатывает текст к началу правки — во время закрытия изменения не принимаем
            _message.onValueChanged.AddListener(v => { if (_closing) return; s_draft = v; RefreshCounter(); });
            _counter = LGBuild.Label(body, "", 11, UIManager.DS.TextMuted, TextAnchor.UpperRight);
            _counter.rectTransform.TopBand(y + 224, 16);
            y += 220f + 26f;

            // Контакт
            var host = LGControls.Row(body, ref y, Loc.T("Контакт для ответа"), Loc.T("Почта, VK или Telegram — необязательно"), 360f);
            _contact = LGControls.Input(host, s_contact, Loc.T("необязательно"), 80);
            _contact.onValueChanged.AddListener(v => s_contact = v);

            host = LGControls.Row(body, ref y, Loc.T("Приложить сведения о системе"), Loc.T("Версия, ОС, видеокарта и журнал игры — помогает найти баг"), 160f);
            LGControls.Switch(host, s_attach, v => s_attach = v);

            // Низ: статус и кнопки
            _status = LGBuild.Label(_panel, "", 14, UIManager.DS.TextMuted, TextAnchor.MiddleLeft, wrap: true);
            _status.rectTransform.anchorMin = new Vector2(0, 0);
            _status.rectTransform.anchorMax = new Vector2(1, 0);
            _status.rectTransform.pivot = new Vector2(0.5f, 0);
            _status.rectTransform.offsetMin = new Vector2(Pad, 112);
            _status.rectTransform.offsetMax = new Vector2(-Pad, 148);

            _send = MenuPrimaryButton.Create(_panel, Loc.T("ОТПРАВИТЬ"), 18, Send);
            ((RectTransform)_send.transform).At(new Vector2(1, 0), new Vector2(1, 0), new Vector2(-Pad, 40), new Vector2(300, 56));

            _mail = LGBuild.Button(_panel, "Mail", UIManager.DS.BtnNeutral, new Color(0.7f, 0.8f, 0.85f, 0.7f),
                () => FeedbackSender.OpenMailClient(s_category, s_draft, s_contact, s_attach), null, Loc.T("ЧЕРЕЗ ПОЧТУ"), 13);
            ((RectTransform)_mail.transform).At(new Vector2(0, 0), new Vector2(0, 0), new Vector2(Pad, 43), new Vector2(220, 50));
            TooltipHelper.Attach(_mail.gameObject, Loc.F("Открыть почтовую программу с готовым письмом на {0}", FeedbackSender.Email));

            LG.Skin(_panel);
            RefreshCounter();
            _message.Select();
            _message.ActivateInputField();
        }

        private void RefreshCounter()
        {
            if (_counter == null) return;
            int n = (s_draft ?? "").Trim().Length;
            _counter.text = $"{(s_draft ?? "").Length} / {MaxChars}";
            _counter.color = n > 0 && n < MinChars ? UIManager.DS.Red : UIManager.DS.TextMuted;
            if (_send != null && !_sending) _send.Interactable = n >= MinChars;
        }

        private void SetStatus(string text, Color c)
        {
            _status.text = text;
            _status.color = c;
        }

        private void Send()
        {
            if (_sending || _closing) return;
            string msg = (s_draft ?? "").Trim();
            if (msg.Length < MinChars) { SetStatus(Loc.T("Опишите чуть подробнее — хотя бы пару слов."), UIManager.DS.Red); return; }
            if (Time.unscaledTime - s_lastSent < 30f) { SetStatus(Loc.T("Сообщение уже отправлено. Подождите полминуты перед следующим."), UIManager.DS.TextMuted); return; }
            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                Fail(Loc.T("Нет подключения к интернету."));
                return;
            }

            _sending = true;
            _send.Interactable = false;
            _send.SetLabel(Loc.T("ОТПРАВЛЯЕМ…"));
            SetStatus(Loc.T("Отправляем сообщение…"), UIManager.DS.TextMuted);
            StartCoroutine(FeedbackSender.Send(s_category, msg, s_contact, s_attach, (ok, err) =>
            {
                _sending = false;
                if (this == null) return;
                if (ok)
                {
                    s_lastSent = Time.unscaledTime;
                    _sentOk = true;
                    s_draft = "";
                    _message.text = "";
                    _send.SetLabel(Loc.T("ОТПРАВЛЕНО"));
                    _send.SetAction(Close);
                    _send.Interactable = true;
                    SetStatus(Loc.T("Спасибо! Сообщение отправлено — мы его прочитаем."), UIManager.DS.Green);
                }
                else Fail(Loc.T("Не удалось отправить."));
            }));
        }

        private void Fail(string why)
        {
            _failed = true;
            _send.SetLabel(Loc.T("ПОВТОРИТЬ"));
            _send.Interactable = true;
            SetStatus(why + " " + Loc.T("Попробуйте ещё раз или нажмите «Через почту» — откроется письмо с уже заполненным текстом."), UIManager.DS.Red);
        }

        private void Close()
        {
            if (_closing) return;
            _closing = true;
            IsOpen = false;
            if (_message != null && !_sentOk) s_draft = _lastText;
        }

        private void LateUpdate()
        {
            if (!_closing && _message != null) _lastText = _message.text;
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 20f);
            _k = LGEase.Spring(_k, _closing ? 0f : 1f, ref _kv, 4.5f, 0.9f, dt);
            _cg.alpha = Mathf.Clamp01(_k);
            _cg.blocksRaycasts = _cg.interactable = !_closing;
            if (_closing && _k < 0.01f) { Destroy(gameObject); return; }

            var r = _root.rect;
            var size = new Vector2(Mathf.Min(MaxW, r.width - 40f), Mathf.Min(MaxH, r.height - 30f));
            if ((_panel.sizeDelta - size).sqrMagnitude > 0.25f) _panel.sizeDelta = size;

            // Кнопка «Через почту» заметнее после неудачной отправки
            if (_mail != null) _mail.transform.localScale = Vector3.one * (_failed ? 1f + 0.02f * Mathf.Sin(Time.unscaledTime * 4f) : 1f);

            if (!_closing && Input.GetKeyDown(KeyCode.Escape)) { UIEsc.Consume(); Close(); }
        }
    }
}
