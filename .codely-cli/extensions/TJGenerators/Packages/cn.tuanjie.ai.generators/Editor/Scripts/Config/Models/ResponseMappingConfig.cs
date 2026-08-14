using System;

namespace TJGenerators.Config
{
    /// <summary>
    /// API响应字段映射
    /// </summary>
    [Serializable]
    public class ResponseMappingConfig
    {
        public string downloadUrlPath;
        public string downloadUrlPathMultiview;
        public string previewUrlPath;
        public string renderedImagePath;  // 渲染贴图URL路径（用于FBX主贴图）
        public string taskIdPath;
        public string progressPath;
        public string statusPath;

        // 动画相关字段（用于Meshy动画模型）
        public string animationUrlPath;  // 动画模型URL路径，如 "result.animation_fbx_url"
        public string walkingAnimationUrlPath;  // 行走动画URL路径，如 "result.basic_animations.walking_fbx_url"
        public string runningAnimationUrlPath;  // 奔跑动画URL路径，如 "result.basic_animations.running_fbx_url"
    }
}
