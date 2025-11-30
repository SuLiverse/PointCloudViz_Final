using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PointCloudViz_Final.Models;

namespace PointCloudViz_Final.Rendering
{
    /// <summary>渲染器管理器：自动选择GPU或CPU渲染器</summary>
    public class RendererManager : IRenderer
    {
        private readonly IRenderer _gpuRenderer;
        private readonly IRenderer _cpuRenderer;
        private bool _useGpu = true;
        private bool _gpuInitialized = false;

        public RendererManager()
        {
            // 优先使用GPU优化渲染器
            _gpuRenderer = new GpuOptimizedRenderer();
            _cpuRenderer = new SoftwarePointRenderer();
        }

        public bool IsUsingGpu => _useGpu && _gpuInitialized;

        public async Task<WriteableBitmap> RenderAsync(
            PointCloud cloud,
            Camera camera,
            int width, int height,
            IColorMap colorMap,
            int pointSize,
            Color background,
            CancellationToken token,
            bool isInteracting = false)
        {
            // 如果GPU可用且未初始化失败，尝试使用GPU
            if (_useGpu && !_gpuInitialized)
            {
                try
                {
                    var result = await _gpuRenderer.RenderAsync(cloud, camera, width, height, colorMap, pointSize, background, token, isInteracting);
                    if (result != null)
                    {
                        _gpuInitialized = true;
                        return result;
                    }
                }
                catch (Exception)
                {
                    // GPU渲染失败，标记为不可用
                    _useGpu = false;
                }
            }

            // GPU不可用或失败，回退到CPU渲染
            if (!_useGpu || !_gpuInitialized)
            {
                return await _cpuRenderer.RenderAsync(cloud, camera, width, height, colorMap, pointSize, background, token, isInteracting);
            }

            // 如果GPU已初始化，继续使用GPU
            try
            {
                var result = await _gpuRenderer.RenderAsync(cloud, camera, width, height, colorMap, pointSize, background, token, isInteracting);
                if (result != null)
                {
                    return result;
                }
            }
            catch (Exception)
            {
                // GPU渲染失败，回退到CPU
                _useGpu = false;
                _gpuInitialized = false;
            }

            // 最终回退到CPU
            return await _cpuRenderer.RenderAsync(cloud, camera, width, height, colorMap, pointSize, background, token, isInteracting);
        }

        /// <summary>强制使用CPU渲染</summary>
        public void ForceCpu()
        {
            _useGpu = false;
            _gpuInitialized = false;
        }

        /// <summary>尝试重新启用GPU渲染</summary>
        public void TryEnableGpu()
        {
            _useGpu = true;
            _gpuInitialized = false;
        }
    }
}

