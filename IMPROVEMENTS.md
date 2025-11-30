# 可改进点概览
- **渲染/测量 UI 反馈**：测量结果目前通过状态栏与通知展示，缺少持久的历史列表或导出功能。可以在侧边栏增加测量记录面板并提供导出按钮，方便回溯与分享。【F:PointCloudViz_Final/MainWindow.xaml.cs†L120-L197】
- **流式下采样 UI 配置**：流式 LAS 读取器已支持通过 `TargetPointCount` 属性调整下采样步长，但尚未暴露到设置界面或文件导入对话框。后续可添加 UI 控件或配置文件项，让终端用户无需修改代码即可控制精度/性能权衡。【F:PointCloudViz_Final/IO/StreamingLasReader.cs†L11-L145】
- **LAS 额外字段利用**：LAS 读取器已经兼容 1.4 与扩展点计数，但仍忽略了回波数、波形数据偏移等扩展字段。结合业务需求可以进一步解析这些字段，启用按回波或强度的高级可视化模式。【F:PointCloudViz_Final/IO/StreamingLasReader.cs†L81-L145】
