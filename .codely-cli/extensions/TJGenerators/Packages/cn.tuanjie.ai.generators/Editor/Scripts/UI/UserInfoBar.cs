#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using TJGenerators.Utils;

namespace TJGenerators.UI
{
    /// <summary>
    /// 左侧面板底部用户信息栏：邮箱与点数（同一行）。
    /// </summary>
    public static class UserInfoBar
    {
        private const float BarHeight = 56f;
        private const float ContentRowHeight = 40f;
        private const float HorizontalPadding = 16f;
        private const float EmailCreditsGap = 8f;
        private const float CreditsMinWidth = 78f;
        private const string DefaultEmailPlaceholder = "user123@unity.cn";

        private static GUIStyle s_emailRowStyle;

        /// <summary>点数文案宽度缓存；仅在文本变化时重新 CalcSize。</summary>
        public struct CreditsTextLayoutCache
        {
            string m_text;
            float m_width;

            public float Measure(string text, GUIStyle style)
            {
                if (text != m_text)
                {
                    m_text = text;
                    m_width = style.CalcSize(new GUIContent(text)).x;
                }
                return m_width;
            }
        }

        public static float Height => BarHeight;

        /// <summary>
        /// 绘制用户信息栏。email 为 null 时使用 <see cref="UserInfoHelper.LastUserInfo"/>。
        /// </summary>
        public static void Draw(
            float windowHeight,
            float leftPanelWidth,
            bool hasLoadedUserInfo,
            int currentCredits,
            ref CreditsTextLayoutCache creditsCache,
            string email = null)
        {
            Rect barRect = GetBarRect(windowHeight, leftPanelWidth);
            EditorGUI.DrawRect(barRect, CommonStyles.WindowBackgroundColor);

            Rect rowRect = GetContentRowRect(barRect);
            string emailText = ResolveEmail(email);
            string creditsText = FormatCreditsText(hasLoadedUserInfo, currentCredits);

            var creditsStyle = CommonStyles.BottomStatusBarCreditsStyle;
            float creditsWidth = MeasureCreditsWidth(
                creditsText, leftPanelWidth, creditsStyle, ref creditsCache);

            LayoutEmailAndCredits(rowRect, leftPanelWidth, creditsWidth,
                out Rect emailRect, out Rect creditsRect);

            GUI.Label(emailRect, emailText, GetEmailRowStyle());
            GUI.Label(creditsRect, creditsText, creditsStyle);
        }

        private static Rect GetBarRect(float windowHeight, float panelWidth) =>
            new Rect(0f, windowHeight - BarHeight, panelWidth, BarHeight);

        private static Rect GetContentRowRect(Rect barRect)
        {
            float rowY = barRect.y + (BarHeight - ContentRowHeight) * 0.5f;
            return new Rect(barRect.x, rowY, barRect.width, ContentRowHeight);
        }

        private static string FormatCreditsText(bool hasLoadedUserInfo, int currentCredits) =>
            hasLoadedUserInfo ? $"{TJGeneratorsL10n.L("点数：")}{currentCredits}" : TJGeneratorsL10n.L("点数：--");

        private static float MeasureCreditsWidth(
            string creditsText,
            float panelWidth,
            GUIStyle creditsStyle,
            ref CreditsTextLayoutCache cache)
        {
            float maxWidth = Mathf.Max(CreditsMinWidth, panelWidth - HorizontalPadding * 2f);
            float textWidth = cache.Measure(creditsText, creditsStyle);
            return Mathf.Clamp(textWidth, CreditsMinWidth, maxWidth);
        }

        private static void LayoutEmailAndCredits(
            Rect rowRect,
            float panelWidth,
            float creditsWidth,
            out Rect emailRect,
            out Rect creditsRect)
        {
            creditsRect = new Rect(
                panelWidth - HorizontalPadding - creditsWidth,
                rowRect.y,
                creditsWidth,
                rowRect.height);

            float emailWidth = Mathf.Max(1f, creditsRect.x - EmailCreditsGap - HorizontalPadding);
            emailRect = new Rect(HorizontalPadding, rowRect.y, emailWidth, rowRect.height);
        }

        private static string ResolveEmail(string email)
        {
            if (!string.IsNullOrEmpty(email))
                return email;

            string userEmail = UserInfoHelper.LastUserInfo?.email;
            return string.IsNullOrEmpty(userEmail) ? DefaultEmailPlaceholder : userEmail;
        }

        private static GUIStyle GetEmailRowStyle()
        {
            if (s_emailRowStyle == null)
            {
                s_emailRowStyle = new GUIStyle(CommonStyles.ProfileEmailStyle)
                {
                    alignment = TextAnchor.MiddleLeft
                };
            }
            return s_emailRowStyle;
        }
    }
}
#endif