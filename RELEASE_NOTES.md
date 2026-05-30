# v1.0.0 - 三维点云可视化作品集初版

这是 `PointCloudViz_Final` 的第一个正式 Release。项目最初来自面向对象程序设计课程期末作品，现整理为一个可展示的 C# / WPF 三维点云可视化桌面项目。

## 发布亮点

- 基于 `.NET 8`、`WPF` 和 `HelixToolkit.Wpf.SharpDX` 实现三维点云可视化。
- 支持 `.xyz`、`.txt`、ASCII `.ply` 和 `.las` 点云文件读取。
- 支持将当前点云导出为 `.xyz`。
- 支持按高程和按强度进行颜色映射。
- 支持黑色、白色、灰色背景切换和点大小调整。
- 支持 Z 范围过滤、体素下采样、半径离群点剔除和恢复原始数据。
- 支持两点距离测量、多点面积测量、测量点高亮和测量线显示。
- 支持鼠标旋转、平移、缩放，以及 `WASD` / `QE` 键盘视角移动。
- 支持 JSON 项目配置保存与恢复。
- 支持生成合成街景点云，用于快速测试渲染和交互效果。
- 包含 `sample_final.xyz` 和 `sample_final.ply` 示例点云数据。

## 主要改进记录

- 修复大坐标点云渲染黑屏问题：加载后根据包围盒中心进行坐标归一化。
- 改进相机控制：禁用默认冲突手势，加入更接近 CloudCompare 的交互方式。
- 改进滚轮缩放和相机状态同步，减少缩放后视角被重置的问题。
- 增加加载遮罩、状态栏和顶部通知条，提升长耗时操作的反馈。
- 增加流式 LAS 读取和大点云下采样提示，降低大文件加载时的内存压力。

## 运行环境

推荐环境：

- Windows 10 / Windows 11
- .NET 8 SDK
- Visual Studio 2022 或更高版本
- 支持 DirectX 11 的显卡或集成显卡

## 使用方式

1. 下载源码或 Release 附件。
2. 使用 Visual Studio 打开 `PointCloudViz_Final.sln`。
3. 等待 NuGet 依赖还原完成。
4. 启动 `PointCloudViz_Final` 项目。
5. 通过 `文件 -> 打开点云...` 加载示例或自己的点云文件。

命令行构建方式：

```powershell
dotnet restore
dotnet build
dotnet run --project .\PointCloudViz_Final\PointCloudViz_Final.csproj
```

## 已知限制

- PLY 读取器当前仅支持 ASCII PLY。
- 内置 LAS 支持主要覆盖 LAS 1.0-1.3，LAS 1.4 和扩展字段支持有限。
- 超大规模点云仍主要依赖抽稀和体素下采样，尚未实现完整的分块瓦片加载。
- 项目保存功能主要保存数据路径和显示配置，不是完整点云工程格式。
- 当前 Release 以源码发布为主，暂未提供独立安装包。

## 后续计划

- 补充项目截图和演示 GIF。
- 清理仓库中的构建中间文件和开发日志。
- 添加 `.gitignore`、许可证和 GitHub Actions 构建流程。
- 增强 LAS 1.4、二进制 PLY 和更大规模点云处理能力。
- 增加测量历史列表、结果导出和更多空间裁剪工具。
