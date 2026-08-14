#if UNITY_EDITOR
using TJGenerators.Pipeline;
using UnityEditor;

namespace TJGenerators.Utils
{
    /// <summary>
    /// Blocks AI generation while the Editor is in Play mode (or entering it).
    /// Play-mode scene/asset changes are discarded on exit, so generation must run in Edit mode.
    /// </summary>
    public static class TJGeneratorsPlayModeGuard
    {
        public static bool IsActive => EditorApplication.isPlayingOrWillChangePlaymode;

        public static string Message =>
            TJGeneratorsL10n.L(
                "正在 Play 模式，无法使用 AI 生成。Play 模式下生成的内容会在退出后丢失，请退出 Play 模式后在编辑模式下使用。");

        public static string ShortHint =>
            TJGeneratorsL10n.L("Play 模式下不可生成，请退出 Play 模式");

        public static string SearchShortHint =>
            TJGeneratorsL10n.L("Play 模式下不可搜索，请退出 Play 模式");

        /// <summary>
        /// Unity 在 Play 模式下禁止 <c>AssetDatabase.ImportPackage</c>，下载到项目与放入场景均不可用。
        /// </summary>
        public static string DownloadOrPlaceShortHint =>
            TJGeneratorsL10n.L("Play 模式下不可下载或放入场景，请退出 Play 模式");

        /// <summary>
        /// 资产搜索窗口综合提示：搜索、下载、放置均受限，统一用一条文案。
        /// </summary>
        public static string AssetSearchAllBlockedHint =>
            TJGeneratorsL10n.L("Play 模式下不可搜索、下载或放入场景，请退出 Play 模式");

        /// <summary>
        /// If play mode is active, notifies via <paramref name="host"/> and returns true (caller should abort).
        /// </summary>
        public static bool TryBlock(IGenerationPipelineHost host)
        {
            if (!IsActive)
                return false;

            host?.ShowDialog(TJGeneratorsL10n.L("提示"), Message);
            return true;
        }
    }
}
#endif
