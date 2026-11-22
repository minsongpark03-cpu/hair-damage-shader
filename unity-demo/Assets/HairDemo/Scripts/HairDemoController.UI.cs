using System.Collections.Generic;
using UnityEngine;

namespace HairDemo
{
    /// <summary>
    /// 전시용 화면 UI. 반투명 유리(글래스) 패널 스타일.
    ///  - 오른쪽 카드: 머리색 / 헤어 모델 / 두상 / 결절성 열모증 / 루트 틴트 / 배경
    ///  - 아래 가운데: 손상도 슬라이더 (MIN ~ MAX)
    ///  - 위: 제목, 카메라 초기화 버튼
    /// 전시에 필요 없는 진단/튜닝 항목은 화면에서 모두 뺐다(기본값은 코드에 고정).
    /// 좌표는 1920x1080 기준 가상 좌표이며 화면 높이에 맞춰 크기가 조절된다.
    /// </summary>
    public partial class HairDemoController
    {
        const float UiW = 1920f, UiH = 1080f;

        static readonly Color Ink = new Color(0.17f, 0.19f, 0.28f, 1f);
        static readonly Color InkSoft = new Color(0.17f, 0.19f, 0.28f, 0.62f);
        static readonly Color Accent = new Color(0.25f, 0.43f, 0.98f, 1f);

        static readonly string[] ColorShortNames = { "고동", "밝은 갈색", "적갈", "금발" };

        // 마우스가 UI 위에 있는지 판별하기 위한 영역(화면 픽셀, 아래쪽이 y=0이 아니라 위쪽이 0인 GUI 좌표)
        readonly List<Rect> uiRectsPx = new List<Rect>();

        Texture2D texPanel, texShadow, texChip, texChipOn, texCircle, texRing, texTrack, texFill, texThumb, texKnob;
        GUIStyle stPanel, stShadow, stChip, stChipOn, stTitle, stSection, stSmall, stValue, stNone, stTrack, stFill;
        GUIStyle stChipText, stChipTextOn, stMsg, stSub, stLeft, stRight;
        GUIStyle stSliderBase, stThumb;
        bool uiReady;

        // ---------------------------------------------------------------- 텍스처 만들기
        // 둥근 사각형(안티앨리어싱 포함). 9-슬라이스로 늘려 쓰므로 한 번만 만든다.
        static Texture2D MakeRound(int w, int h, float radius, Color fill, Color border, float borderW)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[w * h];
            Vector2 half = new Vector2(w * 0.5f, h * 0.5f);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f) - half;
                Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - (half - Vector2.one * radius);
                float d = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
                float cover = Mathf.Clamp01(0.5f - d);
                float edge = Mathf.Clamp01(d + borderW + 0.5f);
                Color c = Color.Lerp(fill, border, edge);
                c.a *= cover;
                px[y * w + x] = c;
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        // 부드러운 그림자(가장자리로 갈수록 투명해지는 둥근 사각형)
        static Texture2D MakeShadow(int size, float inset, float radius, float maxAlpha, float blur)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            float halfSize = size * 0.5f;
            float inner = halfSize - inset;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(Mathf.Abs(x + 0.5f - halfSize), Mathf.Abs(y + 0.5f - halfSize)) - Vector2.one * (inner - radius);
                float d = new Vector2(Mathf.Max(p.x, 0f), Mathf.Max(p.y, 0f)).magnitude + Mathf.Min(Mathf.Max(p.x, p.y), 0f) - radius;
                float a = d <= 0f ? maxAlpha : maxAlpha * Mathf.Exp(-(d * d) / (blur * blur));
                px[y * size + x] = new Color(0f, 0f, 0f, a);
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        void BuildUi()
        {
            var white = Color.white;
            texPanel  = MakeRound(96, 96, 30f, new Color(1f, 1f, 1f, 0.55f), new Color(1f, 1f, 1f, 0.85f), 1.6f);
            texShadow = MakeShadow(160, 56f, 30f, 0.10f, 22f);
            texChip   = MakeRound(64, 64, 22f, new Color(1f, 1f, 1f, 0.50f), new Color(1f, 1f, 1f, 0.9f), 1.4f);
            texChipOn = MakeRound(64, 64, 22f, Accent, new Color(1f, 1f, 1f, 0.9f), 1.4f);
            texCircle = MakeRound(96, 96, 48f, white, new Color(1f, 1f, 1f, 1f), 0f);
            texRing   = MakeRound(96, 96, 48f, new Color(1f, 1f, 1f, 0f), Accent, 5f);
            // 슬라이더 트랙/채움: 높이 12px짜리 알약 모양(9-슬라이스, 양 끝 6px)
            texTrack  = MakeRound(24, 12, 6f, new Color(0.17f, 0.19f, 0.28f, 0.16f), new Color(0.17f, 0.19f, 0.28f, 0.16f), 0f);
            texFill   = MakeRound(24, 12, 6f, Accent, Accent, 0f);
            texThumb  = MakeRound(96, 96, 48f, white, new Color(0.25f, 0.43f, 0.98f, 1f), 7f);
            texKnob   = MakeRound(96, 96, 48f, white, new Color(0.85f, 0.87f, 0.93f, 1f), 3f);

            stNone = new GUIStyle();
            stPanel = new GUIStyle { border = new RectOffset(30, 30, 30, 30) };
            stPanel.normal.background = texPanel;
            stShadow = new GUIStyle { border = new RectOffset(56, 56, 56, 56) };
            stShadow.normal.background = texShadow;

            stChip = new GUIStyle { border = new RectOffset(22, 22, 22, 22), alignment = TextAnchor.MiddleCenter, fontSize = 22, fontStyle = FontStyle.Bold };
            stChip.normal.background = texChip; stChip.normal.textColor = Ink;
            stChip.hover.background = texChip; stChip.hover.textColor = Ink;
            stChip.active.background = texChip; stChip.active.textColor = Ink;
            stChipOn = new GUIStyle(stChip);
            stChipOn.normal.background = texChipOn; stChipOn.normal.textColor = Color.white;
            stChipOn.hover.background = texChipOn; stChipOn.hover.textColor = Color.white;
            stChipOn.active.background = texChipOn; stChipOn.active.textColor = Color.white;

            stTitle = new GUIStyle { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            stTitle.normal.textColor = Ink;
            stSection = new GUIStyle { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            stSection.normal.textColor = InkSoft;
            stSmall = new GUIStyle { fontSize = 18, alignment = TextAnchor.UpperCenter, wordWrap = false };
            stSmall.normal.textColor = InkSoft;
            stValue = new GUIStyle { fontSize = 44, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            stValue.normal.textColor = Ink;

            // 칩 안의 글자(배경은 stChip/stChipOn이 그리고, 글자는 UiLabel이 그린다)
            stChipText = new GUIStyle { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            stChipText.normal.textColor = Ink;
            stChipTextOn = new GUIStyle(stChipText);
            stChipTextOn.normal.textColor = Color.white;

            // 매 프레임 새로 만들면 쓰레기가 쌓이므로 한 번만 만들어 둔다
            stMsg = new GUIStyle(stTitle) { fontSize = 28, alignment = TextAnchor.MiddleCenter };
            stSub = new GUIStyle(stSmall) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
            stLeft = new GUIStyle(stSmall) { alignment = TextAnchor.MiddleLeft };
            stRight = new GUIStyle(stSmall) { alignment = TextAnchor.MiddleRight };

            stTrack = new GUIStyle { border = new RectOffset(6, 6, 6, 6) };
            stTrack.normal.background = texTrack;
            stFill = new GUIStyle { border = new RectOffset(6, 6, 6, 6) };
            stFill.normal.background = texFill;

            stSliderBase = new GUIStyle();
            stThumb = new GUIStyle { fixedWidth = 52f, fixedHeight = 52f };
            stThumb.normal.background = texThumb;
            stThumb.hover.background = texThumb;
            stThumb.active.background = texThumb;

            uiReady = true;
        }

        // ---------------------------------------------------------------- 그리기 도우미
        Rect Region(Rect r)
        {
            // 마우스가 UI 위에 있을 땐 카메라가 같이 움직이지 않게 영역을 기록한다(Layout 이벤트에서 목록을 비우고 다시 채운다).
            if (Event.current.type == EventType.Layout)
            {
                float s = Screen.height / UiH;
                uiRectsPx.Add(new Rect(r.x * s, r.y * s, r.width * s, r.height * s));
            }
            return r;
        }

        void Glass(Rect r)
        {
            GUI.Box(new Rect(r.x - 40f, r.y - 26f, r.width + 80f, r.height + 80f), GUIContent.none, stShadow);
            GUI.Box(r, GUIContent.none, stPanel);
        }

        bool Chip(Rect r, string text, bool on)
        {
            // 배경과 클릭은 둥근 칩 스타일로, 글자는 화면 픽셀 크기로 따로 그려서 선명하게 한다.
            bool clicked = GUI.Button(r, GUIContent.none, on ? stChipOn : stChip);
            UiLabel(r, text, on ? stChipTextOn : stChipText);
            return clicked;
        }

        // ---------------------------------------------------------------- 글자
        // GUI.matrix로 UI 전체를 확대/축소하면 글자도 한 번 그린 비트맵을 늘이거나 줄여서 흐릿해진다.
        // 글자만 행렬 없이 "화면 픽셀 단위 크기"의 글꼴로 직접 그려서 해상도와 상관없이 또렷하게 한다.
        readonly Dictionary<GUIStyle, GUIStyle> scaledStyles = new Dictionary<GUIStyle, GUIStyle>();
        int scaledStylesScreenH = -1;

        GUIStyle PixelStyle(GUIStyle src, float scale)
        {
            if (scaledStylesScreenH != Screen.height) { scaledStyles.Clear(); scaledStylesScreenH = Screen.height; }
            if (!scaledStyles.TryGetValue(src, out var s))
            {
                s = new GUIStyle(src);
                s.fontSize = Mathf.Max(8, Mathf.RoundToInt(src.fontSize * scale));
                scaledStyles[src] = s;
            }
            return s;
        }

        void UiLabel(Rect r, string text, GUIStyle style)
        {
            if (Event.current.type != EventType.Repaint) return; // 글자는 그릴 때만 필요하다
            float sc = Screen.height / UiH;
            var prevMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            GUI.Label(new Rect(r.x * sc, r.y * sc, r.width * sc, r.height * sc), text, PixelStyle(style, sc));
            GUI.matrix = prevMatrix;
        }

        // 색 동그라미 버튼 (선택되면 파란 테두리)
        bool Swatch(Rect r, Color color, bool on)
        {
            var prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.95f);
            GUI.DrawTexture(new Rect(r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f), texKnob);
            GUI.color = color;
            GUI.DrawTexture(r, texCircle);
            GUI.color = Accent;
            if (on) GUI.DrawTexture(new Rect(r.x - 6f, r.y - 6f, r.width + 12f, r.height + 12f), texRing);
            GUI.color = prev;
            return GUI.Button(r, GUIContent.none, stNone);
        }

        // 켜짐/꺼짐 스위치
        bool Switch(Rect r, bool on)
        {
            var prev = GUI.color;
            if (on)
            {
                GUI.Box(r, GUIContent.none, stChipOn);
            }
            else
            {
                GUI.color = new Color(0.55f, 0.57f, 0.66f, 1f);
                GUI.Box(r, GUIContent.none, stChip);
            }
            float k = r.height - 10f;
            float kx = on ? r.xMax - k - 5f : r.x + 5f;
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(kx, r.y + 5f, k, k), texCircle);
            GUI.color = prev;
            return GUI.Button(r, GUIContent.none, stNone) ? !on : on;
        }

        // ---------------------------------------------------------------- 로딩
        // 머리카락 파일을 읽는 동안은 화면이 잠시 멈춘다. 로딩창이 먼저 화면에 그려지도록 읽기를 2프레임 미룬다.
        System.Action pendingLoad;
        int pendingLoadFrame;
        string pendingLoadMessage = "";
        bool wasBusy;
        float busyStart;

        void QueueLoad(string message, System.Action action)
        {
            pendingLoadMessage = message;
            pendingLoad = action;
            pendingLoadFrame = Time.frameCount + 2;
        }

        void RequestHairSlot(int slot)
        {
            if (pendingLoad != null) return; // 이미 불러오는 중
            slot = Mathf.Clamp(slot, 0, HairFiles.Length - 1);
            if (slot == hairSlot && currentHair != null) return;
            QueueLoad("머리카락 모델을 불러오는 중…", () => LoadHairSlot(slot));
        }

        void RequestHeadSlot(int slot)
        {
            if (pendingLoad != null) return;
            slot = Mathf.Clamp(slot, 0, HeadSpecs.Length - 1);
            if (slot == headSlot && headGO != null) return;
            QueueLoad("두상을 불러오는 중…", () => LoadHeadSlot(slot));
        }

        void RunPendingLoad()
        {
            if (pendingLoad == null || Time.frameCount < pendingLoadFrame) return;
            var action = pendingLoad;
            pendingLoad = null;
            action();
        }

        // 지금 무언가 계산/로딩 중인가, 그렇다면 로딩창에 띄울 문구
        bool IsBusy(out string message)
        {
            if (pendingLoad != null) { message = pendingLoadMessage; return true; }
            if (fitTask != null || snapTask != null || snapDirty)
            {
                message = "머리카락을 두상에 맞추는 중…";
                return true;
            }
            message = "";
            return false;
        }

        // ---------------------------------------------------------------- 화면
        void OnGUI()
        {
            if (!uiReady) BuildUi();
            if (Event.current.type == EventType.Layout) uiRectsPx.Clear();

            float sc = Screen.height / UiH;
            GUI.matrix = Matrix4x4.Scale(new Vector3(sc, sc, 1f));
            // 화면이 가로로 더 넓어도 UI는 가상 좌표(1920 폭) 기준으로 오른쪽 끝에 맞춘다.
            float virtualW = Screen.width / sc;
            float right = virtualW - 40f;

            bool busy = IsBusy(out string busyMessage);
            if (busy && !wasBusy) busyStart = Time.realtimeSinceStartup;
            wasBusy = busy;

            // 로딩 중에는 아래 UI가 눌리지 않게 비활성화하고 위에 로딩창을 그린다.
            bool prevEnabled = GUI.enabled;
            GUI.enabled = !busy;
            DrawTopBar(right);
            DrawRightCard(right);
            DrawDamageBar(virtualW);
            GUI.enabled = prevEnabled;

            if (busy) DrawLoadingOverlay(virtualW, busyMessage);
        }

        void DrawLoadingOverlay(float virtualW, string message)
        {
            // 화면 전체를 덮어서 카메라 조작도 막는다.
            Region(new Rect(0f, 0f, virtualW, UiH));

            var prev = GUI.color;
            GUI.color = new Color(0.10f, 0.12f, 0.20f, 0.35f);
            GUI.DrawTexture(new Rect(0f, 0f, virtualW, UiH), Texture2D.whiteTexture);
            GUI.color = prev;

            const float w = 560f, h = 240f;
            var card = new Rect((virtualW - w) * 0.5f, (UiH - h) * 0.5f, w, h);
            Glass(card);
            GUI.Box(card, GUIContent.none, stPanel); // 배경이 어두워져도 카드가 또렷하도록 한 겹 더

            // 회전하는 점 10개 (뒤따라오는 점일수록 흐려진다)
            Vector2 center = new Vector2(card.center.x, card.y + 84f);
            float t = Time.realtimeSinceStartup;
            for (int i = 0; i < 10; i++)
            {
                float ang = (i / 10f) * Mathf.PI * 2f - Mathf.PI * 0.5f;
                float fade = Mathf.Repeat(t * 1.3f - i / 10f, 1f);
                float a = Mathf.Pow(1f - fade, 2.2f);
                float size = Mathf.Lerp(10f, 20f, 1f - fade);
                var pos = center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 36f;
                GUI.color = new Color(Accent.r, Accent.g, Accent.b, 0.15f + 0.85f * a);
                GUI.DrawTexture(new Rect(pos.x - size * 0.5f, pos.y - size * 0.5f, size, size), texCircle);
            }
            GUI.color = prev;

            UiLabel(new Rect(card.x, card.y + 138f, card.width, 44f), message, stMsg);

            float elapsed = Time.realtimeSinceStartup - busyStart;
            UiLabel(new Rect(card.x, card.y + 184f, card.width, 30f), $"잠시만 기다려 주세요  ·  {elapsed:0.0}초", stSub);
        }

        void DrawTopBar(float right)
        {
            // 제목
            var title = Region(new Rect(40f, 32f, 600f, 84f));
            Glass(title);
            GUI.color = Accent;
            GUI.DrawTexture(new Rect(title.x + 22f, title.y + 22f, 40f, 40f), texCircle);
            GUI.color = Color.white;
            UiLabel(new Rect(title.x + 82f, title.y, title.width - 90f, title.height), "머리카락 손상 시뮬레이터", stTitle);

            // 카메라 초기화
            var reset = Region(new Rect(right - 240f, 38f, 240f, 70f));
            GUI.Box(new Rect(reset.x - 20f, reset.y - 14f, reset.width + 40f, reset.height + 50f), GUIContent.none, stShadow);
            if (Chip(reset, "카메라 초기화", true)) ResetCamera();
        }

        void DrawRightCard(float right)
        {
            const float w = 420f;
            const float pad = 32f;
            float x = right - w;
            float y = 126f;

            // 모델 개수에 따라 카드 높이가 달라진다
            const int cols = 4;
            int modelRows = Mathf.Max(1, (HairFiles.Length + cols - 1) / cols);
            float h = pad + 34f + 96f            // 머리색
                    + 20f + 34f + modelRows * 64f + 10f   // 헤어 모델
                    + 20f + 34f + 64f            // 두상
                    + 20f + 56f                  // 결절성 열모증
                    + 56f + 20f                  // 루트 틴트
                    + 20f + 34f + 76f            // 배경
                    + pad;
            var card = Region(new Rect(x, y, w, h));
            Glass(card);

            float cx = x + pad;
            float cw = w - pad * 2f;
            float cy = y + pad;

            // --- 머리색
            UiLabel(new Rect(cx, cy, cw, 34f), "머리색", stSection);
            cy += 40f;
            float sw = 60f, gap = (cw - sw * Presets.Length) / (Presets.Length - 1);
            for (int i = 0; i < Presets.Length; i++)
            {
                float sx = cx + i * (sw + gap);
                // 어두운 머리색이 검정으로만 보이지 않도록 화면 표시용으로 살짝 밝힌다
                var c = Presets[i].color;
                var shown = new Color(Mathf.Pow(c.r, 0.62f), Mathf.Pow(c.g, 0.62f), Mathf.Pow(c.b, 0.62f), 1f);
                if (Swatch(new Rect(sx, cy + 4f, sw, sw), shown, i == colorIndex)) colorIndex = i;
                var name = i < ColorShortNames.Length ? ColorShortNames[i] : Presets[i].name;
                UiLabel(new Rect(sx - gap * 0.5f, cy + 72f, sw + gap, 24f), name, stSmall);
            }
            cy += 96f + 20f;

            // --- 헤어 모델
            UiLabel(new Rect(cx, cy, cw, 34f), "헤어 모델", stSection);
            cy += 40f;
            float chipW = (cw - 12f * (cols - 1)) / cols;
            for (int i = 0; i < HairFiles.Length; i++)
            {
                int row = i / cols, col = i % cols;
                var r = new Rect(cx + col * (chipW + 12f), cy + row * 64f, chipW, 52f);
                if (Chip(r, (i + 1).ToString(), i == hairSlot)) RequestHairSlot(i);
            }
            cy += modelRows * 64f + 10f + 20f;

            // --- 두상
            UiLabel(new Rect(cx, cy, cw, 34f), "두상", stSection);
            cy += 40f;
            float headW = (cw - 12f * (HeadSpecs.Length - 1)) / HeadSpecs.Length;
            for (int i = 0; i < HeadSpecs.Length; i++)
            {
                var r = new Rect(cx + i * (headW + 12f), cy, headW, 52f);
                if (Chip(r, HeadSpecs[i].label, i == headSlot)) RequestHeadSlot(i);
            }
            cy += 64f + 20f;

            // --- 결절성 열모증
            UiLabel(new Rect(cx, cy, cw - 110f, 40f), "결절성 열모증", stSection);
            nodosaOn = Switch(new Rect(cx + cw - 96f, cy, 96f, 46f), nodosaOn);
            cy += 56f + 20f;

            // --- 루트 틴트
            UiLabel(new Rect(cx, cy, cw - 110f, 40f), "루트 틴트", stSection);
            rootTint = Switch(new Rect(cx + cw - 96f, cy, 96f, 46f), rootTint);
            cy += 56f + 20f;

            // --- 배경
            UiLabel(new Rect(cx, cy, cw, 34f), "배경", stSection);
            cy += 40f;
            float bsw = 52f, bgap = (cw - bsw * Backgrounds.Length) / (Backgrounds.Length - 1);
            for (int i = 0; i < Backgrounds.Length; i++)
            {
                var r = new Rect(cx + i * (bsw + bgap), cy + 6f, bsw, bsw);
                if (Swatch(r, Backgrounds[i].color, i == backgroundIndex)) backgroundIndex = i;
            }

        }

        void DrawDamageBar(float virtualW)
        {
            const float w = 1000f, h = 130f;
            var bar = Region(new Rect((virtualW - w) * 0.5f, UiH - h - 44f, w, h));
            Glass(bar);

            float x = bar.x + 44f, y = bar.y;
            UiLabel(new Rect(x, y + 14f, 300f, 36f), "손상도", stSection);

            // MIN ----●---- MAX
            var minL = new Rect(x, y + 70f, 56f, 30f);
            var maxL = new Rect(bar.xMax - 44f - 190f - 76f, y + 70f, 56f, 30f);
            UiLabel(minL, "MIN", stLeft);
            UiLabel(maxL, "MAX", stRight);

            float trackX = minL.xMax + 18f;
            float trackW = maxL.x - 18f - trackX;
            const float thumb = 52f;
            var slider = new Rect(trackX, y + 59f, trackW, thumb);

            // 트랙과 채워진 부분은 직접 그리고, 슬라이더(손잡이)는 IMGUI 기본 동작을 쓴다
            var track = new Rect(trackX + thumb * 0.5f, slider.y + thumb * 0.5f - 6f, trackW - thumb, 12f);
            GUI.Box(track, GUIContent.none, stTrack);
            GUI.Box(new Rect(track.x, track.y, Mathf.Max(12f, track.width * damage), track.height), GUIContent.none, stFill);
            damage = GUI.HorizontalSlider(slider, damage, 0f, 1f, stSliderBase, stThumb);

            UiLabel(new Rect(bar.xMax - 44f - 190f, y + 28f, 190f, 76f), $"{Mathf.RoundToInt(damage * 100f)}%", stValue);
        }
    }
}
